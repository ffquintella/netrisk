# Track 3 — Vulnerability Aggregation & Finding Lifecycle (ASPM): Detailed Specifications

> Status: **Planned** · Roadmap: [ROADMAP.md → Track 3](../../ROADMAP.md)
> Research basis: web survey of ASPM/vulnerability-management best practices (June 2026) — sources at the end of each milestone.

This track bridges GRC with Application Security Posture Management: ingest, deduplicate, and triage automated scanner output. New entities follow the Track 6 schema conventions (see [CLAUDE.md](../../CLAUDE.md)) and ship through the numbered-SQL `db_version` path.

---

## Milestone 3.1: Extensible Scanner Importers

**Market context.** The industry has converged on **SARIF** as the interoperability format for code scanners; ASPM platforms differentiate on breadth of native connectors plus an API-first path for custom tools, with normalization and deduplication at ingestion time.

### 3.1.1 `IVulnerabilityImporter` plugin contract in `netrisk-plugin-sdk`

**Contract** (lives in the SDK submodule so third parties can implement it):
- Identity: `Name`, `DisplayName`, `Version`, `SupportedFileExtensions` / `SupportedMimeTypes`.
- `bool CanHandle(Stream sample)` — content sniffing for auto-detection.
- `Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)` returning normalized `Vulnerability` + `Host` + `CVEDetail` graphs **without touching the DB** — persistence, deduplication (3.3), and entity scoping stay in `ServerServices`.
- `ImportResult` includes per-record warnings and skipped-row diagnostics — silent drops are the classic importer bug.
- Version the contract explicitly (`ImporterContractVersion`) so SDK upgrades don't silently break third-party plugins.

**Normalized model** must capture everything dedup (3.3) and SLA (3.4) need:
tool name, tool-native unique id, rule/plugin id, title, normalized severity **plus the tool's raw value preserved**, CVE/CWE lists, CVSS vector/score, affected host/port/path/location, first/last seen, raw evidence blob.

### 3.1.2 Refactor the legacy Nessus parser onto the contract

- Wrap the existing `NessusParser` (`libs/NessusParser`) as the first `IVulnerabilityImporter` implementation.
- **Parity gate:** import a corpus of real `.nessus` fixtures through both paths and diff the resulting records before deleting the legacy path. Any field the Nessus path needs that the contract lacks is a contract bug.

### 3.1.3 Native importers: ZAP, Trivy, Semgrep, OpenVAS, Burp Suite, Snyk, Grype, Dependabot

- One importer per tool against its canonical output: ZAP JSON, Trivy JSON, Semgrep JSON/SARIF, OpenVAS XML, Burp XML/JSON, Snyk JSON, Grype JSON, Dependabot via GitHub API/SARIF export.
- **Add a generic SARIF 2.1 importer** — SARIF ingestion alone unlocks dozens of additional scanners and is what ASPM buyers check first; Semgrep and Dependabot can ride on it where their native formats are thin.
- Each importer ships with: a configurable severity-mapping table (tool scale → NetRisk scale), fixture files from real scans in the test project, and documented field mapping under `docs/features/`.

**Definition of done per importer:** imports a real-world sample of ≥1000 findings without error; severity mapping verified; re-import of the same file creates zero duplicates (depends on 3.3).

### 3.1.4 API modernization: dynamic importer discovery

- `GET /vulnerabilities/importers` lists importers discovered via `IPluginsService` (name, display name, extensions, version) — built-ins and plugins indistinguishable to callers.
- `POST /vulnerabilities/import/{importerName}/{fileId}` resolves the importer by name (404 with the available list on unknown name) and runs the import **as a background job**, returning an import-job id.
- `GET /vulnerabilities/import-jobs/{id}`: status (queued/running/succeeded/failed) + counts (new, updated, duplicates, skipped, warnings). Large scan files make synchronous import a timeout trap.
- Optional `auto` importer name triggers `CanHandle` content sniffing.

### 3.1.5 GUIClient: dynamic importer selector in the import dialog

- The import dialog fetches the importer list and presents a combo (auto-detect default based on extension/sniffing), with importer description/version.
- Shows live job progress and the final per-import summary (new/updated/duplicate/skipped + downloadable warning list).
- Follows the established dialog standards (`DialogWindowBase`, button taxonomy, localized strings).

**Sources (3.1):** [Checkmarx ASPM — SARIF ingestion](https://checkmarx.com/product/aspm/) · [Phoenix Security — Semgrep integration](https://phoenix.security/phoenix-security-integration-semgrep/) · [ScanDog — What is ASPM](https://scandog.io/blog/aspm-fundamentals/what-is-aspm)

---

## Milestone 3.2: Finding Lifecycle & Audit Trails

**Reference model.** DefectDojo's status machine (`Active`, `Inactive`, `Duplicate`, `Mitigated`, `False Positive`, `Out of Scope`, `Risk Accepted`) is the de-facto FOSS standard — and crucially, triage verdicts are **sticky across re-imports**: a finding marked False Positive stays suppressed when the scanner reports it again.

### 3.2.1 Granular lifecycle states

- `FindingStatus` enum: `Active`, `Verified`, `FalsePositive`, `OutOfScope`, `Duplicate`, `RiskAccepted`, `Mitigated` — int-backed with explicit `HasConversion<int>()`, stored in a `status_id` column (the pattern Track 6 Phase 5 established on `risks`).
- Transition matrix enforced in `ServerServices`: e.g. `Mitigated` only from `Active`/`Verified`; `Duplicate` requires a link to the canonical finding; any → `FalsePositive` allowed with mandatory reason. Invalid transitions throw a domain exception surfaced as HTTP 422.
- **Sticky triage:** re-imports respect `FalsePositive`/`OutOfScope`/`RiskAccepted` — update `last_seen`, do not reactivate. A `Mitigated` finding seen again by a scanner **does** reactivate (regression detection) with an audit event.
- Migration maps current status values; applied via a numbered-SQL phase.

### 3.2.2 Audit logging of state transitions (who, when, why)

- Append-only `finding_status_history` table: finding id, from-status, to-status, user id, UTC timestamp, mandatory free-text justification for suppressing transitions (`FalsePositive`/`OutOfScope`/`RiskAccepted`), and source (`Manual`, `Import`, `Job`, `IssueSync`).
- No update/delete API on history rows. Rendered as a timeline tab on the finding detail view; included in exports.
- Framing: the audit trail is what keeps "accepted risk" from becoming "forgotten risk" — it is an auditor-facing artifact, not a debug log.

### 3.2.3 Dedicated `RiskAcceptance` entity

- Entity (`risk_acceptances`): id, name/summary, business justification (rich text), authorizing manager (FK user), **expiration date (required)**, optional compensating-controls text, attachment/artifact file links, audit fields, status (`Active`, `Expired`, `Revoked`), many-to-many to findings (one acceptance can cover a set).
- Accepting findings flips them to `RiskAccepted` and records the history event; revoking or expiring reactivates them (3.2.4).
- API + GUIClient management view with an "expiring within 30 days" filter.

### 3.2.4 Hangfire job to re-open expired risk acceptances

- Daily job: `Active` acceptances past expiry → mark `Expired`, transition covered findings back to `Active` (history event, source=`Job`), notify the authorizing manager + finding owners.
- Pre-expiry warnings at T-30 / T-7 days (configurable).
- Idempotent and safe to re-run; failures alert via the standard job-failure path.

**Acceptance criteria:** an acceptance expiring yesterday is processed on the next run exactly once; each finding's timeline shows the automated reactivation.

**Sources (3.2):** [DefectDojo — Finding status definitions](https://docs.defectdojo.com/triage_findings/findings_workflows/finding_status_definitions/) · [DefectDojo — Risk acceptances](https://docs.defectdojo.com/triage_findings/findings_workflows/pro__risk_acceptance/) · [DefectDojo — Auto-triage & dedup](https://defectdojo.com/blog/auto-triage-and-deduplicate-security-findings-to-reduce-alert-fatigue)

---

## Milestone 3.3: Intelligent Deduplication Engine

**Research basis.** Mature dedup uses **layered strategies** — tool-native unique ids first, then algorithmic fingerprints built from stable components (tool + rule id + normalized location + asset). The key property: the fingerprint survives cosmetic changes (line drift, description edits). GitLab's location fingerprint (file + scope + offset) is the canonical example. Dedup must *group, not discard*: keep every sighting as evidence under one canonical finding.

### 3.3.1 Modular matching strategies

`IDeduplicationStrategy` with `string ComputeKey(NormalizedFinding f)`:

| Strategy | Behavior |
|---|---|
| `UniqueIdFromTool` | The scanner's own GUID (Snyk issue id, Dependabot alert number, …). Highest precedence when present. |
| `HashBased` | SHA-256 over a configurable, ordered field set. Default: tool, rule/plugin id, normalized host/asset, port/path/location, CVE. **New default.** |
| `LegacyHashCode` | The current matching behavior, kept for backward compatibility with already-imported data. |
| `Custom` | Plugin-provided via the SDK. |

- Per-scanner configuration chooses the strategy **chain** (first non-null key wins).
- Computed keys are **persisted** on the finding (`dedup_key` column, indexed) — never recomputed on the fly, so algorithm upgrades only affect new imports unless a re-key migration is run deliberately.

### 3.3.2 Update-don't-duplicate on import, with scan history

- Per-finding import pipeline: compute key → look up open finding with the same key (within the same entity scope) → if found: update `last_seen`, severity/CVSS if changed (history event), increment occurrence count; if not: create.
- Findings present in previous scans but absent from the current **full** scan of the same scope are candidates for auto-close — configurable per scanner, **default off** (partial scans must not close findings).
- New `scan_imports` log table: file, importer, who, when, counts (new/updated/closed/duplicate/skipped) — every import is reconstructible.

**Acceptance criteria:** importing the same Nessus file twice yields zero new findings on the second pass; a newer scan of the same host updates `last_seen` on persisting findings.

### 3.3.3 Administration UI for dedup heuristics per scanner

- Admin view: each importer with its active strategy chain and `HashBased` field set (checkbox list).
- **Preview/test panel:** pick or paste two findings, see their computed keys and whether they'd merge — heuristic changes validated before saving.
- Change history recorded; settings persisted via the existing settings storage.

**Sources (3.3):** [GitLab — Vulnerability deduplication process](https://docs.gitlab.com/user/application_security/detect/vulnerability_deduplication/) · [Strobes — Vulnerability deduplication](https://strobes.co/blog/vulnerability-deduplication-security/) · [Invicti — ASPM dedup](https://www.invicti.com/blog/web-security/aspm-tools-vulnerability-deduplication)

---

## Milestone 3.4: SLA Tracking & Aging

**Benchmarks.** CISA: criticals remediated in ~15 days, highs ~30, KEV-listed in 14 (adversaries exploit in ~15 days on average). Common industry ladders: Critical 24–72h…15d, High 30d, Medium 60–90d, Low 90–180d. The most-demanded views are SLA-compliance-by-severity widgets and overdue/aging reports.

### 3.4.1 `SlaConfiguration` schema

- Entity (`sla_configurations`): per severity, `max_triage_days` and `max_remediation_days`; one global default + optional per-entity override (composes with 2.3).
- **Effective-dated** so changing the policy doesn't retroactively rewrite past compliance numbers.
- Seed defaults aligned to CISA: remediation Critical 15 / High 30 / Medium 60 / Low 90 days; triage 2 / 5 / 10 / 15 days.
- Admin CRUD UI with the benchmark values shown as guidance.

### 3.4.2 Computed `SlaDueDate` and `DaysOverdue`

- `sla_due_date` computed at finding creation (`first_seen` + remediation days for its severity, from the config effective at that date); **recomputed on severity change** (history event notes the recompute).
- `DaysOverdue` computed at query time (`today − due_date`, floor 0) — not stored, no nightly drift.
- Suppressed states (`RiskAccepted`, `FalsePositive`, `OutOfScope`) pause the SLA clock.
- Surfaced in: vulnerability grids (sortable/Sieve-filterable columns, overdue rows in alert color), a dashboard SLA-compliance widget (% within SLA by severity), and exports.

### 3.4.3 Breach notifications (email + webhook)

- Daily Hangfire job evaluating thresholds: approaching (T-7, T-3, T-1 — configurable) and breached.
- **Digest-style** notifications: one email per owner listing their at-risk findings (per-finding spam is the known anti-pattern); fires Track 4 channel events `sla.approaching` / `sla.breached`.
- De-duplicated: each finding+threshold notifies once (`sla_notifications` log table guards idempotence).

**Acceptance criteria:** a finding crossing its due date triggers exactly one breach notification; rerunning the job sends nothing new.

**Sources (3.4):** [CISA — Remediate vulnerabilities for internet-accessible systems](https://www.cisa.gov/sites/default/files/publications/CISAInsights-Cyber-RemediateVulnerabilitiesforInternetAccessibleSystems_S508C.pdf) · [Tenable — SLAs and remediation](https://docs.tenable.com/cyber-exposure-studies/cyber-exposure-insurance/Content/SLARemediation.htm) · [Secure.com — Remediation SLAs by severity](https://www.secure.com/blog/vulnerability-remediation-slas) · [Rootshell — VM SLAs](https://www.rootshellsecurity.net/vulnerability-management-sla/)

---

## Milestone 3.5: CI/CD-First Integration API

**Research basis.** CI integration demands non-interactive, *scoped*, revocable tokens (never user passwords), idempotent upload endpoints, and exit-code gating. OWASP CI/CD guidance stresses least-privilege per-pipeline identities.

### 3.5.1 Scoped API-token authentication

- `api_tokens` table: random ≥256-bit secret **shown once and stored hashed**; prefix scheme (`nrk_` + key id) so leaked tokens are grep-able by secret scanners; scopes (`vulnerabilities:import`, `vulnerabilities:read`, …); optional expiry; optional entity binding (composes with 2.3); `last_used_at`; instantly revocable.
- New auth handler accepting `Authorization: Bearer nrk_…` alongside existing user auth; token principals carry scope claims; endpoints annotated with required scopes; per-token rate limiting.
- Admin UI + API for issue/list/revoke with full audit (who created, for what).

### 3.5.2 Bulk, idempotent direct-upload endpoints

- `POST /vulnerabilities/import/{importer}` accepting the raw scan payload as the request body — one curl-able call, no separate file-upload step.
- Optional `Idempotency-Key` header: a repeated key returns the original import-job result instead of re-importing (protects against CI retry storms).
- Async by default (job id + status URL, per 3.1.4) with `?wait=true` synchronous mode for small payloads; response includes the counts CI gating needs (new findings by severity).
- Size limits, content-type validation, and streaming into the importer (no full buffering of 500 MB scan files).

### 3.5.3 Official CI recipes (GitHub Actions, GitLab CI, Azure Pipelines)

- `docs/ci/` with copy-pasteable, pinned-version recipes per platform: run scanner → upload to NetRisk → gate.
- Each recipe shows secret handling the platform-native way (GitHub encrypted secrets, GitLab masked variables, Azure variable groups).
- Stretch: a published composite GitHub Action (`netrisk/import-action`) wrapping the upload + gate logic.
- Recipes are integration-tested against a live instance in the repo's own CI so they can't rot silently.

### 3.5.4 Exit-code gating

- The upload response — plus a small cross-platform CLI subcommand (`netrisk-console ci gate --job <id> --fail-on critical:new`) — evaluates a policy expression: `fail-on: new-critical`, `fail-on: any-high>5`, `fail-on: sla-breach`.
- Exits non-zero on violation with a human-readable summary table on stdout. Policy syntax documented.
- "New vs. pre-existing" determination rides on the dedup engine (3.3) — that is what makes gating non-flaky.

**Sources (3.5):** [OWASP — CI/CD Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/CI_CD_Security_Cheat_Sheet.html) · [Wiz — CI/CD security scanning](https://www.wiz.io/academy/application-security/ci-cd-security-scanning) · [Aembit — Pipeline secrets checklist](https://aembit.io/blog/ci-cd-security-checklist-eliminate-secrets-workload-identity/)

---

## Dependencies & sequencing

- **3.1 (importer contract) → 3.3 (dedup) → 3.5.4 (gating)** is a hard chain.
- **3.2** can proceed in parallel with 3.1 but must land before 3.3.2's sticky-triage behavior is meaningful.
- **3.4.3** notification channels beyond email depend on Track 4.1.
- **3.5.1** entity binding depends on Track 2.3.
