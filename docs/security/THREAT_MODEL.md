# NetRisk Threat Model

> Track 7 milestone 7.1.1 · First issued 2026-08-26 · Reviewed each minor release (see [7.5.3](#review-cadence))
> Method: OWASP [Threat Modeling Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html) scope → diagram → STRIDE per boundary → control mapping.

NetRisk is a self-hosted risk, vulnerability and incident management product. Its database holds
the customer's *unfixed vulnerabilities*, which makes the application itself a high-value target:
a read of the finding register is a prioritised attack plan for every asset the customer owns.
That single observation drives most of what follows.

---

## 1. Assets

Ranked by what an attacker gains, not by how the code is organised.

| # | Asset | Where it lives | Why it matters |
|---|---|---|---|
| A1 | **Open vulnerability findings** (host, port, plugin, CVSS, evidence, remediation status) | `vulnerabilities`, `hosts`, `hosts_services`, `finding_status_history` | A ranked list of what is exploitable on the customer's estate, with the evidence needed to exploit it. Disclosure is the worst outcome in this product. |
| A2 | **The JWT signing key** | `<AppData>/NRServer/secret_token.txt`, 0600-intent | Forging a token is authentication as anyone, including an administrator. Also the root of the integration-credential key (A5). |
| A3 | **Password hashes** | `user.password` (bcrypt, work factor 15) | Offline cracking; reuse against other systems. |
| A4 | **Biometric templates** (FaceID plugin) | `faceid_users`, embeddings | Irrevocable personal data. Unlike a password, a face cannot be rotated. |
| A5 | **Integration credentials** — Slack/Teams webhooks, Jira and Azure DevOps tokens, Trend Micro and SecurityScorecard API keys, SMTP credentials | `encrypted_*` columns, AES-256-GCM under a key derived from A2 | Lateral movement out of NetRisk into the customer's other systems. A Slack webhook URL is itself a credential. |
| A6 | **API and SCIM tokens** | `api_tokens.secret_hash`, `scim_tokens.secret_hash` (SHA-256 of a 256-bit secret) | Non-interactive access, often held by CI where secrets leak most easily. |
| A7 | **Uploaded scan files and attachments** | `nr_files.content` (BLOB), staging directory during upload | Same disclosure value as A1, plus whatever the customer attached to a risk (contracts, architecture diagrams). |
| A8 | **Risk register and management reviews** | `risks`, `mgmt_reviews`, `risk_acceptances` | Commercially sensitive: unmitigated risk the organisation has formally accepted. |
| A9 | **The database connection string** | Environment / user-secrets / `appsettings.json` | Direct access to everything above, bypassing every application-layer control. |
| A10 | **TLS private keys** | Operator-supplied `.pfx`; **see finding NR-2026-003** | Impersonation of the API, decryption of captured sessions. |
| A11 | **Audit trail** | `audit_log`, `schema_upgrade_log` | Its integrity is what makes an incident reconstructible. Tampering is the cover-up. |

---

## 2. Trust boundaries and data flow

```
                    ┌──────────────────────────── UNTRUSTED ────────────────────────────┐
                    │                                                                   │
   ┌────────────┐   │  TB1                       ┌─────────────┐   TB4                  │
   │  GUIClient │───┼──── HTTPS ────────────────►│             │◄──── .nessus/.xml ──────┘
   │  (desktop) │   │   Basic → JWT              │             │      scan file upload
   └────────────┘   │   ClientId header          │             │
                    │                            │             │
   ┌────────────┐   │  TB1                       │     API     │   TB5
   │ CI runner  │───┼──── HTTPS, Bearer nrk_ ───►│  (Kestrel)  │◄──── plugin assembly ──── TB5
   └────────────┘   │                            │             │      (in-process, full trust)
                    │                            │             │
   ┌────────────┐   │  TB1                       │             │   TB6
   │ IdP / SCIM │───┼──── HTTPS, Bearer scim_ ──►│             │────► outbound: Slack, Jira,
   └────────────┘   │     SAML POST / OIDC       │             │      Teams, Trend Micro, SMTP
                    │                            └──────┬──────┘
   ┌────────────┐   │  TB1                              │
   │  Browser   │───┼──── HTTPS (SSO consent) ──────────┤ TB2
   └────────────┘   │                                   │ EF Core / MySqlConnector
                    └───────────────────────────────────┼──────────────────────────────
                                                        ▼
                                                ┌───────────────┐
                                                │   MariaDB     │
                                                └───────────────┘
                                                        ▲
                        ┌───────────────┐   TB2          │
                        │ BackgroundJobs│────────────────┘
                        │  (Hangfire)   │
                        └───────┬───────┘
                                │ TB3  signed /sync (Ed25519), one-way push
                                ▼
                        ┌───────────────┐        ┌──────────┐
                        │   WebSite     │───────►│  SQLite  │
                        │  (public)     │        └──────────┘
                        └───────────────┘
```

| Boundary | Between | Crossing data | Direction of trust |
|---|---|---|---|
| **TB1** | Any client ↔ API | Credentials, tokens, every domain object | The API trusts **nothing** from the client. |
| **TB2** | API / BackgroundJobs / ConsoleClient ↔ MariaDB | Full data set | Mutual: a compromise either way is total. |
| **TB3** | API ↔ WebSite | Release metadata, download counters | One-way, signed. The WebSite never reaches into the database. |
| **TB4** | Scan file ↔ importer | Attacker-authored XML/JSON/CSV | The importer trusts nothing. The file is authored by whoever ran the scanner — or by whoever persuaded someone to import theirs. |
| **TB5** | Plugin ↔ host | .NET assembly loaded in-process | **No boundary in practice.** A plugin runs with the API's full authority. See §5. |
| **TB6** | API ↔ third-party service | Credentials outbound, response bodies inbound | The API trusts the operator's URL but not the response. |

### The request flow the spec asks about, annotated

`GUIClient view → ReactiveUI view-model → ClientServices REST service → HTTPS → API controller → ServerServices → DAL (NRDbContext) → MariaDB`

* **GUIClient → ClientServices.** In-process, same trust domain. Client-side validation is a
  usability feature; nothing here is a control. *All* of it is re-decided server-side.
* **ClientServices → API (TB1).** TLS with certificate validation (finding NR-2026-004 removed an
  unconditional bypass). Credentials: HTTP Basic on first sign-in, then a bearer JWT. Every request
  additionally carries a `ClientId` naming an administrator-approved device registration — a second
  factor of sorts, and the gate that makes the SSO flow safe (NR-2026-001).
* **API controller.** Authorization happens here and only here for the *coarse* decision: an
  `[Authorize]` policy or `[PermissionAuthorize]` permission. An action with no attribute falls
  through to `DefaultPolicyProvider.GetFallbackPolicyAsync`, which requires an authenticated,
  existing user — so unannotated is denied, not open. `ControllerAuthorizationInventoryTest`
  enforces that every action is either annotated or on a reviewed anonymous allowlist.
* **ServerServices.** Domain logic. Sieve applies caller-supplied filter/sort/page expressions; it
  is parameterised, so this is not an injection surface, but it *is* a surface for asking questions
  the caller should not be able to ask — which is why scoping is not left to this layer.
* **DAL (TB2).** The tenancy boundary. `NRDbContext` carries an `EntityScope` derived from the
  caller's claims and enforces it as **EF global query filters** on every entity carrying an
  `entity_id`, plus a write-side guard in `AuditableContext.SaveChanges`. This is deliberately *not*
  a call each service must remember: the previous design was an extension method invoked from
  exactly one query with a null principal, which filtered nothing at all. Model-level filters cannot
  be forgotten.

---

## 3. STRIDE per boundary

Only threats with a real path are listed. **[NR-…]** cross-references
[FINDINGS.md](FINDINGS.md); *(mitigated)* means the control was already present and was verified,
not assumed.

### TB1 — client ↔ API

| STRIDE | Threat | Status |
|---|---|---|
| **S**poofing | Guess or brute-force a password | Progressive lockout + per-source rate limit **[NR-2026-008]**; bcrypt cost 15 |
| S | Forge a session token | HMAC-SHA256 over a 190-bit installation key; issuer, audience, algorithm and lifetime all validated **[NR-2026-012]** |
| S | Predict a security token (reset link, file key, SAML request id) | CSPRNG **[NR-2026-002]** |
| S | Take over a session through the desktop SSO flow | Server-minted request id, approved-client gate, explicit browser consent, single-use redemption **[NR-2026-001]** |
| S | Forge a SAML assertion | Signature verification was disabled in the shipped configuration **[NR-2026-010]** |
| S | Intercept the session by presenting any certificate | Certificate validation now on by default **[NR-2026-004, NR-2026-005]** |
| **T**ampering | Alter another tenant's data | EF global query filters + `SaveChanges` guard *(mitigated, Track 2.3.1)* |
| T | Write outside the upload directory | Path allowlist + containment check **[NR-2026-006]** |
| **R**epudiation | Act without attribution | `AuditableContext` records actor and timestamp *(mitigated)* |
| **I**nformation disclosure | Read another tenant's findings | Query filters *(mitigated)* |
| I | Read another user's attachment by naming it | `GET /Files/{name}` has no per-file ACL; the name is the capability, now a 256-bit token **[NR-2026-017 — partially open]** |
| I | Keep access after being disabled | Basic auth ignored the `enabled` flag **[NR-2026-007]** |
| I | Keep access after a password change | JWTs issued before `last_password_change` are refused **[NR-2026-012]** |
| **D**enial of service | Exhaust bcrypt capacity | Rate limiter on the credential endpoints |
| **E**levation of privilege | Reach an unannotated endpoint | Fallback deny policy + inventory test **[NR-2026-009]** |

### TB2 — application ↔ MariaDB

| STRIDE | Threat | Status |
|---|---|---|
| **T** | SQL injection through a domain query | EF Core parameterises; Sieve parameterises. Audited: no `FromSqlRaw`, no string-built domain SQL |
| T | SQL injection through the numbered-SQL upgrade machinery | Reviewed statement by statement; the only interpolations are operator-supplied schema/table identifiers from the connection string and `SchemaUpgradePhases.yaml`, both trusted config. Two `information_schema` lookups parameterised anyway **[NR-2026-021]** |
| **I** | Read the connection string from a deployed `appsettings.json` | Documented as an environment/secret-store value; the Puppet templates still write it to disk **[NR-2026-025]** |
| **E** | The application account is more privileged than it needs | Documented least-privilege grant in [DATA_PROTECTION.md](DATA_PROTECTION.md) |

### TB3 — API ↔ WebSite (`/sync`)

| STRIDE | Threat | Status |
|---|---|---|
| **S** | Push forged release data | Ed25519 signature over the canonical payload; trust-on-first-use enrolment then pinned *(mitigated, Track 6)* |
| **T** | Replay a captured push | Nonce + timestamp window *(mitigated)* |
| **I** | Read the pushed payload in transit | TLS; the `--insecure` flag disables validation and now says so loudly **[NR-2026-026]** |

### TB4 — scan file ↔ importer

| STRIDE | Threat | Status |
|---|---|---|
| **I** | XXE — read a server file through an external entity | `DtdProcessing.Prohibit` + `XmlResolver = null` on every importer; **verified by `ImporterXxeTest`**, which asserts the refusal rather than trusting the comment |
| **I/D** | SSRF or entity-expansion through the DTD | Same control; the legacy `NessusClientData_v2.ParseAsync` path (unreachable today) was hardened at the call site **[NR-2026-022]** |
| **T** | Poison the register with fabricated findings | Accepted: importing is an authorised action by a user with `vulnerabilities_create`. The audit trail records who imported what |
| **E** | Escape the parser into code execution | `XmlSerializer` over a fixed generated type graph — no polymorphic type resolution, so no deserialization gadget |

### TB5 — plugin ↔ host

| STRIDE | Threat | Status |
|---|---|---|
| **E** | A plugin does anything the API can | **Accepted and documented.** A plugin is a .NET assembly loaded in-process; .NET has no supported in-process sandbox (Code Access Security was removed in .NET Core). Installing a plugin is equivalent to trusting the operator who installed it. **[NR-2026-027]** — the mitigation is provenance (signature verification before load), not confinement |

### TB6 — API ↔ third-party service

| STRIDE | Threat | Status |
|---|---|---|
| **I** | SSRF: point an integration at the cloud metadata service and read the response | `OutboundUrlPolicy` refuses link-local and the metadata addresses always; private ranges are allowed by default because on-premise Jira is the normal case, and can be refused by configuration **[NR-2026-013]** |
| I | Log a webhook URL, which is itself a credential | Only the host is logged *(mitigated, Track 4)* |
| **S** | Accept a forged inbound webhook | HMAC over the raw body (GitHub, GitLab) or a shared URL secret (Jira, Azure DevOps), all compared in constant time **[NR-2026-019]** |
| **T** | Follow a redirect to a blocked destination | `AllowAutoRedirect = false`, so the policy cannot be bypassed with a 302 |

---

## 4. Existing controls, mapped

| Control | Where | Verified by |
|---|---|---|
| Default-deny authorization | `DefaultPolicyProvider.GetFallbackPolicyAsync` | `ControllerAuthorizationInventoryTest` |
| Permission model | `PermissionPolicyProvider`, `PermissionAuthorizeAttribute` | `API.Tests` per controller |
| Tenant isolation | EF global query filters + `AuditableContext.SaveChanges` | `EntityScopeEnforcementTest`, `MultiEntityScopedAccessTest`, `MultiEntityRolesSchemaTests` |
| Password storage | bcrypt, work factor 15 | `UsersServiceInMemoryTest` |
| Brute-force throttling | `LoginAttemptTracker` + `AuthRateLimiting` | `LoginAttemptTrackerTest` |
| Session revocation | `last_password_change` vs token `iat` | `JwtAuthenticationHandler` |
| Credential encryption at rest | `SecretProtector` → AES-256-GCM | `SecretProtectorTest`, `AesGcm256Test` |
| Token hashing | SHA-256 + `FixedTimeEquals` | `ApiTokensAndGateTest`, `ScimServiceInMemoryTest` |
| XXE prevention | `DtdProcessing.Prohibit`, `XmlResolver = null` | `ImporterXxeTest` |
| Path containment | `SafePathTool` | `SafePathToolTest`, `FilesServiceUploadPathTest` |
| SSRF policy | `OutboundUrlPolicy` | `OutboundUrlPolicyTest` |
| Transport | TLS 1.2+ floor, validated certificates | `ServerCertificatePolicyTest` |
| Security headers | `SecurityHeaderPolicy` on API and WebSite | `SecurityHeaderPolicyTest` |
| Supply chain | Dependabot, `dotnet list package --vulnerable` gate, CycloneDX SBOM, submodule-review gate | `ContinuousSecurityConfigurationTest`, `SbomTest` |

---

## 5. Accepted risks

Dogfooding the product's own risk-acceptance concept (Track 3.2.3): each of these is a conscious
decision with a stated reason and a review date, not an oversight.

| Id | Risk | Why accepted | Review |
|---|---|---|---|
| **TM-A1** | A plugin has the API's full authority | .NET offers no in-process sandbox. Confinement would mean a separate process and an IPC contract — a redesign, not a fix. Mitigation is provenance. | Each minor release |
| **TM-A2** | Private and loopback addresses are reachable by integrations | Refusing them would break the on-premise deployments this product exists for. Metadata endpoints are refused unconditionally; the rest is opt-in via `Integrations:BlockPrivateNetworks`. | 2027-02 |
| **TM-A3** | Brute-force counters are per process and in memory | A persisted counter is a schema change and a write on every failed login. The in-memory throttle removes the unbounded case; the residual is a multi-instance deployment. | Tracked as NR-2026-008b |
| **TM-A4** | A database administrator can read everything | Application-level encryption of the whole dataset would make the product unusable (no filtering, no sorting, no reporting). Credentials and secrets *are* encrypted; the register is not. | Permanent, documented in DATA_PROTECTION.md |
| **TM-A5** | No per-file access control on attachments | Needs an ACL model that does not exist yet. Mitigated to a capability URL with a 256-bit unguessable name. | NR-2026-017, Track 8 |

---

## 6. Review cadence

Living document, per 7.1.1 and 7.5.3. Each **minor** release:

1. re-walk §2 against the actual code — has a new tier, host or boundary appeared?
2. re-run STRIDE for anything that changed;
3. reconcile §4 against the test names — a control with no test is a claim, not a control;
4. re-date §5 and either renew or close each acceptance;
5. record the delta in [BURN_DOWN.md](BURN_DOWN.md).

The specific trap this document is written to avoid: NetRisk has shipped a control that was
documented as working and was not (`ApplyEntityScope`, fixed in a386faaf) and a backend that was
marked complete and did not exist. **Every "mitigated" above names the test that proves it.** A
review that cannot name the test must downgrade the claim.
