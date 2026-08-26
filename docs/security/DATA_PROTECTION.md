# Data Protection at Rest and in Transit

> Track 7 milestone 7.4 · First issued 2026-08-26
> Related: [THREAT_MODEL.md](THREAT_MODEL.md) §1 (assets) · [SECRETS.md](SECRETS.md) (rotation) · [FINDINGS.md](FINDINGS.md)

---

## 1. Data classification

| Class | What | At rest | In transit |
|---|---|---|---|
| **C1 — Secret** | JWT signing key, TLS private key, integration credentials, API/SCIM token secrets | Key file with process-only permissions; credentials AES-256-GCM in the database | Never transmitted, except a token once at issue |
| **C2 — Sensitive** | Vulnerability findings, hosts and services, risk register, management reviews, uploaded scan files, attachments, biometric templates | Database and BLOB storage; **not** column-encrypted — see §3 | TLS 1.2+ |
| **C3 — Personal** | User names, e-mail addresses, login timestamps, source addresses | Database; passwords are bcrypt hashes | TLS 1.2+ |
| **C4 — Operational** | Audit log, job history, schema-upgrade log | Database | TLS 1.2+ |
| **C5 — Public** | Release metadata, download counters, installer artifacts | WebSite SQLite | TLS 1.2+; artifacts signed and checksummed |

**C2 is the class that makes this product a target.** A list of a customer's unfixed
vulnerabilities, with evidence, is a ranked attack plan for their whole estate.

---

## 2. In transit

### Client ↔ API

* TLS only. Kestrel binds HTTPS; `UseHttpsRedirection` is on; HSTS is sent over TLS.
* **Floor: TLS 1.2.** `Security:Tls:MinimumVersion=Tls13` pins 1.3 only. SSL 3.0 and TLS 1.0/1.1
  are not reachable through configuration at all.
  * The listener was previously TLS 1.3 *only*, which is stricter than the 1.2 minimum the milestone
    asks for. 1.2 was allowed alongside it deliberately: a 1.3-only listener silently refuses clients
    on older platform TLS stacks (Windows Server 2019, some corporate middleboxes), and the observed
    operator workaround for "the client cannot connect" is to disable HTTPS entirely. An installation
    that controls its clients should set `Tls13`.
* Cipher suites on non-Windows follow the Mozilla server-side recommendations, TLS 1.3 suites first.
* **Certificate validation is on.** It was unconditionally bypassed on the client — findings
  NR-2026-004 and NR-2026-005. The bypass survives only as an explicit, per-installation,
  loudly-logged opt-in (`Server:AllowInvalidCertificate`) that defaults to off.

### Private certificate authorities — the supported path

Install the CA root in the **operating-system trust store** on each client machine. Validation then
succeeds properly, which is a different thing from being skipped: the client still checks the host
name, the validity dates and the revocation status.

```bash
# Linux (Debian/Ubuntu)
sudo cp internal-ca.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates

# macOS
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain internal-ca.crt

# Windows (elevated)
Import-Certificate -FilePath internal-ca.crt -CertStoreLocation Cert:\LocalMachine\Root
```

`Server:AllowInvalidCertificate` is **not** the supported path. It disables validation entirely — not
just the chain, also the host name — and it warns, naming itself, on every start-up.

### API ↔ MariaDB

TLS on the database connection is a deployment choice, not enforced by the application. Recommended
whenever the database is not on the same host:

```
server=db.internal;...;SslMode=VerifyFull;SslCa=/etc/netrisk/db-ca.pem
```

`VerifyFull` rather than `Required`: `Required` encrypts but accepts any certificate, which stops a
passive listener and not an active one.

### API ↔ third parties

`OutboundHttpClient` allows only `http`/`https` (the operator configures `https`), does not follow
redirects, and evaluates `OutboundUrlPolicy` before every send — see NR-2026-013. It logs the
destination **host** and never the URL, because a webhook URL is itself a credential.

### API ↔ WebSite

One-way, Ed25519-signed `/sync` push over TLS. The `--insecure` flag disables certificate validation
for an installation with an unresolvable certificate; it now warns once per process that the payloads
are signed but readable (NR-2026-026).

---

## 3. At rest

### What is encrypted

| Data | Mechanism | Key |
|---|---|---|
| Integration credentials, webhook secrets | **AES-256-GCM**, `enc:v2:` prefix, per-message 16-byte salt and 12-byte nonce, 128-bit tag | HKDF-SHA256 from the installation key with a domain-separation label |
| Passwords | bcrypt, work factor **15** | n/a (one-way) |
| API and SCIM token secrets | SHA-256, compared with `FixedTimeEquals` | n/a (one-way) |
| MFA recovery codes | SHA-256, single-use | n/a (one-way) |
| Password-reset link keys | SHA-256 index over a CSPRNG key | n/a (one-way) |

The credential format was AES-CBC with `key = SHA256(passphrase)` and `IV = MD5(passphrase)` — a
constant IV, no salt and no authentication (NR-2026-011). GCM fixes all three, and a v1 value is
upgraded in place on save, but only after a round-trip check: v1 is deterministic, so re-encrypting
the decrypted value and comparing is the only reliable way to distinguish "decrypted correctly" from
"decrypted to plausible garbage", which unauthenticated CBC cannot tell you.

HKDF rather than PBKDF2 for the key derivation: PBKDF2's iteration count exists to slow down guessing
a low-entropy human secret, and the input here is a 256-bit installation key. Iterations would buy
nothing while adding hundreds of milliseconds to every notification dispatch.

### What is not encrypted, and why

**The finding register itself (C2) is stored in plaintext columns.** This is a deliberate,
documented acceptance — **TM-A4** in the threat model — not an oversight:

* application-level encryption of `vulnerabilities`, `hosts` and `risks` would remove filtering,
  sorting, aggregation and reporting. That is the product;
* deterministic or order-preserving encryption would keep those working while leaking most of what
  the encryption was meant to protect;
* MariaDB TDE cannot be assumed in a self-hosted FOSS deployment, which is the target topology.

**The compensating control is volume-level encryption**, which the operator owns:

* LUKS (Linux), BitLocker (Windows), FileVault (macOS) on the database volume;
* encrypted backups — `netrisk-console` backups are AES-encrypted with the configured backup
  password; set one;
* a least-privilege database grant, so an application-account compromise is not a server compromise:

```sql
CREATE USER 'netrisk'@'10.0.0.%' IDENTIFIED BY '…';
GRANT SELECT, INSERT, UPDATE, DELETE ON netrisk.* TO 'netrisk'@'10.0.0.%';
-- DDL only for the account that runs `database upgrade-schema`, and only while it runs:
-- GRANT ALTER, CREATE, DROP, INDEX, REFERENCES ON netrisk.* TO 'netrisk_migrator'@'localhost';
```

**Biometric templates are column-encrypted** since Track 8 — finding **NR-2026-032**, closed. This
one was worth more than its exploitability suggested: a leaked password is rotated in a minute, a
leaked face is not rotated at all. `FaceIDService` routes `FaceIdentification` and `SignatureSeed`
through `ISecretProtector` (AES-GCM) on write and reveals them on read.

`ISecretProtector.LooksProtected` is what makes this an in-place upgrade: a row written before the
change is plaintext, is read as-is, and is protected the next time it is written. So **an existing
installation's enrolled templates stay in the clear until each user's next enrolment or update** —
volume-level encryption remains the compensating control for those rows, and an operator who wants
them protected now has to re-enrol. Stated rather than glossed, because "encrypted at rest" would
otherwise read as a claim about data that is already in the database.

### Uploaded files

* Attachment content is a database BLOB, so it inherits whatever protects the database.
* Who may read one is decided by `IFileAccessAuthorizer` from the file's parent record, and
  `nr_files.entity_id` is under the tenancy query filter (finding **NR-2026-017**, closed in Track 8).
  A file whose parent carries no entity keeps a NULL `entity_id` and stays visible to any caller who
  can name it — the alternative was assigning it to a tenant by guesswork.
* Chunked uploads are staged on disk, previously in `/tmp` (finding NR-2026-020). They now go to
  `/var/netrisk/netrisk-api/uploads` (Linux) or the application-data folder (macOS), mode `0700`,
  and are deleted in a `finally` block once reassembled. If the preferred directory cannot be
  created the service falls back to the temporary directory **and warns that it is world-writable**,
  rather than refusing uploads.
* The path a chunk is written to is validated by `SafePathTool` — a character allowlist *and* a
  resolved-path containment check (finding NR-2026-006).

---

## 4. HTTP response headers

Applied to both hosts by `Tools.Security.SecurityHeaderPolicy` (finding NR-2026-015). The policy is
data rather than middleware so the two hosts compute an identical set; each keeps a ten-line
middleware, because sharing the middleware itself would put a `Microsoft.AspNetCore.App` framework
reference into a library the Avalonia desktop client also consumes.

| Header | API | WebSite |
|---|---|---|
| `Strict-Transport-Security` | `max-age=15552000` (configurable; `0` disables) | same |
| `X-Content-Type-Options` | `nosniff` | `nosniff` |
| `X-Frame-Options` | `DENY` | `DENY` |
| `Referrer-Policy` | `no-referrer` | `no-referrer` |
| `X-Permitted-Cross-Domain-Policies` | `none` | `none` |
| `Cross-Origin-Resource-Policy` | `same-origin` | `same-origin` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; …; frame-ancestors 'none'` |
| `Server` | removed | removed |

`0` genuinely disables HSTS, and that is the right setting while an installation is still on a
self-signed certificate: pinning a browser to HTTPS-only for a host whose certificate does not
validate is not recoverable from the server side.

The WebSite's `style-src` allows `'unsafe-inline'` because the Razor views and the bundled CSS
framework carry inline style attributes, and a policy that breaks the site is a policy that gets
removed. `script-src 'self'` with no inline allowance is the half that actually stops an injected
payload from executing, and it is kept strict.

### CORS

There is no CORS policy on either host, which is the strongest possible position: with none
configured, a browser will not expose a cross-origin response, so a hostile page cannot read the API
even with a user's cookies attached. Recorded as NR-2026-024 so that a future "let's add CORS for a
web client" change is recognised as a security decision. If one is ever needed: an explicit origin
list from configuration, never `AllowAnyOrigin`, and never a wildcard combined with credentials.

### Cookies

The only cookies are the SAML flow's. Both are `Secure`, `HttpOnly` and `IsEssential`.
`SameSite=None` is forced on the session cookie — it has to survive the identity provider's
cross-site POST back — which is precisely why the sign-in approval form carries a single-use
anti-forgery token (NR-2026-001, NR-2026-016).

---

## 5. Verification

| Claim | How to check it yourself |
|---|---|
| TLS version and ciphers | `nmap --script ssl-enum-ciphers -p 5443 <host>` |
| Certificate chain | `openssl s_client -connect <host>:5443 -servername <host>` |
| Response headers | `curl -sI https://<host>:5443/System/Ping` |
| Header grade | Mozilla Observatory, or `testssl.sh --headers <host>:5443` |
| Credential encryption | `SELECT encrypted_api_key FROM issue_tracker_connections;` — every value starts `enc:v2:` |
| Staging directory permissions | `ls -ld /var/netrisk/netrisk-api/uploads` — expect `drwx------` |

A header scan against a development instance was captured with the Track 7 baseline; see
[baseline-2026-08-26.md](baseline-2026-08-26.md) § "Security headers".
