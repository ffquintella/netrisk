# Secrets Inventory and Rotation

> Track 7 milestone 7.3.3 · First issued 2026-08-26
> Related: [DATA_PROTECTION.md](DATA_PROTECTION.md) · [FINDINGS.md](FINDINGS.md) NR-2026-003, NR-2026-025

Every secret NetRisk touches, where it lives, and exactly how to rotate it. If a value is not in this
table, it is not a secret NetRisk is aware of — which is itself worth reporting.

---

## 1. The rule

**Development:** .NET user-secrets. Never a file inside the repository.
**Production:** environment variables, or a secret store the process reads at start-up.
**Never:** `appsettings*.json` committed to a repository, a wiki, a chat message, or a CI log.

.NET configuration binds `Database__ConnectionString` (double underscore) to
`Database:ConnectionString`, so every value below can be supplied as an environment variable with no
code change.

```bash
# Development
cd src/API && dotnet user-secrets init
dotnet user-secrets set "Database:ConnectionString" "server=...;uid=...;pwd=...;database=netrisk;ConvertZeroDateTime=True"

# Production (systemd EnvironmentFile, mode 0600, owned by the service account)
Database__ConnectionString=server=...;uid=...;pwd=...;database=netrisk;ConvertZeroDateTime=True
https__certificate__password=...
```

---

## 2. Inventory

| Secret | Lives in | Set by | Read by | Rotation |
|---|---|---|---|---|
| **Database connection string** (contains the password) | Environment `Database__ConnectionString`, or user-secrets in development | Operator | API, BackgroundJobs, ConsoleClient, WebSite | §3.1 |
| **JWT signing key** | `<AppData>/NRServer/secret_token.txt`, generated on first start | Generated — 32 characters of CSPRNG output, base64-encoded | `EnvironmentService.ServerSecretToken` → `AuthenticationBootstrapper`, `SecretProtector` | §3.2 — **read the warning** |
| **TLS certificate password** | Environment `https__certificate__password` | Operator | `Program.cs` on both hosts | §3.3 |
| **TLS private key** | Operator-supplied `.pfx` outside the repository | Operator / CA | Kestrel | §3.3 |
| **SMTP credentials** | Environment under `email:` | Operator | `AddSmtpSender` | Provider-dependent; no NetRisk state |
| **Integration credentials** — Slack/Teams webhooks, Jira & Azure DevOps tokens, Trend Micro & SecurityScorecard API keys | Database, AES-256-GCM, in `encrypted_*` columns | Administrator through the UI | The Track 4 services via `ISecretProtector` | §3.4 |
| **Webhook shared secrets** (inbound, unsigned providers) | Same | Generated per connection | `IssueTrackerService.ApplyWebhookAsync` | §3.4 |
| **CI API tokens** (`nrk_…`) | Database as SHA-256; the plaintext exists once, at issue | User through `POST /ApiTokens` | `ApiTokenAuthenticationHandler` | §3.5 |
| **SCIM provisioning tokens** (`scim_…`) | Same | Administrator through `POST /ScimTokens` | `ScimAuthenticationHandler` | §3.5 |
| **Website sync keys** (Ed25519) | `<AppData>/NRServer/` private, enrolled public on the website | Generated | `SyncKeyService`, `SyncSignatureVerifier` | §3.6 — has a built-in rotation flow |
| **MFA recovery codes** | Database as SHA-256, single-use | Generated per batch | `WebAuthnService` | Regenerate a batch; the old batch is invalidated |
| **Code-signing certificates** | CI secret store; `NETRISK_*` environment variables | Release engineer | Track 5 signing targets | Per CA policy; see [release-engineering.md](../packaging/release-engineering.md) |
| **Password hashes** | `user.password`, bcrypt cost 15 | The application | `UsersService.VerifyPassword` | Not a rotatable secret; a change is a password change |
| **Biometric templates** | `faceid_users`, **not encrypted at column level** — NR-2026-032 | The FaceID plugin | `FaceIDService` | Not rotatable. That is the finding. |

---

## 3. Rotation procedures

### 3.1 Database password

1. Create the new grant on MariaDB, or change the password for the existing account.
2. Update `Database__ConnectionString` on every host that has one: **API, BackgroundJobs,
   ConsoleClient, WebSite**. Missing one produces a service that looks healthy until its next query.
3. Restart in that order; BackgroundJobs last, so a job does not start mid-rotation.
4. Verify with `netrisk-console database baseline`, which reports the version and connectivity
   without mutating anything.

No application state depends on the password, so this is safe to do at any time.

### 3.2 JWT signing key — **destructive, read first**

The key at `<AppData>/NRServer/secret_token.txt` is used for two things: signing session tokens
**and**, through a domain-separated derivation, encrypting integration credentials
(`SecretProtector`). Deleting it therefore does two things at once:

* every outstanding session token becomes invalid — acceptable, users sign in again;
* **every stored integration credential becomes undecryptable** — not acceptable without preparation.

Procedure:

1. Note every configured integration (notification channels, issue trackers, posture providers).
2. Stop the API and BackgroundJobs.
3. Move the old file aside — move, do not delete, until step 6 has succeeded.
4. Start the API. A new key is generated on first use.
5. Re-enter every integration credential noted in step 1. `SecretProtector.Unprotect` throws
   `SecretProtectionException`, which the controllers surface as **409 Conflict** with a message
   telling the operator to re-enter the value — so the failure is legible rather than a silent 401
   from the provider.
6. Confirm each integration with its "test connection" action, then delete the old file.

**Rotate when:** the file may have been read (host compromise, a backup landing somewhere it should
not, an operator leaving with a copy). Not on a schedule — the cost is re-entering every credential,
and a rotation people avoid is worse than one they do deliberately.

### 3.3 TLS certificate and its password

1. Obtain the new certificate and place the `.pfx` **outside the repository** — `/etc/netrisk/` or
   equivalent, mode `0600`, owned by the service account.
2. Point `https:certificate:file` at it and set `https__certificate__password` in the environment.
3. Restart. Verify with `openssl s_client -connect <host>:5443 -servername <host>`.

**A Release build refuses to start** if the file name is one of the certificates committed to this
repository, or the password is one of the known placeholders (`pass`, `password`, `changeit`,
`netrisk`) — finding NR-2026-003. That is a refusal rather than a warning on purpose: the insecure
configuration was the one an installation got by changing nothing, and a start-up warning lives in a
log nobody tails.

**Private-CA deployments:** install the CA root in the operating-system trust store on every client
machine. Do **not** set `Server:AllowInvalidCertificate` — that disables validation entirely, which
is the finding NR-2026-004 removed, and it logs a warning naming itself on every start-up for exactly
that reason.

### 3.4 Integration credential

1. Revoke or regenerate the credential at the provider (Slack, Jira, Trend Micro…).
2. Enter the new value on the connection in NetRisk. `Protect` re-encrypts; the old ciphertext is
   overwritten.
3. Use "test connection" to confirm.

Values still in the superseded `enc:v1:` format are upgraded to `enc:v2:` (AES-256-GCM) automatically
on save — see NR-2026-011. A value that does not decrypt with this installation's key is left
byte-identical rather than overwritten, so a credential encrypted on another host stays recoverable
there.

### 3.5 API or SCIM token

Tokens are stored as SHA-256 of a 256-bit secret, so a lost token cannot be recovered — only revoked
and reissued.

1. `POST /ApiTokens/{id}/revoke` (or `/ScimTokens/{id}/revoke`). Effective on the next request.
2. Issue a new token with the *same scopes and no more*, and update the consumer.
3. Check `GET /ScimTokens/log` or the application log to confirm the old token is no longer used.

Tokens carry an expiry; prefer a short one and reissue over a long-lived token nobody remembers.
Note that a token never receives the `Admin` role even when the user it acts as is an administrator —
a CI runner holding a credential that bypasses every permission check is the outcome scoped tokens
exist to prevent.

### 3.6 Website sync key

Has a designed rotation flow, so use it rather than regenerating by hand:

```bash
netrisk-console keys rotate --website https://netrisk.example
```

The new public key is presented **signed with the current private key**, proving control of the
trusted key, and only committed locally once the website accepts it. If the private key is already
lost, fall back to trust-on-first-use recovery — documented in the website-sync guide — which
requires an operator action on the website side.

---

## 4. Deployment checklist

Before an installation is considered production-ready:

- [ ] `Database__ConnectionString` supplied through the environment, **not** `appsettings.json`
      (finding NR-2026-025 — the Puppet module still writes it to disk)
- [ ] `https:certificate:file` points outside the repository; the Release build starts, proving it is
      not one of the committed certificates
- [ ] `https__certificate__password` supplied through the environment
- [ ] `Saml2:Enabled` is `false` unless SAML is actually in use, and if it is,
      `OmitAssertionSignatureCheck` is `false` (finding NR-2026-010)
- [ ] `Server:AllowInvalidCertificate` is **unset** on every client
- [ ] `Security:Headers:HstsMaxAgeSeconds` is non-zero once a real certificate is installed
- [ ] `AllowedHosts` set to the actual host names rather than `*` (finding NR-2026-029)
- [ ] `JWT:Timeout` left at 60 minutes, or shortened — not lengthened
- [ ] `<AppData>/NRServer/` is mode `0700`, owned by the service account
- [ ] The upload staging directory exists and is owned by the service account, so the service does
      not fall back to a world-writable temporary directory (finding NR-2026-020)
- [ ] Database TLS enabled, if the database is not on the same host
- [ ] `Integrations:BlockPrivateNetworks` considered — set it if every integration is SaaS
      (finding NR-2026-013)

---

## 5. What to do if a secret is exposed

1. **Rotate first, investigate second.** The procedures above are all safe to run immediately, with
   the single exception of §3.2, which needs its credential list prepared first.
2. **Report it** through the channel in [SECURITY.md](../../SECURITY.md). If it was committed to a
   repository, say so explicitly — a value in git history is published even after the commit that
   removed it.
3. **Do not rewrite history** to hide it. It does not un-publish the value, it breaks every clone,
   and it destroys the evidence of what happened when. Rotate and record.
4. **Record it** in [FINDINGS.md](FINDINGS.md) with the date, the value's blast radius and the
   rotation performed, so the next audit can see the exposure and its closure together.
