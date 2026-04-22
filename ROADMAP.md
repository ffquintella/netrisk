# NetRisk Roadmap

This document tracks the planned direction for NetRisk. It is a living document — items may shift between milestones as priorities evolve. For shipped changes, see [CHANGELOG.md](CHANGELOG.md).

## Guiding Principles

- Free and open source risk management for small/medium organizations
- Secure by default, elegant to use
- Cross-platform: Windows, Linux, macOS
- Modular architecture (API, GUI client, background jobs, plugins)

## Short Term (next release)

- [ ] Standalone installers for Linux and macOS
- [ ] Continued UI responsiveness improvements across GUIClient views
- [ ] Expand automated test coverage (API.Tests, ClientServices.Tests, ServerServices.Tests)
- [ ] Documentation improvements under `docs/`

## Medium Term

- [ ] Plugin marketplace / discovery improvements (netrisk-plugin-sdk)
- [ ] Enhanced reporting (customizable templates, scheduled exports)
- [ ] Improved assessments workflow
- [ ] Better multi-tenant / multi-entity support
- [ ] Richer incident response plan automation

## Long Term

- [ ] Web-based client as a first-class alternative to the Avalonia GUI
- [ ] Integrations with external vulnerability scanners beyond Nessus
- [ ] SSO / enterprise auth providers (SAML, OIDC) hardening
- [ ] Compliance mapping (ISO 27001, NIST CSF, etc.)

## Vulnerability Management Gap Closure

Benchmarked against DefectDojo, NetRisk's vulnerability-management surface is thinner than its risk/incident surface. This initiative brings AppSec/ASPM-style capabilities into NetRisk while keeping its GRC-first identity. Tracked as a cross-release program.

### Phase 1 — Scanner Coverage (short term)

Goal: expand beyond the current Nessus-only importer so users can feed findings from the scanners they already run.

- [ ] Define a generic `IVulnerabilityImporter` plugin contract in `netrisk-plugin-sdk` (input: report file or stream; output: normalized `Vulnerability` + `Host` + `CVEDetail` records)
- [ ] Refactor the existing Nessus importer onto that contract (no behavior change)
- [ ] Add importers for: OWASP ZAP, Trivy, Semgrep, OpenVAS, Burp Suite
- [ ] Stretch: Snyk, Checkmarx, SonarQube, GitHub Dependabot, Grype
- [ ] API: generalize `POST /vulnerabilities/import/nessus/{fileId}` to `POST /vulnerabilities/import/{importerName}/{fileId}` with importer discovery via `IPluginsService`
- [ ] GUIClient: importer picker in the vulnerability import dialog

### Phase 2 — Finding Lifecycle (short/medium term)

Goal: richer per-vulnerability state machine so triage reflects reality.

- [ ] Add statuses: `Active`, `Verified`, `FalsePositive`, `OutOfScope`, `Duplicate`, `RiskAccepted`, `Mitigated` (extend `Model/Status` + DAL migration)
- [ ] Status transitions with audit trail (who/when/why) — reuse existing comments/audit infrastructure
- [ ] Dedicated `RiskAcceptance` entity with expiry date, accepting user, justification — separate from current `Closure` (distinct concept)
- [ ] Background job to auto-reopen expired risk acceptances (Hangfire)
- [ ] Filter/query support via existing Sieve pipeline on `GET /vulnerabilities/Filtered`

### Phase 3 — Deduplication Engine (medium term)

Goal: configurable dedup so repeated scans don't explode the finding count.

- [ ] Extend current hash-based `Find` lookup with pluggable dedup strategies: `HashBased`, `UniqueIdFromTool`, `LegacyHashCode`, `Custom`
- [ ] Per-importer default strategy declared in the plugin contract
- [ ] On import: match against existing finding → update vs. create new; mark `Duplicate` when appropriate
- [ ] Admin UI to configure dedup algorithm per scanner type
- [ ] Ensure `AssociateRisks` links flow through to the canonical record, not duplicates

### Phase 4 — SLA & Aging (medium term)

Goal: time-based accountability per finding.

- [ ] `SlaConfiguration` entity: target days per severity (Critical/High/Medium/Low/Info), plus per-product overrides
- [ ] Compute `SlaDueDate` / `DaysOverdue` on findings (view-model or computed column)
- [ ] Breach notifications via `EmailService` + new notification abstractions (Phase 6)
- [ ] Statistics rollups in `IStatisticsService` (findings past SLA by severity/entity)
- [ ] Reports: "SLA Breach" report template

### Phase 5 — CI/CD-First API (medium term)

Goal: make pipeline integration a supported first-class flow, not an afterthought.

- [ ] API-token auth path optimized for CI (scoped, non-interactive)
- [ ] Idempotent bulk import endpoint: `POST /vulnerabilities/import/{importer}` accepting the raw scanner payload in one call (no pre-upload)
- [ ] Documented GitHub Actions / GitLab CI / Azure Pipelines recipes under `docs/product-guides/`
- [ ] Exit-code gates (fail pipeline on new Critical findings above a threshold) — documented client-side pattern using existing endpoints

### Phase 6 — Integrations & Notifications (medium/long term)

Goal: close the loop into the tools teams already use.

- [ ] Notification abstraction `INotificationChannel` with implementations for Email (existing), Slack, Microsoft Teams, generic Webhook
- [ ] Event hooks: new Critical finding, SLA breach, risk-acceptance expiry, incident opened
- [ ] Jira integration plugin: create/link issue from a vulnerability, sync status back
- [ ] GitHub / GitLab / Azure DevOps issue integrations

### Phase 7 — Metrics & Dashboards (long term)

Goal: executive- and product-level visibility comparable to DefectDojo's dashboards.

- [ ] Dashboard view in GUIClient and WebSite: open findings by severity/entity, SLA compliance, MTTR trend, scanner coverage
- [ ] Export dashboard snapshots via the existing Reports feature
- [ ] Per-entity and per-host drilldowns

### Out of Scope (intentionally)

- Becoming a scanner ourselves — NetRisk remains an aggregator
- Replacing DefectDojo for pure AppSec shops — NetRisk keeps its GRC identity (risks, incidents, IRPs, assessments) as first-class

## Ideas / Under Consideration

Items here are not committed. They are candidates awaiting discussion or user feedback.

- Mobile companion app
- Real-time collaboration on risk records
- AI-assisted risk scoring and triage

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Feature requests and discussion are welcome via GitHub issues.
