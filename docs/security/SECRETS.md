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
| **JWT signing key** | `<AppData>/NRServer/secret_token.txt`, generated on first start | Generated — 32 characters of CSPRNG output, base64-encoded | `EnvironmentService.ServerSecretToken` → `AuthenticationBootstrapper` | §3.2 |
| **Credential master key** | The most protected store the host offers — TPM 2.0 (Linux), keychain (macOS), DPAPI (Windows), else `<AppData>/NRServer/secrets/master.key` at mode 0600. Overridable with `NETRISK_SECRET_MASTER_KEY`. | Generated — 32 CSPRNG bytes, base64-encoded | `MasterKeyProvider` → `SecretProtector` | §3.7 — **destructive, read first** |
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
| **Biometric templates** | `faceid_users`, AES-GCM via `ISecretProtector` since Track 8 — NR-2026-032 | The FaceID plugin | `FaceIDService` | Not rotatable — which is why they are encrypted rather than merely access-controlled. Rows written before Track 8 are plaintext until their next write; `LooksProtected` reads both. |

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

### 3.2 JWT signing key

The key at `<AppData>/NRServer/secret_token.txt` signs session tokens. Deleting it invalidates every
outstanding session token; users sign in again, and nothing else is lost.

Until 2.22.5 it did more than that — the credential-encryption key was derived from it, so rotating
it also made every stored integration credential undecryptable. That is no longer true: credentials
are encrypted under the separate master key of §3.7. The old derivation survives as a **decrypt-only
fallback** in `SecretProtector`, so an installation that has not yet re-saved its connections still
reads them; once every connection has been saved once, this file no longer matters to them.

Procedure:

1. Stop the API and BackgroundJobs.
2. Move the old file aside.
3. Start the API. A new key is generated on first use.
4. If the installation has **not** re-saved its integration connections since the upgrade to 2.22.5,
   re-enter each credential — the fallback key is gone with the file. `SecretProtector.Unprotect`
   throws `SecretProtectionException`, which the controllers surface as **409 Conflict** with a
   message telling the operator to re-enter the value, so the failure is legible rather than a
   silent 401 from the provider.

**Rotate when:** the file may have been read. Sessions are the only guaranteed cost.

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

### 3.7 Credential master key — **destructive, read first**

This is the key every stored integration credential, OIDC client secret, webhook shared secret and
biometric template is encrypted under. Losing it loses all of them.

**Where it is.** `MasterKeyProvider` resolves it once per process, from the most protected place the
host offers, and logs which one it landed on at first use:

| Order | Backend | Where the key actually is |
|---|---|---|
| 1 | Environment | `NETRISK_SECRET_MASTER_KEY` — base64 of 32 bytes. Nothing is written to disk. |
| 2 | TPM 2.0 (Linux) | Sealed to the chip. `<AppData>/NRServer/secrets/master.key.tpm2.pub` + `.priv` are the sealed blobs; they are useless on any other machine. Requires `/dev/tpmrm0` and `tpm2-tools`. |
| 2 | Keychain (macOS) | Generic-password item, service `netrisk-server`, account `secret-master-key`. Protected by the Secure Enclave on Apple silicon. Inspect with `security find-generic-password -s netrisk-server -a secret-master-key`. |
| 2 | DPAPI (Windows) | `<AppData>/NRServer/secrets/master.key.dpapi`, wrapped at machine scope — TPM-bound where the OS binds DPAPI keys to one. |
| 3 | Protected file | `<AppData>/NRServer/secrets/master.key`, mode 0600 in a 0700 directory. The fallback when none of the above is available. |

`<AppData>` is `/var/netrisk` on Linux, `%APPDATA%` on Windows, `~/Library/Application Support` on
macOS.

An existing key is **never** replaced: every store is read before any write is considered, so a host
that gains or loses TPM tooling keeps the key it already had
(`MasterKeyProviderTest.ReadsAnExistingKeyFromALowerStoreRatherThanMintingANewOne`). A newly written
key is read back and compared before it is used, so a backend that reports success and cannot
actually return the key is demoted rather than trusted
(`MasterKeyProviderTest.SkipsAStoreThatDoesNotReturnWhatItWasGiven`).

**Back it up.** The hardware-backed stores are, by design, not portable: a TPM-sealed blob restored
onto another machine will not unseal, and a keychain item does not travel in a `tar` of the home
directory. That is the point, and it is also the failure mode — a host rebuild with no plan is every
credential re-entered. Either accept that cost deliberately, or run the installation on
`NETRISK_SECRET_MASTER_KEY` supplied from wherever the deployment already keeps its database
password, which makes the key an input to the deployment instead of state on the host.

**Rotation** is the same shape as §3.2 and has the same cost:

1. Note every configured integration.
2. Stop the API and BackgroundJobs.
3. Remove the key: `security delete-generic-password -s netrisk-server -a secret-master-key` on
   macOS, or move `<AppData>/NRServer/secrets/` aside — move, do not delete, until step 6.
4. Start the API. A new key is generated on first use.
5. Re-enter every credential noted in step 1.
6. Confirm each integration with its "test connection" action, then destroy the old key.

---

## 4. Deployment checklist

Before an installation is considered production-ready:

- [ ] `Database__ConnectionString` supplied through the environment, **not** `appsettings.json`
      (finding NR-2026-025 — closed in Track 8: the Puppet module writes it to /netrisk/netrisk.env,
      mode 0600, owned by the service account, and no longer into appsettings.json. An installation
      upgraded in place keeps whatever is already in its appsettings.json until Puppet reapplies —
      delete the Database section by hand if it predates that run.)
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
