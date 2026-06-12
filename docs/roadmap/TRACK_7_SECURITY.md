# Track 7 — Security Review & Hardening: Detailed Specifications

> Status: **Planned** · Roadmap: [ROADMAP.md → Track 7](../../ROADMAP.md)
> Research basis: OWASP guidance and industry hardening practices (June 2026) — sources at the end of each milestone.

A full, end-to-end security review across every tier (API, ServerServices, DAL, ClientServices, GUIClient, BackgroundJobs, WebSite, Plugins), producing a prioritized findings register and a remediation backlog. As a security/GRC product, NetRisk holds itself to the standards it helps customers enforce. The output of 7.1 feeds concrete, scheduled work into 7.2–7.5. Security artifacts live under `docs/security/`.

---

## Milestone 7.1: Comprehensive Security Audit

### 7.1.1 Threat-model the request flow

- Follow the OWASP threat-modeling process: scope from the existing architecture (GUIClient → ClientServices → API → ServerServices → DAL, plus BackgroundJobs, WebSite, plugin loading, file imports).
- Produce data-flow diagrams with explicit **trust boundaries**: desktop↔API, API↔DB, plugin↔host, scan-file↔parser.
- Enumerate threats per boundary with STRIDE.
- Output: `docs/security/THREAT_MODEL.md` with an asset inventory (credentials, biometric data, vulnerability data — itself sensitive), threat list, and existing-control mapping.
- Treat it as a **living document** reviewed each minor release (ties to 7.5.3).

### 7.1.2 OWASP ASVS / Top 10 codebase audit

- Audit against **ASVS Level 2** (the appropriate bar for a security product), chapter by chapter, explicitly covering:
  - authN/authZ on every controller;
  - input validation;
  - SQL/EF injection — including raw SQL in the numbered-SQL upgrade machinery and any `FromSqlRaw`;
  - command/path injection;
  - secrets handling;
  - crypto usage (password hashing/KDF, token signing);
  - deserialization (plugin loading, YAML manifests, scan-file parsers);
  - SSRF (webhook/issue-tracker outbound calls once Track 4 lands);
  - file-upload/import paths — the Nessus parser is a prime attack surface: fuzz the XML parser with malicious `.nessus` files, check for XXE.
- Record per-requirement pass/fail/N-A in a checklist committed to `docs/security/`.

### 7.1.3 Prioritized findings register

- `docs/security/FINDINGS.md` (or YAML for tooling). Per finding: id, title, severity via the OWASP Risk Rating methodology (likelihood × impact), affected tier/file, exploitability notes, proposed fix, owner, target milestone.
- Each finding **must** be triaged into milestones 7.2–7.5 or explicitly risk-accepted with justification — dogfooding the product's own risk-acceptance concept (Track 3.2.3).
- Sensitive details that would weaponize the public repo stay in a private tracker; the register references ids.

### 7.1.4 Recurring `/security-review` gate

- Run the repo's `/security-review` skill over the current branch now; commit the baseline report as `docs/security/baseline-<date>.md`.
- Make it recurring: per release branch and per security-sensitive PR; new findings are triaged into the register.
- Track the burn-down (links to 7.5.3).

**Sources (7.1):** [OWASP — Threat Modeling Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html) · [OWASP — Risk Rating Methodology](https://owasp.org/www-community/OWASP_Risk_Rating_Methodology) · [Appknox — ASVS overview](https://www.appknox.com/blog/all-you-need-to-know-about-owasp-asvs)

---

## Milestone 7.2: Dependency & Supply-Chain Security

### 7.2.1 Automated dependency scanning

- Enable Dependabot (`.github/dependabot.yml`) for NuGet across all `src/` projects, `build/`, **and each `libs/` submodule's manifests**, plus GitHub Actions version updates.
- CI job running `dotnet list package --vulnerable --include-transitive` that **fails** on known-vulnerable packages, with a documented, *expiring* suppression file for accepted ones.
- Cadence: weekly schedule + on every PR touching csproj/lock files.

### 7.2.2 SBOM in the Nuke `Package*` targets

- Integrate **CycloneDX for .NET** (`CycloneDX.MSBuild` or the dotnet tool) into the `Package*` targets: every artifact ships a `netrisk-<component>-<version>.cdx.json` alongside it.
- Generated **at build time** from resolved dependencies (not ad-hoc) — the key practice, capturing exact versions.
- Published with releases (WebSite / GitHub Releases).
- Optional: push SBOMs to an OWASP Dependency-Track instance for continuous new-CVE monitoring against released versions.

### 7.2.3 Submodule provenance & patching policy

- Pin all `libs/` submodules (`NessusParser`, `Aura.UI`, `netrisk-plugin-sdk`, `reliable-rest-client-wrapper`) to reviewed commit SHAs; make the review explicit — a CI check requires submodule bumps to arrive via PRs with a diff summary.
- Document in `docs/security/SUPPLY_CHAIN.md`: upstream review procedure, cadence for syncing upstream security fixes, criteria for vendoring vs. tracking, ownership per submodule.

**Sources (7.2):** [OWASP Dependency-Track](https://dependencytrack.org/) · [CycloneDX Tool Center](https://cyclonedx.org/tool-center/) · [OWASP — Dependency Graph SBOM Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Dependency_Graph_SBOM_Cheat_Sheet.html)

---

## Milestone 7.3: AuthN/AuthZ & Secrets Hardening

### 7.3.1 Controller authorization sweep

- Inventory every API controller/action; make `[Authorize]` the default via a **fallback policy** in `Program.cs` so *unannotated = denied*; explicit `[AllowAnonymous]` only on login/health/download endpoints, each justified in the findings register.
- Verify role/entity scoping is applied in `ServerServices` (not just controllers — defense in depth; the layer Track 2.3 formalizes).
- Regression-proofing: an automated test reflecting over all endpoints that **fails** when a new anonymous endpoint appears without being on the allowlist.

### 7.3.2 Token, session, password, biometric, and lockout audit

- **JWT:** validate issuer/audience/lifetime/signing key; shorten access-token lifetime toward the 5–15 min guideline with refresh tokens; ensure server-side revocation on logout/disable.
- **Passwords:** confirm a modern KDF (Argon2id, or PBKDF2 at current OWASP iteration counts); migrate legacy hashes on next login if weaker.
- **FaceID/biometric:** review template storage (encrypted at rest), replay protection on `BiometricTransaction`, fallback-path strength.
- **Lockout/brute force:** Track 6 confirmed `failed_login_attempts` carries **no live lockout logic** — implement progressive lockout/throttling plus ASP.NET rate limiting on auth endpoints, with audit events.

### 7.3.3 Secrets hygiene

- Full-history secret scan (gitleaks/trufflehog); rotate anything found; record in the register.
- Add gitleaks to CI (links to 7.5.1).
- Confirm runtime config standardizes on user-secrets (dev) / environment variables or secret store (prod), per the existing convention; no connection strings or SMTP/webhook credentials in `appsettings*.json`.
- Write `docs/security/SECRETS.md`: where each secret lives and its rotation procedure (DB, JWT signing key, SMTP, signing certs, API tokens).

---

## Milestone 7.4: Data Protection & Transport Security

### 7.4.1 TLS enforcement & certificate validation

- Sweep every `HttpClient`/RestClient construction (ClientServices, `reliable-rest-client-wrapper`, future Track 4 outbound calls) for disabled certificate validation (`ServerCertificateCustomValidationCallback` returning true, `DangerousAcceptAnyServerCertificateValidator`) — remove, or gate strictly behind an explicit dev-only flag with a loud warning.
- Enforce TLS 1.2+ minimum on Kestrel and outbound calls.
- Document a supported path for private-CA deployments (custom CA trust, **not** validation bypass).
- GUIClient must hard-fail with a clear error on an invalid server certificate — never silently proceed.

### 7.4.2 Encryption at rest & KDF review

- Classify stored data: credentials, biometric templates, uploaded scan files, attachments, API tokens.
- Encrypt uploaded files and biometric data at rest — application-level AES-GCM with the key from the secret store (MariaDB TDE can't be assumed in self-hosted FOSS deployments).
- Verify hashing choices per 7.3.2; ensure Track 4 channel/issue-tracker credentials and Track 3.5 API tokens follow the encrypted-settings pattern.
- Document the at-rest posture and key-rotation story in `docs/security/DATA_PROTECTION.md`.

### 7.4.3 CORS, security headers, and cookies on `API` and `WebSite`

- **CORS:** never `AllowAnyOrigin` in production; explicit origin list from config; never wildcard with credentials.
- **Headers middleware** on both API and WebSite: `Strict-Transport-Security` (sensible max-age ramp-up), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, a CSP on the WebSite; remove chatty headers (`Server`, `X-Powered-By`).
- **Cookies** (WebSite / any auth cookies): `Secure; HttpOnly; SameSite=Lax|Strict`.
- **Acceptance:** a clean run of a security-headers scanner (Mozilla Observatory-equivalent) captured in the audit doc.

**Sources (7.3/7.4):** [ASP.NET Hacker — HTTP security headers](https://asp.net-hacker.rocks/2025/07/23/web-api-headers.html) · [DevelopersVoice — API security in ASP.NET Core](https://developersvoice.com/blog/secure-coding/api-security-in-asp-net-core/)

---

## Milestone 7.5: Continuous Security in CI/CD

### 7.5.1 SAST + secret scanning, failing on new highs

- Add CodeQL (C#) and gitleaks to GitHub Actions on PR + main.
- **Baseline current findings** so only *new* high/critical findings fail the build — the practice that keeps gates from being disabled out of frustration.
- Failure output links the rule and fix guidance; suppressions require an inline justification comment reviewed in PR.
- Results feed the 7.1.3 register.

### 7.5.2 Disclosure policy (`SECURITY.md`) + internal triage SLA

- Root `SECURITY.md`: supported-versions table, private reporting channel (GitHub private vulnerability reporting and/or security@ address), expected acknowledgment time (e.g., 72 h), coordinated-disclosure window (e.g., 90 days), safe-harbor language for good-faith research.
- Internal companion doc: triage SLAs per severity (e.g., Critical — acknowledge 24 h / fix target 7 d), **deliberately consistent with the SLA defaults NetRisk ships in Track 3.4** — the project holds itself to its own product's standards.

### 7.5.3 Periodic re-audits & remediation burn-down

- Each minor release: run the `/security-review` gate, refresh threat-model deltas, re-run the ASVS checklist for changed areas, update the findings register.
- Track remediation burn-down (open findings by severity over time) — chart committed to `docs/security/`, or, fittingly, tracked *inside NetRisk itself* as risks.
- Release checklist item in the Nuke release flow: a release cannot ship with an untriaged critical finding.

**Sources (7.5):** [OWASP — CI/CD Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/CI_CD_Security_Cheat_Sheet.html) · [Aembit — Pipeline secrets checklist](https://aembit.io/blog/ci-cd-security-checklist-eliminate-secrets-workload-identity/)

---

## Dependencies & sequencing

- **7.1 deliberately precedes and feeds 7.2–7.5** — its findings register is the work queue.
- 7.2.2 (SBOM) coordinates with Track 5's `Package*` target work.
- 7.3.1's entity-scoping verification depends on Track 2.3 landing (or audits the current role model until then).
