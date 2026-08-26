# OWASP ASVS Level 2 Checklist — NetRisk

> Track 7 milestone 7.1.2 · Audited 2026-08-26 against baseline `756c0322` · ASVS 4.0.3, Level 2
> Level 2 is the bar for an application handling sensitive data. NetRisk stores a customer's
> unfixed vulnerabilities, so Level 2 is the floor rather than the ceiling.

## Reading this document

Three columns matter.

**Status** is one of:

| | Meaning |
|---|---|
| ✅ | Requirement met, **and the evidence names the code or the test** |
| ⚠️ | Partially met, or met with a documented gap — cross-referenced to [FINDINGS.md](FINDINGS.md) |
| ❌ | Not met, with a finding raised |
| ➖ | Not applicable, with the reason |

**Evidence** is a file, a symbol or a test name. It is never "by design", "handled" or "see
above". This is deliberate: the two most serious defects in NetRisk's history were controls that
were *documented* as working — multi-entity scoping, and the Master Dashboard backend — so a claim
without a name is treated as unverified.

**Where a ✅ became a ✅ during this audit** (that is, the control was added by Track 7 rather than
found already present) the finding id is given, so a reader can tell "was already right" from "was
made right".

Chapters V6 (stored crypto), V7 (logging), V9 (communications), V12 (files) and V13 (API) carry most
of the weight for this product. V15 and V11 are thin because NetRisk has little business-logic
sequencing to abuse.

---

## V1 — Architecture, Design and Threat Modelling

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 1.1.2 | Threat modelling for every design change | ✅ | [THREAT_MODEL.md](THREAT_MODEL.md), reviewed per minor release (7.5.3) |
| 1.1.3 | User stories carry security constraints | ⚠️ | Track specs carry them (this document's own spec is `docs/roadmap/TRACK_7_SECURITY.md`); ordinary issues do not. No finding — process, not code. |
| 1.1.4 | Trust boundaries documented | ✅ | THREAT_MODEL.md §2 — six boundaries, TB1–TB6 |
| 1.1.5 | Application-tier centralisation of security controls | ✅ | Authorization in `API/Security`, crypto in `Tools/Security` + `Tools/Criptography`, tenancy in `DAL/Context/NRDbContext.EntityScope.cs` |
| 1.1.6 | Reusable, vetted security components | ✅ | `SecurityHeaderPolicy`, `ServerCertificatePolicy`, `SafePathTool`, `ExternalUrlPolicy`, `OutboundUrlPolicy`, `AesGcm256` — each with its own test class |
| 1.2.1 | Unique low-privilege runtime account | ⚠️ | Puppet module runs a dedicated account; documented in [DATA_PROTECTION.md](DATA_PROTECTION.md). Container images run as root — noted for Track 8. |
| 1.2.2 | Authenticated communication between components | ✅ | `/sync` is Ed25519-signed (Track 6); API↔DB uses credentialed TLS-capable connections |
| 1.2.3 | Single vetted authentication path | ⚠️ | Five schemes (Basic, JWT, API token, SCIM token, SAML), each a separate `AuthenticationHandler` selected by `headerSelector` on the credential shape. Deliberate — one handler per credential shape is what stops a scope check being skipped for one of them — but five is five. Audited individually; NR-2026-007 was found precisely because the two were compared. |
| 1.2.4 | All paths equally strong | ⚠️ | Was **not** true: Basic auth ignored `enabled` while JWT honoured it — **NR-2026-007**, fixed. |
| 1.4.1 | Trusted enforcement points | ✅ | `AuthorizationMiddleware` (policies) + `AuditableContext.SaveChanges` (tenancy). A service that forgets to scope still cannot cross a tenant. |
| 1.4.4 | Single access-control mechanism | ✅ | `PermissionPolicyProvider` for every policy name; `PermissionAuthorizeAttribute` for every permission |
| 1.4.5 | Attribute- or feature-based access control | ✅ | Permission claims, not role strings, for everything except the four legacy `RequireAdminOnly`-style policies |
| 1.5.2 | No serialisation against untrusted clients | ✅ | `System.Text.Json` with no polymorphic type resolution; scan files deserialise into fixed generated types |
| 1.6.1 | Documented key-management policy | ✅ | [SECRETS.md](SECRETS.md) — every secret, its home and its rotation procedure |
| 1.8.1 | Data classified by protection level | ✅ | THREAT_MODEL.md §1 (A1–A11), [DATA_PROTECTION.md](DATA_PROTECTION.md) §1 |
| 1.9.1 | Encrypted component communication | ⚠️ | Client↔API and API↔WebSite are TLS. API↔MariaDB TLS is a deployment choice; documented as recommended, not enforced. |
| 1.11.1 | Documented business-logic components | ✅ | `docs/features/`, `docs/roadmap/` |
| 1.14.6 | No unsupported client-side technologies | ✅ | Avalonia desktop; no Flash, Silverlight or applets |

## V2 — Authentication

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 2.1.1 | Minimum 12-character passwords | ⚠️ | `PasswordTools.CheckPasswordComplexity` requires 8 with mixed case and a symbol. Below ASVS L2's 12, and complexity rules are not what ASVS asks for. Raised as a hardening item, not a finding: enforcing 12 breaks every existing password on next change and needs a product decision. |
| 2.1.2 | At least 64 characters permitted | ✅ | Upper bound is 64 exactly (`PasswordTools`) — meets, with no margin |
| 2.1.3 | No password truncation | ⚠️ | Was **not** true over the wire: Basic auth split on every colon, truncating any password containing one — **NR-2026-018**, fixed |
| 2.1.7 | Breached-password check | ❌ | Not implemented. No finding raised: it needs an outbound call (Pwned Passwords k-anonymity), which a self-hosted air-gapped install cannot make. Candidate for an optional feature. |
| 2.1.9 | No composition rules | ⚠️ | NetRisk *does* impose them, which ASVS discourages. Kept: removing them without a length increase would weaken the effective floor. |
| 2.2.1 | Anti-automation on credential paths | ✅ | `LoginAttemptTracker` + `AuthRateLimiting` — **NR-2026-008**, fixed. The per-process residual **NR-2026-008b** was closed in Track 8 by `PersistedLoginAttemptTracker` over a `login_attempts` table keyed on `(identity, source)`. |
| 2.2.2 | No weak second factor | ✅ | WebAuthn hardware factor (Track 4.3.3), FaceID as an optional plugin. No SMS. |
| 2.2.3 | Notification of security-sensitive changes | ⚠️ | Password change is logged at Warning and MFA recovery-code generation at Warning with actor and target; no user-facing e-mail. Notification channels exist (Track 4) and could carry it. |
| 2.3.1 | Initial passwords are random and short-lived | ⚠️ | `UsersController` generates a 12-character password from the CSPRNG (**NR-2026-002**) and sets `ChangePassword`; there is no expiry on the temporary value itself. |
| 2.4.1 | Approved one-way KDF for passwords | ✅ | bcrypt, work factor **15** (`UsersService.ChangePassword`, `BCrypt.Net`). ASVS asks ≥10; 15 is well above. `UsersServiceInMemoryTest` |
| 2.5.1 | No default credentials | ✅ | First-run creates an administrator with an operator-supplied password (`ConsoleClient` `UserCommand`) |
| 2.5.4 | No shared or default accounts | ✅ | Every principal is a `user` row; API tokens act *as* a user and deliberately never receive the `Admin` role (`ApiTokenAuthenticationHandler.BuildClaimsAsync`) |
| 2.5.6 | Secure password-recovery | ⚠️ | Time-limited (30 min), single-type link, key now from the CSPRNG (**NR-2026-002**) and indexed by SHA-256 (**NR-2026-014**). Gap: the link is not single-use — it works until it expires. |
| 2.6.1 | Look-up secrets used once | ✅ | MFA recovery codes are single-use and SHA-256-hashed (`WebAuthnService`) |
| 2.7.2 | Out-of-band verifier expires | ✅ | SAML request 10 min, reset link 30 min, WebAuthn ceremony single-use |
| 2.7.3 | Out-of-band request is single-use | ✅ | SAML token redemption removes the cache entry before writing the token — **NR-2026-001**, fixed |
| 2.8.1 | Time-based tokens are single-use | ➖ | No TOTP factor |
| 2.10.1 | Service accounts do not use default passwords | ✅ | API and SCIM tokens are 256-bit CSPRNG secrets stored as SHA-256 (`ApiTokensService`, `ScimService`) |
| 2.10.2 | Service credentials not embedded in source | ✅ | Verified by full-tree grep and by the gitleaks gate. The **certificate** exception is **NR-2026-003** |
| 2.10.4 | Secrets protected from unauthorised access | ✅ | Integration credentials AES-256-GCM at rest (**NR-2026-011**); signing key in the app-data directory |

## V3 — Session Management

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 3.1.1 | No session tokens in URL parameters | ⚠️ | The SAML *request id* travels in a URL. It is not a session token — it is a handle that a specific approved client redeems once, after explicit consent — but the distinction only holds because of the **NR-2026-001** fix; before it, that URL parameter was effectively a session token. |
| 3.2.1 | New token on authentication | ✅ | `GenerateToken` mints a fresh JWT with a new `jti` per authentication |
| 3.2.3 | Tokens stored securely on the client | ⚠️ | The desktop client persists the JWT through `IMutableConfigurationService` in the user profile — file-permission protected, not OS-keychain protected. Candidate for Track 8. |
| 3.3.1 | Logout terminates the session | ✅ | Mass revocation on password change and immediate effect on disable (**NR-2026-012**), plus per-session `jti` revocation via `POST /Sessions/Logout` and the `revoked_tokens` table — **NR-2026-028**, fixed in Track 8 |
| 3.3.2 | Re-authentication after inactivity | ✅ | 60-minute token lifetime with no refresh flow, so inactivity beyond it forces re-authentication (**NR-2026-012**; was 1440 minutes) |
| 3.3.4 | Users can view and terminate active sessions | ⚠️ | A user can terminate *the session they are on* and verify it took effect (`POST /Sessions/Logout`, `GET /Sessions/Current`, **NR-2026-028**). Enumerating and terminating *other* sessions is still not implemented: it needs a record of issued tokens, and `revoked_tokens` deliberately records only revocations. |
| 3.4.1–3.4.3 | Cookie `Secure` / `HttpOnly` / `SameSite` | ✅ | SAML cookies: `Secure=Always`, `HttpOnly`, `SameSite=None` (required for the identity-provider POST back) with a compensating anti-forgery token — **NR-2026-016**, fixed |
| 3.5.2 | Static API secrets are not used | ⚠️ | API tokens *are* static bearer secrets. Compensated: scoped, expiring, revocable, SHA-256 at rest, and never granted the `Admin` role. The alternative (mTLS or OIDC client credentials for CI) is disproportionate for the use case. |
| 3.5.3 | Stateless tokens use a proven signature | ✅ | HMAC-SHA256 with issuer, audience and algorithm all validated — **NR-2026-012**, fixed |

## V4 — Access Control

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 4.1.1 | Server-side enforcement | ✅ | `AuthorizationMiddleware` + `AuditableContext.SaveChanges`; the client enforces nothing |
| 4.1.2 | Attributes cannot be manipulated by the user | ✅ | Permission claims are built server-side per request from the database (`BasicAuthenticationHandler`, `JwtAuthenticationHandler`), never read from the token |
| 4.1.3 | Least privilege / no elevation | ✅ | Per-permission policies. Verified by enumeration, not by comment — `ControllerAuthorizationInventoryTest` — which is how **NR-2026-009** was found |
| 4.1.5 | Fail securely | ✅ | `DefaultPolicyProvider.GetFallbackPolicyAsync` requires an authenticated existing user, so an unannotated endpoint is *denied*. Asserted by `TheFallbackPolicyRequiresAnAuthenticatedValidUser`. |
| 4.2.1 | No IDOR | ✅ | Tenant-scoped entities are covered by the global query filters (an out-of-scope row is simply not found), and attachments joined them in Track 8: `nr_files.entity_id` is filtered, and `IFileAccessAuthorizer` applies the parent record's permission rules on both read routes — **NR-2026-017**, fixed. |
| 4.2.2 | Anti-CSRF on state-changing operations | ✅ | The API is bearer-token authenticated, so it is not CSRF-reachable. The one cookie-authenticated state change — SAML approval — carries a single-use anti-forgery token, which is required because its cookie must be `SameSite=None` (**NR-2026-001**). |
| 4.3.1 | Administrative interfaces use MFA | ⚠️ | WebAuthn hardware-factor policy exists (Track 4.3.3) and can be required per user; it is not mandatory for administrators. Product decision. |
| 4.3.2 | No directory browsing | ✅ | `UseStaticFiles` with an explicit content-type provider and no directory browser |
| 4.3.3 | Additional authorisation for high-value transactions | ✅ | Risk acceptance and closure require distinct permissions (`close_risks`, `delete_risk`); FaceID transactions gate specific actions |

## V5 — Validation, Sanitisation and Encoding

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 5.1.1 | Mass-assignment defence | ⚠️ | DTOs are used for most writes; a few controllers bind entities directly (`FilesController.CreateFile` takes `NrFile`). `FilesService.Create` overwrites `Id`, `User`, `Timestamp` and `UniqueName` server-side, so the dangerous fields are not assignable. |
| 5.1.3 | All input validated | ⚠️ | Data annotations plus explicit guards on the security-relevant paths; not uniform across every DTO |
| 5.1.4 | Structured data is strongly typed | ✅ | Model binding to typed DTOs; route constraints (`{id:int}`) |
| 5.2.5 | No template injection | ✅ | Report templates render through QuestPDF and a fixed token substitution (`PackagingTemplate`, `IssueTemplate`) — no expression evaluation |
| 5.2.6 | SSRF defence | ✅ | `OutboundUrlPolicy` — **NR-2026-013**, fixed. `OutboundUrlPolicyTest` |
| 5.3.4 | Parameterised queries | ✅ | EF Core and Sieve parameterise. Verified by grepping every `MySqlCommand`; the three interpolations found were operator-config identifiers and were parameterised anyway (**NR-2026-021**) |
| 5.3.8 | No OS command injection | ✅ | `Process.Start` appears only in the desktop client, now URL-validated with `ArgumentList` — **NR-2026-023**, fixed |
| 5.3.9 | No local file inclusion / path traversal | ✅ | `SafePathTool` — **NR-2026-006**, fixed. `SafePathToolTest`, `FilesServiceUploadPathTest` |
| 5.3.10 | No XPath/XML injection | ✅ | No XPath over untrusted input |
| 5.5.2 | Safe XML parser configuration | ✅ | `DtdProcessing.Prohibit` + `XmlResolver = null` on every importer and on the SAML assertion parser. **Proved by `ImporterXxeTest`**, which asserts the refusal of an external file entity, an external HTTP entity and nested internal entities — rather than trusting the comment beside the setting |
| 5.5.3 | No deserialisation of untrusted data into arbitrary types | ✅ | `XmlSerializer` over fixed generated types; `System.Text.Json` with no `TypeInfoResolver` polymorphism; YAML (`SchemaUpgradePhases.yaml`, `DatabaseInformation.yaml`) is operator-controlled config, not user input |

## V6 — Stored Cryptography

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 6.1.1 | Regulated private data encrypted at rest | ⚠️ | Credentials, tokens and integration secrets are encrypted. The finding register itself is not — see **TM-A4**: application-level encryption of the whole dataset would remove filtering, sorting and reporting, i.e. the product. Documented in [DATA_PROTECTION.md](DATA_PROTECTION.md) with the disk/volume-encryption recommendation that covers it. |
| 6.1.2 | Regulated health data encrypted | ✅ | Biometric templates and signature seeds go through `ISecretProtector` (AES-GCM) on write — **NR-2026-032**, raised below and fixed in Track 8 |
| 6.2.1 | All modules use a vetted crypto library | ✅ | `System.Security.Cryptography` and `BCrypt.Net` only. No hand-rolled primitives; `AesGcm256` composes `AesGcm` and `HKDF`. |
| 6.2.2 | No custom or deprecated crypto | ⚠️ | MD5 and SHA-1 remain in `HashTool` for reading existing values, now documented as compatibility-only. MD5-as-index removed from the reset path (**NR-2026-014**). SHA-1 still names files (`FilesService`), where it hashes a 256-bit CSPRNG token, so collision resistance is not load-bearing. |
| 6.2.3 | Authenticated encryption | ✅ | AES-256-GCM with a 128-bit tag — **NR-2026-011**, fixed. `TamperingWithTheCiphertextIsDetected` |
| 6.2.4 | Random-number, cipher and mode choices are current | ✅ | GCM, HKDF-SHA256, `RandomNumberGenerator` — **NR-2026-002**, **NR-2026-011** |
| 6.2.5 | No insecure block mode | ✅ | GCM. CBC remains only in the `enc:v1:` *read* path, retained so an in-place upgrade can read existing rows |
| 6.2.6 | Nonces and IVs are not reused | ✅ | Fresh 12-byte nonce and 16-byte salt per message. `EncryptingTheSameValueTwiceProducesDifferentCiphertext` — the exact defect that was there before |
| 6.2.7 | Authenticated encryption verifies before decrypting | ✅ | `AesGcm.Decrypt` throws `AuthenticationTagMismatchException`; "malformed", "wrong key" and "tampered" are one outcome, so there is no oracle |
| 6.3.1 | Approved random for secrets | ✅ | **NR-2026-002**, fixed. `RandomGeneratorHoldsNoPseudoRandomState` |
| 6.3.2 | GUIDs use a CSPRNG or are not used as secrets | ✅ | `Guid.NewGuid()` is used for upload ids and ceremony ids; no GUID is a security boundary |
| 6.4.1 | Secret-management solution | ✅ | user-secrets (development) / environment or secret store (production), documented in [SECRETS.md](SECRETS.md) |
| 6.4.2 | Key material is not exposed to the application | ⚠️ | The signing key is a file the API reads. An HSM or a cloud KMS would be better and is out of scope for a self-hosted FOSS product; the file is in the application-data directory with the process's own permissions. |

## V7 — Error Handling and Logging

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 7.1.1 | No credentials or payment details in logs | ✅ | Verified by reading every credential-adjacent log statement. `OutboundHttpClient` logs only the *host*, because a webhook URL is itself a credential; `LoginAttemptTracker` truncates the login |
| 7.1.2 | No sensitive data in logs | ⚠️ | Vulnerability ids and file names appear in informational logs. Appropriate for an audit trail; an installation shipping logs off-host should treat them at the same classification as the register |
| 7.1.3 | Security-relevant events are logged | ✅ | Authentication success and failure, throttling, permission changes, MFA recovery-code generation, password change, cross-entity refusal, SSO approval, API-token use |
| 7.1.4 | Logs include necessary context | ✅ | Serilog structured properties: actor, source address, target |
| 7.2.1 | All authentication decisions logged | ✅ | Every arm of both credential handlers |
| 7.2.2 | All access-control failures logged | ✅ | `EntityScopeViolationMiddleware`, `PermissionAuthorizationHandler`, the `Unauthenticated` helper |
| 7.3.1 | Log injection defence | ✅ | Serilog structured logging — user data is a property value, never concatenated into the template |
| 7.3.3 | Logs protected from unauthorised access | ⚠️ | Filesystem permissions. No append-only or remote-attestation store. |
| 7.4.1 | Generic error messages | ✅ | Domain exceptions map to status codes in `IntegrationsControllerBase`; the API-token and webhook failures are deliberately uninformative ("revoked", "expired", "unknown" and "wrong secret" are one answer) |

## V8 — Data Protection

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 8.1.1 | Sensitive data is not cached client-side | ⚠️ | The desktop client caches domain data in memory and the token on disk; no HTTP caching layer |
| 8.2.1 | Anti-caching headers | ⚠️ | Not set on the API. Low impact — there is no browser client and no intermediary cache in the supported topology. Recorded here rather than as a finding. |
| 8.2.2 | Minimal client-side storage | ✅ | `IMutableConfigurationService` stores the server URL, the token and preferences |
| 8.3.1 | Sensitive data sent in the body, not the URL | ⚠️ | Two exceptions, both known: the SAML `requestId` (see V3 3.1.1) and the unsigned providers' webhook `?secret=` (which the provider forces — Jira and Azure DevOps do not sign) |
| 8.3.4 | Collected sensitive data is documented | ✅ | THREAT_MODEL.md §1, [DATA_PROTECTION.md](DATA_PROTECTION.md) §1 |

## V9 — Communications

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 9.1.1 | TLS for all client connectivity | ✅ | Kestrel is HTTPS-only on both hosts; `UseHttpsRedirection`; HSTS |
| 9.1.2 | Only strong ciphers and versions | ✅ | TLS 1.2+ floor with an explicit Mozilla-recommended cipher-suite policy on non-Windows; `Security:Tls:MinimumVersion=Tls13` pins 1.3 only |
| 9.1.3 | Old TLS versions disabled | ✅ | `SslProtocols.Tls12 \| SslProtocols.Tls13` — SSL 3.0 and TLS 1.0/1.1 are not reachable through configuration at all |
| 9.2.1 | Trusted TLS certificate validation | ✅ | **NR-2026-004**, **NR-2026-005**, fixed. `ServerCertificatePolicyTest` |
| 9.2.2 | Encrypted connections to all external systems | ✅ | `OutboundHttpClient` allows only `http`/`https` and the operator configures `https`; SMTP TLS is a deployment setting |
| 9.2.4 | Proper certificate revocation handling | ⚠️ | Platform default (CRL/OCSP as the OS provides). Not pinned or stapled. |
| 9.2.5 | Backend TLS failures are logged | ✅ | `OutboundHttpClient` logs the transport error with the host; `SyncClient` warns when validation is disabled (**NR-2026-026**) |

## V10 — Malicious Code

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 10.2.1 | No malicious code / unauthorised phone-home | ✅ | Outbound calls are the configured integrations plus the update check; enumerated in THREAT_MODEL.md TB6 |
| 10.2.2 | No unnecessary or unauthorised capability | ⚠️ | The plugin system is by construction an arbitrary-code capability — **NR-2026-027**, still accepted. Track 8 implemented the proposed mitigation: `PluginSignatureVerifier` checks the publisher before load, refusing unsigned or unlisted assemblies when the installation opts in. Provenance, not confinement. |
| 10.3.2 | Integrity protection for updates | ✅ | Track 5 code signing (Authenticode, Developer ID, notarisation), SHA-256 checksums beside every artifact, `VerifySignatures` gate |
| 10.3.3 | Application does not have write access to its own code | ⚠️ | Deployment-dependent; the packaged installers place binaries in system locations |

## V11 — Business Logic

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 11.1.1 | Sequential processing where required | ✅ | Finding status transitions go through `FindingStatusMachine`, which rejects an illegal transition (`FindingStatusMachineTest`) |
| 11.1.4 | Anti-automation on business flows | ✅ | Rate limiting on the credential paths; import runs as a tracked job |
| 11.1.5 | Business-limit enforcement | ✅ | Chunk count bounded (**NR-2026-006**), request body bounded (`Files:MaxRequestBodySizeBytes`), page size bounded (Sieve `MaxPageSize`) |

## V12 — Files and Resources

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 12.1.1 | Upload size limits | ✅ | `Kestrel.Limits.MaxRequestBodySize` (100 MB default), `MaxChunksPerUpload` |
| 12.2.1 | File type validation | ⚠️ | Importers sniff content (`ImporterHelpers.Sniff`) and declare extensions; generic attachments are not type-restricted, which is intentional — a risk's evidence can be any document |
| 12.3.1 | Filename metadata is not used for path construction | ✅ | The stored name is a server-generated token; the user's filename is metadata only. **NR-2026-006** fixed the one place a client-supplied value reached a path |
| 12.3.2 | No path traversal | ✅ | `SafePathTool` with both a character allowlist and a resolved-path containment check |
| 12.3.3 | No local file inclusion | ✅ | No dynamic include or require |
| 12.3.4 | No reflective file download | ✅ | `Content-Disposition` and `X-Content-Type-Options: nosniff` (**NR-2026-015**) |
| 12.3.6 | Files from untrusted sources are scanned | ➖ | Anti-virus is the operator's responsibility on a self-hosted install; documented |
| 12.4.1 | Files stored outside the web root | ✅ | Attachment content is a database BLOB; the staging directory is outside any served path (**NR-2026-020**) |
| 12.4.2 | Untrusted files are scanned / served safely | ✅ | Attachments are never executed and are served with an explicit content type |
| 12.5.1 | Only intended file types are served | ✅ | Explicit `FileExtensionContentTypeProvider` on the WebSite |
| 12.6.1 | No unvalidated redirects to external domains | ✅ | The only redirect is the internal `/Authentication/SAMLSingIn` |

## V13 — API and Web Service

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 13.1.1 | Consistent authentication and session across API and UI | ✅ | One `AuthenticationHandler` set for every surface |
| 13.1.3 | API URLs do not expose sensitive information | ⚠️ | See V8 8.3.1 |
| 13.1.4 | Authorisation decisions at both URI and resource level | ✅ | URI level via policies; resource level via the tenancy filters, attachments included since Track 8 (**NR-2026-017**) |
| 13.2.1 | Only allowed HTTP methods | ✅ | Attribute routing; there is no catch-all |
| 13.2.2 | Schema validation on API input | ⚠️ | Model binding plus data annotations; no formal OpenAPI-schema enforcement |
| 13.2.3 | Anti-CSRF for cookie-authenticated state changes | ✅ | See V4 4.2.2 |
| 13.2.5 | `Content-Type` is validated | ✅ | `[ApiController]` returns 415 for an unexpected media type |
| 13.2.6 | Message payload signature where required | ✅ | `/sync` Ed25519; inbound webhooks HMAC or shared secret in constant time (**NR-2026-019**) |
| 13.3.1 | XML schema validation | ⚠️ | Scan files are deserialised into fixed types with DTDs prohibited; no XSD validation, which would reject the many real-world scanner files that deviate from their own schema |

## V14 — Configuration

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 14.1.1 | Reproducible, automated build | ✅ | Nuke (`build/Build.cs`), pinned SDK via `global.json` |
| 14.1.3 | Server configuration is hardened per guidance | ✅ | Kestrel TLS floor, security headers, host-filtering noted (**NR-2026-029**) |
| 14.1.4 | Application and dependency versions are tracked | ✅ | CycloneDX SBOM per component — **NR-2026-031**, fixed. `SbomTest` |
| 14.2.1 | Dependencies are up to date | ✅ | Dependabot (NuGet ×2, GitHub Actions, git submodules) + a CI gate that fails on a known-vulnerable package. Baseline: **zero** vulnerable packages across all 33 projects |
| 14.2.2 | Unneeded features are removed | ⚠️ | The legacy `NessusImporter` and `ImporterFactory` are unreachable but retained (hardened instead) — **NR-2026-022**. Candidate for deletion in Track 8. |
| 14.2.3 | Third-party assets are integrity-checked | ✅ | NuGet with `packageSourceMapping` (so a `netrisk*` package cannot be substituted from nuget.org), submodules pinned to reviewed SHAs with a CI review gate — [SUPPLY_CHAIN.md](SUPPLY_CHAIN.md) |
| 14.2.4 | Subresource integrity for third-party JS/CSS | ➖ | No CDN assets; the WebSite serves its own |
| 14.2.5 | Build pipeline warns on out-of-date components | ✅ | `security.yml` `dependency-scan`, weekly plus per pull request |
| 14.2.6 | Attack surface reduced by sandboxing | ⚠️ | Not for plugins — **NR-2026-027**, accepted. Signature verification narrows *who* can supply a plugin; it does not sandbox one. |
| 14.3.2 | Debug modes disabled in production | ✅ | Swagger commented out; sensitive-data logging inside `#if DEBUG`; the development-certificate refusal is Release-only (**NR-2026-003**) |
| 14.3.3 | HTTP headers do not expose version detail | ✅ | `Server` removed — **NR-2026-015**, fixed |
| 14.4.1 | Every response has a `Content-Type` charset | ✅ | ASP.NET Core default plus the explicit provider on the WebSite |
| 14.4.2 | `Content-Disposition: attachment` where relevant | ✅ | Installer downloads |
| 14.4.3 | Content Security Policy | ✅ | **NR-2026-015**, fixed — `default-src 'none'` on the API, a page policy on the WebSite |
| 14.4.4 | `X-Content-Type-Options: nosniff` | ✅ | Same |
| 14.4.5 | HSTS | ✅ | Same, with a `0`-disables escape hatch for self-signed installations |
| 14.4.6 | `Referrer-Policy` | ✅ | `no-referrer` |
| 14.4.7 | Framing restricted | ✅ | `X-Frame-Options: DENY` + `frame-ancestors 'none'` |
| 14.5.1 | Only expected HTTP methods accepted | ✅ | Attribute routing |
| 14.5.2 | Origin header is not used for authentication | ✅ | No CORS, no origin-based decisions — **NR-2026-024** |
| 14.5.3 | CORS `Access-Control-Allow-Origin` does not include `null` or `*` | ✅ | No CORS policy at all, which is the strongest form of this |

---

## Requirements raised as new findings by this checklist

| Requirement | Finding |
|---|---|
| 6.1.2 — regulated health data encrypted at rest | **NR-2026-032** *(Medium, **fixed in Track 8**)* — FaceID biometric templates were stored without column-level encryption. Unlike a password, a face cannot be rotated, which is what made this worth more than its exploitability suggested. `FaceIDService` now protects `FaceIdentification` and `SignatureSeed` with `ISecretProtector` on write and reveals them on read; `LooksProtected` makes it an in-place upgrade, so no existing enrolment has to be redone. Proved by `ServerServices.Tests/Track8/FaceIdTemplateProtectionInMemoryTest`, two of whose cases were confirmed to fail on the pre-fix code. |

## Requirements deliberately not raised as findings

Recorded so a later reader does not mistake silence for oversight:

* **2.1.1 / 2.1.9 — password length and composition.** ASVS wants ≥12 characters and no composition
  rules; NetRisk has 8 with composition rules. Changing the floor is a product decision that affects
  every existing user, and dropping the composition rules *without* raising the length would weaken
  the effective floor. Flagged for the product owner rather than filed as a defect.
* **2.1.7 — breached-password check.** Needs an outbound call a self-hosted air-gapped install
  cannot make. Candidate for an optional feature, not a gap in the current design.
* **8.2.1 — anti-caching headers.** No browser client, no intermediary cache in the supported
  topology.
* **13.3.1 — XSD validation of scan files.** Real scanner output routinely deviates from its own
  published schema; strict validation would reject files customers need to import. DTD prohibition
  plus fixed-type deserialisation covers the security requirement.
