---
ptf: 1
project: netrisk
---

# Roadmap

NetRisk is a cross-platform risk/vulnerability/incident management application. This roadmap tracks
strategic direction as **modular Milestone Tracks**: each track is a major capability area, scheduled,
developed and released independently or in mixed-milestone batches. For shipped changes, see
[CHANGELOG.md](CHANGELOG.md).

Tracks 1–8 are delivered as of 2.17.0 (37 milestones, 137 line items) and are kept below as the record
of what was built and why — where delivered behaviour differs from the original specification, the
task's `note:` says so rather than simply claiming the box. Track 9 is planned and does not reopen any
of them.

Two categories of work deliberately stay outside milestone tracking:

- **Accepted security risks and informational findings** — three of the Track 7 audit's 34 findings are
  recorded decisions rather than outstanding work (a plugin running with the API's authority,
  `AllowedHosts: *`, and the absence of a CORS policy). They live in
  [docs/security/FINDINGS.md](docs/security/FINDINGS.md) (S19) and are re-examined each minor release
  via [docs/security/BURN_DOWN.md](docs/security/BURN_DOWN.md) (S24).
- **Artifacts that cannot be produced on a single build host** — the Windows and Linux installer
  formats and the signing/notarisation paths need their own platforms and real certificates; see M21–M22
  and [docs/packaging/release-engineering.md](docs/packaging/release-engineering.md) (S10).

## Guiding Principles

- **FOSS Risk Management:** Professional-grade GRC tools accessible to small/medium organizations.
- **Secure by Default:** Deep-tier segregation, role-based access, enterprise security standards.
- **Cross-Platform:** Full feature parity on Windows, Linux, and macOS.
- **Modular Architecture:** Segregated API, Avalonia GUI, background jobs, and a pluggable system core.

## Track 1 — Modern Desktop Experience (UI/UX Compliance)

Performance tuning, visual standardization, and desktop ergonomics. `./build.sh LintUi` reports 0
violations across 80 views, gated in CI ([.github/workflows/ui-compliance.yml](.github/workflows/ui-compliance.yml)).
The linter itself was rewritten mid-track to scan whole start tags instead of matching line by line —
the previous line-based version both over-reported (11 of 58 R6 hits were false positives on views
already compliant) and under-reported (R5 had been reporting 0 while 45 genuine unlocalized strings,
including 20 window titles showing a class name, were present). True baseline was 106 violations, not
the previously logged 162 (a doubled log count). 102 were fixed and 4 waived with a written reason via
`<!-- ui-lint-waive R5: ... -->`, which the linter validates.

### [M1] Visual Theme Standardization
> outcome: All 67 views of the desktop client comply with the visual token, localization, button and
> responsive-sizing standard, enforced by CI lint.
> version: 2.15.0
> spec: S1

- [x] T1 Replace inline hex colors and named brushes with semantic style classes
- [x] T2 Extract user-facing strings and window titles into localized resx bindings
- [x] T3 Re-class buttons onto the canonical taxonomy with icon+text stacks
- [x] T4 Convert fixed-width form layouts to responsive Grid/SpacedGrid sizing
- [x] T5 Add automated lint checks rejecting inline colors or unclassed buttons
- [x] T6 Close the full docs/ui-standard.md compliance sweep (UI-STD-001) (S3, S4, S5)
  - note: 4 dangling style-class references found (`Panel.EditTitle`, `TextBlock.subHeader`) and fixed by adopting documented classes; guarded by GUIClient.Tests/Views/StyleClassReferenceTest

### [M2] Shell Backdrop & Material Stabilization
> outcome: Glassmorphic window compositions with clean solid-color fallbacks across host window managers.
> version: 2.15.0

- [x] T7 Wrap MainWindow content in a layout-compliant acrylic/Mica panel
- [x] T8 Apply native Windows 11 Mica backdrops conditionally per platform
- [x] T9 Apply native macOS Vibrancy on sidebar and navigation panels
- [x] T10 Fall back to a solid high-contrast background when compositing is unsupported
- [x] T11 Enforce global minimum window sizing constraints
- [x] T12 Remove the unreferenced scratch view teste.axaml

### [M3] Compiled Bindings & Rendering Optimization
> outcome: Compile-time binding safety and virtualization across the desktop client, at extreme
> rendering speed and minimal RAM footprint.
> version: 2.15.0

- [x] T13 Declare explicit x:DataType bindings across all 85+ views
- [x] T14 Resolve compile-time binding errors on reflection-based view-models
- [x] T15 Enable compiled bindings globally in netrisk.sln
- [x] T16 Enforce UI virtualization and adopt TreeDataGrid for dense grids
- [x] T17 Promote bound view-model members to public for compiled-binding visibility
  - note: fixed post-migration regressions in UserInfo, AdminWindow/UsersView and 24 other views; shipped in 2.5.1

### [M4] Platform-Native Ergonomics & Accessibility
> outcome: The app feels like a native local utility, optimized for keyboard and mouse precision.
> version: 2.15.0

- [x] T18 Mirror the window menu into the macOS native global menu bar
- [x] T19 Align window controls and nav bar margin with macOS traffic lights
- [x] T20 Sweep keyboard accessibility: shortcuts, tab order, default/cancel buttons
- [x] T21 Add tray / menu-bar-extra integration with a quick status preview

### [M5] Interaction & Workflow Standardization
> outcome: One dialog stack, one feedback language, state-driven workflows across the desktop client.
> version: 2.15.0
> spec: S2

- [x] T22 Phase A — fix defects and restore the GUI's no-op validation layer
  - note: ReactiveUI.Validation had been dropped and stubbed since Feb 2026 (commit 4c4abaa5); replaced with in-tree ValidationContext
- [x] T23 Phase B — migrate legacy edit windows onto DialogWindowBase/DialogService
- [x] T24 Phase C — add toast notifications, inline validation and busy overlays
- [x] T25 Phase D — converge risk/incident/IRP/device/entity workflows
- [x] T26 Phase E — centralize navigation, window parenting and geometry persistence

## Track 2 — GRC Core & Reporting Engine

GRC core features, incident workflows, and data output templates. Detailed specifications:
[docs/roadmap/TRACK_2_GRC_REPORTING.md](docs/roadmap/TRACK_2_GRC_REPORTING.md) (S6).

### [M6] Advanced Reporting Engine
> outcome: Rich, customizable risk reports and automated exports.
> spec: S6
> version: 2.9.0

- [x] T27 Add customizable report templates with branding, sections and live preview
- [x] T28 Support scheduled dashboard/compliance/incident exports via email
- [x] T29 Add PDF, CSV and Excel export targets for all statistics tables
- [x] T30 Build the report-template designer and scheduled-export GUI screens

### [M7] Enhanced Assessments Workflow
> outcome: Organizations collect, triage and score vulnerability/compliance questionnaires efficiently.
> spec: S6
> version: 2.10.0

- [x] T31 Build a paged assessment viewer with conditional show/hide logic
- [x] T32 Implement progress trackers and draft auto-saving
- [x] T33 Support importing assessment templates from industry standards (NIST, ISO 27001)
- [x] T34 Build the assessment-run viewer, progress tracker and template-import GUI

### [M8] Multi-Entity & Multi-Tenant Support
> outcome: Managed risk monitoring across distinct organizational subdivisions, enforced server-side.
> spec: S6
> version: 2.16.0

- [x] T35 Enforce entity segregation via EF global query filters and a SaveChanges guard
  - note: initial scoping shipped 2.11.0; `ApplyEntityScope` had been called from one query only and filtered nothing, corrected in 2.16.0 to cover 5 entity_id-bearing types plus 9 inherited types
- [x] T36 Add role-based scoped access with an Entity Access admin screen
  - note: deny-by-default for an unassigned authenticated user; 21 negative cross-entity tests plus MariaDB integration tests
- [x] T37 Add a Master Dashboard service aggregating posture metrics across entities
  - note: neither the endpoint nor the service existed despite being previously documented as done; both were built in 2.16.0 (`GET /Dashboard/Master`, admin-only)
- [x] T38 Build the admin-only Master Dashboard GUI view

### [M9] Incident Response Automation (IRP)
> outcome: Close the loop on incident management with active, trackable workflows.
> spec: S6
> version: 2.16.0

- [x] T39 Add customizable IRP templates with task CRUD and clone
  - note: initial templates shipped 2.12.0
- [x] T40 Support automatic task generation/assignment via authored matching rules
- [x] T41 Build server-side CPM scheduling with a Gantt critical-path view
  - note: dependencies now persisted as incident_response_plan_task_dependencies edges and validated acyclic on save (db_version 76); automation/Gantt UI landed 2.16.0
- [x] T42 Build the IRP template editor, automation-rule and Gantt GUI screens

## Track 3 — Vulnerability Aggregation & Finding Lifecycle (ASPM)

Bridges GRC with Application Security Posture Management: ingest, deduplicate, and triage automated
scanner outputs. Detailed specifications:
[docs/roadmap/TRACK_3_ASPM.md](docs/roadmap/TRACK_3_ASPM.md) (S7).

### [M10] Extensible Scanner Importers
> outcome: A unified plugin interface feeds findings from any security tool.
> spec: S7
> version: 2.16.0

- [x] T43 Define the IVulnerabilityReportImporter plugin contract in the SDK
- [x] T44 Refactor the legacy Nessus parser onto the extensible importer contract
- [x] T45 Ship native importers for ZAP, Trivy, Semgrep, OpenVAS, Burp, Snyk, Grype, Dependabot and SARIF 2.1 (S29)
- [x] T46 Generalize the vulnerability import API with dynamic importer discovery
- [x] T47 Build the dynamic importer selector GUI with live job progress

### [M11] Finding Lifecycle & Audit Trails
> outcome: A rigorous triage state machine for individual findings, with a tamper-evident history.
> spec: S7
> version: 2.16.0

- [x] T48 Add granular finding lifecycle states with an enforced transition matrix
- [x] T49 Add an append-only finding_status_history audit log with a timeline view
- [x] T50 Add a RiskAcceptance entity with expiry, authorizer and justification
  - note: generalized further by Track 8.1 into one entity shared by risks and findings
- [x] T51 Add a Hangfire job to auto-reopen expired risk acceptances

### [M12] Intelligent Deduplication Engine
> outcome: No database bloat from repeated automated scans, via pluggable matching strategies.
> spec: S7
> version: 2.16.0

- [x] T52 Add pluggable dedup strategies (HashBased/UniqueIdFromTool/LegacyHashCode/Custom) chained per scanner
- [x] T53 Update existing findings on re-import instead of duplicating, keeping scan logs
- [x] T54 Build an admin UI to configure dedup heuristics with a merge preview

### [M13] SLA Tracking & Aging
> outcome: Compliance boundaries enforced with automated SLAs per severity.
> spec: S7
> version: 2.16.0

- [x] T55 Add effective-dated SlaConfiguration per severity, seeded to CISA benchmarks
- [x] T56 Compute SlaDueDate and DaysOverdue on open findings at read time
- [x] T57 Automate SLA breach email/webhook notifications as a deduplicated digest

### [M14] CI/CD-First Integration API
> outcome: NetRisk integrates directly into automated build pipelines.
> spec: S7
> version: 2.16.0

- [x] T58 Add scoped, revocable API-token authentication for CI runners
- [x] T59 Support bulk idempotent direct-upload import endpoints
- [x] T60 Publish official GitHub Actions/GitLab CI/Azure Pipelines recipes (docs/ci/)
- [x] T61 Support exit-code gating via `netrisk-console ci gate`

## Track 4 — Integrations & Notification Channels

Connects NetRisk with external messaging platforms, issue trackers, and enterprise identity systems.
Detailed specifications: [docs/roadmap/TRACK_4_INTEGRATIONS.md](docs/roadmap/TRACK_4_INTEGRATIONS.md)
(S8). Schema arrives as `db_version` 79 (phase 10) for M15–M19, and `db_version` 83 (phase 14) for M20.

### [M15] Unified Notification Channels
> outcome: Alerts broadcast to platforms where security and engineering teams already communicate.
> spec: S8
> version: 2.17.0

- [x] T62 Define the extensible INotificationChannel provider interface
- [x] T63 Ship native channel providers: Email, Slack, Teams and generic webhooks (S30)
- [x] T64 Add event-triggered notification subscriptions with a delivery log
  - note: deviation — the queue is a database table swept every minute by Hangfire rather than per-message jobs, so retry/digest state stays inspectable

### [M16] Bi-directional Issue Sync
> outcome: Security triage aligned with development workflows.
> spec: S8
> version: 2.17.0

- [x] T65 Build the modular IIssueTrackerProvider integration core (S31)
- [x] T66 Support creating/linking developer tasks in Jira, GitHub, GitLab and Azure DevOps
- [x] T67 Implement bi-directional sync via validated webhooks and a status-mapping table

### [M17] Hardened Enterprise Authentication
> outcome: Standard enterprise SSO secures access, with SCIM provisioning and hardware second factors.
> spec: S8
> version: 2.17.0

- [x] T68 Support SAML 2.0 and OIDC SSO with per-IdP claim/group mapping (S32)
  - note: deviations — OIDC is an explicit PKCE flow rather than cookie middleware (desktop redirects to a loopback URI); SAML is over SignedXml rather than Sustainsys; encrypted assertions and single logout are not implemented
- [x] T69 Implement SCIM 2.0 user/group provisioning with RFC 7644 PATCH semantics
  - note: a SCIM group maps onto a NetRisk role; `active:false` revokes live sessions on the next request
- [x] T70 Support WebAuthn/FIDO2 hardware authentication for admin accounts
  - note: registers as an independent second factor alongside FaceID, not merged into one MFA registry

### [M18] Trend Micro Vision One Integration
> outcome: Asset, risk, vulnerability and posture synchronization with Trend Micro Vision One.
> spec: S8
> version: 2.17.0

- [x] T71 Add region-aware connection management with a test-connection probe
- [x] T72 Automate daily computer-inventory synchronization onto hosts
  - note: dedup is an asset-identity chain (external id → MAC → FQDN → hostname → IP), a separate mechanism from the Track 3.3 finding dedup engine
- [x] T73 Ingest CVE vulnerabilities per device with virtual-patch detection
- [x] T74 Synchronize risk scores into the entity-wide Cyber Risk Index

### [M19] SecurityScorecard Integration
> outcome: Domain-level cyber rating, factor scores, and issue synchronization with SecurityScorecard.
> spec: S8
> version: 2.17.0

- [x] T75 Add domain-targeted connection management with a token test
- [x] T76 Automate posture synchronization of grade, score and factor history
- [x] T77 Ingest domain-level CVE vulnerabilities under a synthetic domain host
- [x] T78 Ingest active security issues under custom categories

### [M20] Jira Service Management & Assets
> outcome: Read the service desk and import the CMDB registers describing applications and machines.
> spec: S8
> version: 2.19.0

- [x] T79 Extend the Jira connection with a Service Management/Assets facet (S34)
  - note: Assets runs on Cloud and Data Center (T220); Service Management stays Cloud-only and enabling it on a Data Center connection is refused at save
- [x] T80 Mirror service-desk requests and SLA cycles with breach notifications
- [x] T81 Generalize finding_issue_links to incidents and risks via a target_kind discriminator
- [x] T82 Import Jira Assets registers for applications, servers and machines
- [x] T83 Make the status-mapping, template and severity-priority screens editable
- [x] T84 Add a live template preview against a real finding before saving
- [x] T85 Link an imported Assets object back to its Jira page by object key
- [x] T220 Import Jira Assets from Jira Data Center connections through the instance's own Assets REST API (S34)
  - note: tries /rest/assets/1.0 then /rest/insight/1.0 on a 404; verified against Atlassian's published reference and fixtures only, not a live Data Center instance
- [x] T221 Surface the server's actual refusal on a failed integration read instead of a generic "Error calling {route}", and stop the Assets schema/object-type refresh from firing while "Enable Assets" is unchecked
- [x] T222 Tell the operator to pick an Assets schema when the object-type button is clicked before one is selected, instead of returning with no feedback

## Track 5 — Native Packaging & Release Engineering

Automates artifact production for secure, seamless software distribution. Detailed specifications:
[docs/roadmap/TRACK_5_PACKAGING.md](docs/roadmap/TRACK_5_PACKAGING.md) (S9). Packaging logic, manifests
and signing pipelines are unit-tested, but no signed, notarized or Windows/Linux-native artifact has
been produced yet in this environment — that needs real certificates, an Apple Developer account and
Windows/Linux runners. Operational guide: [docs/packaging/release-engineering.md](docs/packaging/release-engineering.md) (S10).

### [M21] Automated Code-Signing Pipelines
> outcome: OS-level safety warnings are eliminated and publisher trust is verified.
> spec: S9, S10
> version: 2.17.0

- [x] T86 Wire Windows Authenticode signing (Azure Trusted Signing, signtool fallback)
  - note: never executed end to end in this environment — needs a Windows host and a real certificate; decision logic covered by src/Packaging.Tests
- [x] T87 Wire macOS Developer ID signing and notarization for app/pkg/dmg
  - note: never executed end to end — no Developer ID certificate or Apple account available here

### [M22] Modern Native Installers
> outcome: Streamlined, native installation packages matching platform standards.
> spec: S9, S10
> version: 2.17.0

- [x] T88 Author the Windows MSI (WiX v5) and MSIX packaging targets
  - note: WiX/makeappx are Windows-only; no .msi/.msix produced here, verified statically in src/Packaging.Tests
- [x] T89 Assemble a drag-and-drop macOS DMG with background and volume icon
  - note: executed on Apple Silicon; produced a mountable DMG with the expected unsigned-build warnings
- [x] T90 Author the Linux Flatpak and Snap packaging targets with least-privilege sandboxing
  - note: flatpak-builder/snapcraft are Linux-only; no .flatpak/.snap produced here, rendered recipes asserted in src/Packaging.Tests

## Track 6 — Database Uniformization & Schema Health

Standardizes the database schema (naming, relationships, indexing, types) and removes dead
tables/columns with zero data loss. Full plan:
[docs/plano-uniformizacao-banco.md](docs/plano-uniformizacao-banco.md) (S11).

### [M23] Upgrade Tooling & Preparation (Plan: Tool + Phase 0)
> outcome: A safety net exists before touching the schema.
> spec: S12
> version: 2.8.0

- [x] T91 Add `netrisk-console database upgrade-schema` with pre-flight/backup/validation/audit
- [x] T92 Add `netrisk-console database baseline` census and model-divergence report
- [x] T93 Document the Track 6 naming convention in CLAUDE.md

### [M24] Safe Fixes & Naming Uniformization (Plan: Phases 1–2)
> outcome: Low-risk corrections and snake_case convergence — renames only, no drops.
> spec: S13
> version: 2.8.0

- [x] T94 Fix invalid `0000-00-00` defaults and index-name typos (phase 1, db_version 64)
- [x] T95 Normalize boolean columns from tinyint(4) to tinyint(1) (phase 1b, db_version 66)
- [x] T96 Snake-case the last stray column, `comments.IsAnonymous` (phase 2b, db_version 67)
- [x] T97 Convert all 99 base tables to utf8mb4/utf8mb4_unicode_ci (phase 1c, db_version 68)
- [x] T98 Rename 8 PascalCase tables and hybrid camelCase columns to snake_case (phase 2, db_version 65)

### [M25] Relationships & Indexing for Performance (Plan: Phases 3–4)
> outcome: Every correlation column becomes a real, navigable, indexed foreign key.
> spec: S14
> version: 2.8.0

- [x] T99 Add FK constraints and EF navigations for orphan id columns (phase 3, db_version 69)
- [x] T100 Add query-justified hot-path indexes and convert BLOB-for-text columns (phase 4, db_version 70)

### [M26] Type Standardization & Dead Schema Removal (Plan: Phases 5–6)
> outcome: Consistent temporal/status types, then staged removal of unused objects.
> spec: S15
> version: 2.8.0

- [x] T101 Migrate `risks.status` to an int-backed enum via create-copy-coexist (phase 5, db_version 71)
- [x] T102 Deprecate 23 unreferenced tables and orphan columns, reversibly (phase 6a, db_version 72)
- [x] T103 Drop the deprecated tables and columns after the observation window (phase 6b, db_version 73)

## Track 7 — Security Review & Hardening

A full, end-to-end security review across every tier, producing a prioritized findings register and a
remediation backlog. **Status: complete (2026-08-26).** 34 findings raised, 25 fixed with regression
tests, 5 open with a named owner and proposed fix, 4 risk-accepted with an expiry. No critical or high
finding outstanding. Six of the fixes were wrong on the first attempt and were corrected before landing;
they are listed in [FINDINGS.md](docs/security/FINDINGS.md) §"Regressions introduced by this track's own
fixes" rather than quietly corrected. Detailed specifications:
[docs/roadmap/TRACK_7_SECURITY.md](docs/roadmap/TRACK_7_SECURITY.md) (S16).

### [M27] Comprehensive Security Audit
> outcome: A baseline is established by systematically reviewing the code against a recognized standard.
> spec: S16
> version: 2.17.0

- [x] T104 Threat-model the request flow and document trust boundaries (S17)
- [x] T105 Audit the codebase against OWASP ASVS/Top 10 chapter by chapter (S18)
  - note: fuzzing the Nessus XML parser was not done; the three XXE payload classes are instead asserted against each importer in ImporterXxeTest
- [x] T106 Produce a prioritized findings register naming how each finding was established (S19)
- [x] T107 Run `/security-review` as a recurring gate and capture the baseline report (S20)

### [M28] Dependency & Supply-Chain Security
> outcome: What ships in the binaries and submodules is known and controlled.
> spec: S16
> version: 2.17.0

- [x] T108 Enable Dependabot and a CI dependency-vulnerability scan gate
- [x] T109 Generate and publish a CycloneDX SBOM from Nuke `Package*` targets
  - note: publishing to a Dependency-Track instance is the optional half of the spec and is not done
- [x] T110 Pin and document submodule provenance with a reviewed-bump CI gate (S21)

### [M29] AuthN/AuthZ & Secrets Hardening
> outcome: Gaps in identity, access control, and secret management are closed.
> spec: S16
> version: 2.17.0

- [x] T111 Verify every API controller enforces authorization, by reflection not by comment
  - note: found `WebAuthnController` shipping with no `[Authorize]` at all (NR-2026-009)
- [x] T112 Harden token issuance, session lifetime, lockout and FaceID liveness (NR-2026-001/002/008/010/012)
- [x] T113 Standardize secret storage and document a per-secret rotation procedure (S22)
  - note: found committed expired dev certificates with password "pass" (NR-2026-003); history not rewritten, a Release build now refuses to start with them

### [M30] Data Protection & Transport Security
> outcome: Data is protected in transit and at rest.
> spec: S16
> version: 2.17.0

- [x] T114 Enforce TLS 1.2+ and certificate validation on all client/server and outbound calls (NR-2026-004/005/013/026)
- [x] T115 Encrypt sensitive columns at rest and validate hashing/KDF choices (S23)
  - note: uploaded files and the finding register are not column-encrypted (TM-A4, accepted); biometric templates were a genuine gap, raised as NR-2026-032
- [x] T116 Harden CORS, security headers and cookie flags, verified by a live scan (NR-2026-015/016)

### [M31] Continuous Security in CI/CD
> outcome: Security verification is automatic and non-regressing.
> spec: S16
> version: 2.17.0

- [x] T117 Add CodeQL and gitleaks SAST/secret-scanning gates to CI, failing on new findings
- [x] T118 Publish SECURITY.md and an internal triage SLA (S25)
- [x] T119 Schedule periodic re-audits and track remediation burn-down (S24)
  - note: the release-checklist item is documented, not mechanically enforced in the Nuke release flow

## Track 8 — Risk Governance & Approval Workflows

Closes the gap between NetRisk's risk lifecycle and what ISO 27001 / SOC 2 / DORA auditors and NIST
RMF / COSO ERM test: formal expiring risk acceptance, residual-vs-inherent risk, segregated multi-level
approvals, a field-level audit trail, proactive review notifications, and a business-facing review
portal. **Status: delivered.** All 23 items are implemented; three carry a deliberate difference from
spec, annotated on the item itself. Detailed specifications:
[docs/roadmap/TRACK_8_RISK_GOVERNANCE.md](docs/roadmap/TRACK_8_RISK_GOVERNANCE.md) (S26). The Avalonia
desktop changes are compile- and lint-verified only in this environment; `src/RiskPortal` was run and
exercised end to end against a real MariaDB and API.

### [M32] Formal Risk Acceptance & Time-Bound Exceptions
> outcome: Accepting a risk is a first-class, expiring, authorized artifact (ISO 27001 6.1.3 evidence).
> spec: S26
> version: 2.17.0

- [x] T120 Add the risk_acceptances entity with expiry and revoke/renew lifecycle (db_version 80)
- [x] T121 Add the acceptance service/API with severity-band authority checks
- [x] T122 Automate T-30/T-7 expiry warnings and auto-reopen on lapse
  - note: the first implementation took the first matching threshold and never fired T-7; fixed to take the tightest applicable one
- [x] T123 Build the risk-acceptance GUI panel on the risk editor
  - note: deviation — shipped as a tabbed dialog (`RiskGovernanceWindow`) rather than an inline panel; compile/lint-verified only

### [M33] Inherent vs. Residual Risk
> outcome: Pre- and post-treatment scores are tracked as the routing key for escalation.
> spec: S26
> version: 2.17.0

- [x] T124 Add residual scoring with a swappable mitigation-effectiveness strategy
- [x] T125 Re-create `next_review_date_uses` to select inherent vs residual cadence
  - note: correction to spec — the setting had been deleted in db_version 29, not merely unused for fifty versions; re-created in db_version 80
- [x] T126 Show both scores with delta on lists/editors and an inherent/residual heatmap toggle
  - note: the toggle relabels which score is filtered; it does not move the plotted point, since a residual score has no likelihood/impact decomposition

### [M34] Approval Workflow Engine (State Machine, Segregation of Duties, Escalation, Appetite)
> outcome: Server-side transitions, maker-checker, threshold-escalated dual sign-off, and risk appetite.
> spec: S26
> version: 2.17.0

- [x] T127 Enforce a server-side risk status state machine, refusing with 422
  - note: `ReopenRisk` stays on the unguarded save deliberately; legacy violations are reported, not blocked
- [x] T128 Enforce segregation of duties (reviewer/acceptor ≠ submitter/owner/manager) with an audited break-glass override
- [x] T129 Add risk_appetites with a dual-approval threshold and counter-signature flow
  - note: no appetite row is seeded; gating stays inactive until an organisation configures its own threshold
- [x] T130 Build the appetite admin screen and counter-sign GUI action

### [M35] Field-Level Audit Trail & Auditor Evidence Export
> outcome: "Who changed what, when" is answerable from the database with an exportable evidence pack.
> spec: S26
> version: 2.17.0

- [x] T131 Add a SaveChanges-interceptor field-level audit log over the governance aggregate
  - note: deliberately not global — a trail over vulnerability imports would write millions of unread rows; retention defaults to 1,825 days
- [x] T132 Build the auditor evidence export (CSV + PDF) via the reporting engine

### [M36] Review Cadence Automation & Intake Repair
> outcome: Push, don't pull — overdue reviews and expiring acceptances notify; POA&M-style tasks exist.
> spec: S26
> version: 2.17.0

- [x] T133 Add a daily review-cadence notification job over the existing ReviewLevel cadence
- [x] T134 Add pending-risk promote/dismiss via API and GUI
- [x] T135 Add mitigation_tasks line-items (owner, due date, status) feeding the same notifications

### [M37] Business Risk Acceptance Portal (Web Application)
> outcome: Business-appointed reviewers periodically review, rank and decide their entity's risks.
> spec: S26
> version: 2.17.0

- [x] T136 Stand up the src/RiskPortal ASP.NET Core web app consuming the REST API
  - note: actually run and exercised locally against a real MariaDB and API, where four of this milestone's defects were found
- [x] T137 Add entity-scoped reviewer designation and appointment
  - note: appointing a reviewer did not grant access until `AppointAsync` was fixed to create the entity-role row
- [x] T138 Auto-generate periodic per-entity review campaigns
- [x] T139 Build the reviewer drag-to-rank and accept/mitigate/escalate decision flow
- [x] T140 Surface business rank and campaign evidence in desktop reports

### [M38] Quantitative Scoring Option (FAIR-lite) & Scale Anchors
> outcome: Anchored ordinal scales now, a quantitative alternative for the documented limits of matrices.
> spec: S26
> version: 2.17.0

- [x] T141 Add quantitative definitions/anchors on every likelihood/impact level
- [x] T142 Add the FAIR-lite Monte Carlo scoring method with ALE percentiles and loss-exceedance curve
  - note: the mapped score was taken from median ALE and scored a low-frequency high-impact risk as 0; fixed to map from the mean

## Track 9 — MIGR-TI/IA Methodology Alignment

Aligns NetRisk with the **MIGR-TI/IA** reference methodology, documented in
[docs/methodology/](docs/methodology/) and scoped by a phase-by-phase coverage analysis
([docs/methodology/migr-ti-ia-coverage.md](docs/methodology/migr-ti-ia-coverage.md), S28). Twelve stages,
the fifteen prioritized gaps from that analysis's §12, grouped by dependency. **Status: planned, nothing
started.** Does not reopen Track 8. Detailed specifications:
[docs/roadmap/TRACK_9_MIGR_TI_IA.md](docs/roadmap/TRACK_9_MIGR_TI_IA.md) (S27).

> **Two gates apply to every stage.** Gate 1 — a complete, reviewed specification (eleven required
> sections) merges under `docs/roadmap/track9/9.N-<slug>.md` before the first implementation commit.
> Gate 2 — no item is ticked without the tests its specification planned: happy path, every guard/error
> branch, a regression test that fails on the pre-fix code, schema idempotence/replay, negative
> authorization cases. See [src/AI_TESTING_INSTRUCTIONS.md](src/AI_TESTING_INSTRUCTIONS.md).

### [M39] Stage 9.1 — The linkage chain: objective → process → IT service → data → asset
> outcome: A risk traces to a strategic objective, not only to one generic entity. Closes gap 1.
> spec: S27

- [ ] T143 Merge the Stage 9.1 specification (eleven sections, test plan reviewed)
- [ ] T144 Model the strategic objective as a first-class entity
- [ ] T145 Add an itService type to the entity schema (technical owner, processes served)
- [ ] T146 Link risks to each optional, queryable link of the chain
- [ ] T147 Compute a critical-process coverage metric counting only processes marked critical
- [ ] T148 Test chain traversal with a missing middle link and legacy single-link coexistence

### [M40] Stage 9.2 — Structured scenario, record discrimination and evidence confidence
> outcome: Four scenario fields plus a confidence level make the Phase 2 quality rules machine-verifiable. Closes gap 2.
> spec: S27

- [ ] T149 Merge the Stage 9.2 specification
- [ ] T150 Split cause/threat, vulnerability/condition, central event and consequences into separate fields
- [ ] T151 Add an evidence confidence level (confirmed / indicative / hypothesis)
- [ ] T152 Support standalone hypothesis records, not only ones originating from an assessment answer
- [ ] T153 Distinguish a near miss from an incident
- [ ] T154 Add duplicate-risk detection on (central event, consequence) as a warning, not a block
  - note: deliberately out of scope — back-filling the four fields from existing free text; legacy risks keep them null
- [ ] T155 Test that a legacy risk with all four fields null stays editable, listable and scorable

### [M41] Stage 9.3 — BIA: MTPD/MAO, RTO, RPO and cascading dependencies
> outcome: Continuity fields that flag 4, Gate A and the restoration metric depend on. Closes gap 4.
> spec: S27

- [ ] T156 Merge the Stage 9.3 specification
- [ ] T157 Declare MTPD/MAO, RTO and RPO on the process and the IT service, plus process criticality
- [ ] T158 Model dependencies with cascading effect
- [ ] T159 Add restoration-test records comparable against the declared RTO/RPO
- [ ] T160 Test that a declared RTO with no restoration test reads as unverified, not met; cyclic dependencies don't recurse forever

### [M42] Stage 9.4 — Exploitation signals: CISA KEV, first-class EPSS and MITRE ATT&CK
> outcome: The Phase 3 prioritization signals reach NetRisk as first-class data, not only via Vision One. Closes gap 6.
> spec: S27

- [ ] T161 Merge the Stage 9.4 specification
- [ ] T162 Promote EPSS from a tool-fields bag to a column on vulnerabilities, with its own sync
- [ ] T163 Synchronize the CISA KEV catalogue with listing date and deadline
- [ ] T164 Associate MITRE ATT&CK techniques to the finding and the risk scenario
- [ ] T165 Combine the Phase 3 signals into a prioritization with CVSS as an input
- [ ] T166 Synchronize outbound through IOutboundHttpClient under the SSRF policy
  - note: deliberately out of scope — exposure, required privileges and blast radius (topology modelling); stays ❌ in the coverage analysis
- [ ] T167 Test that an unavailable/malformed KEV catalogue never silently de-lists a KEV item, and two EPSS sources converge by a declared rule

### [M43] Stage 9.5 — The eleven mandatory flags and Gate A
> outcome: Gate A, non-discretionary in the methodology, becomes implementable via queryable flags. Closes gap 3.
> spec: S27
> depends-on: M41, M42

- [ ] T168 Merge the Stage 9.5 specification, declaring the origin of each of the eleven flags
- [ ] T169 Model the eleven flags as queryable fields
- [ ] T170 Refuse to discard a risk carrying a non-discretionary flag, with notified escalation (Gate A)
- [ ] T171 Distinguish an "act immediately" decision from high severity
- [ ] T172 Build a "Top Risks" executive list carrying trend, confidence and next decision
- [ ] T173 Test that a derived flag reverts with an audit-trail entry, and that Gate A precedes Gate B

### [M44] Stage 9.6 — Treatment economics: monetary cost, Gates C and D, the full option set
> outcome: Gate C gets a calculation; Gate D gets portfolio selection under constraints. Closes gaps 7, 10, part of 13.
> spec: S27

- [ ] T174 Merge the Stage 9.6 specification
- [ ] T175 Add monetary control cost alongside the existing ordinal MitigationCost scale
- [ ] T176 Implement Gate C: E[L before] − E[L after] > total cost, citing Gordon–Loeb as a reference, not a fixed 37% rule
- [ ] T177 Implement Gate D: portfolio selection under budget, people, dependencies and deadline
- [ ] T178 Add avoid and transfer/share as treatment types alongside reduce and accept
- [ ] T179 Add completion evidence and an acceptance criterion on MitigationTask
- [ ] T180 Add a target risk level to the register
- [ ] T181 Test that a mitigation with no monetary cost enters Gate C as not-assessable, and Gate D preserves tail/systemic risks at moderate E[L]

### [M45] Stage 9.7 — Tail statistics and portfolio: P95, CVaR, aggregation and correlation
> outcome: The tail statistic appetite compares against, and the portfolio sum Phase 7 needs, exist. Closes gap 9.
> spec: S27

- [ ] T182 Merge the Stage 9.7 specification
- [ ] T183 Compute and store P95 and CVaR with confidence intervals
- [ ] T184 Decompose loss magnitude into response, recovery, productivity, revenue, liability, fine and reputation
- [ ] T185 Aggregate portfolio exposure with declared correlation between scenarios
- [ ] T186 Compare appetite against P95/CVaR (Gate B)
- [ ] T187 Test that CVaR of a low-frequency scenario is not zero, and a zero-correlation portfolio sum is not the sum of individual P95s

### [M46] Stage 9.8 — KRIs, mandatory reassessment triggers and the methodology's metrics
> outcome: A first-class KRI record and the six mandatory reassessment triggers of Phase 7 exist. Closes gap 5.
> spec: S27

- [ ] T188 Merge the Stage 9.8 specification, listing which metrics this stage delivers
- [ ] T189 Model KRI as a first-class record: definition, source, tolerance threshold, history
- [ ] T190 Implement the six mandatory reassessment triggers of Phase 7
- [ ] T191 Gate B by indicator, not only by score
- [ ] T192 Build a metrics panel for the methodology's own performance measures
- [ ] T193 Test that a KRI with no recent reading reads as stale, and a reassessment trigger is idempotent

### [M47] Stage 9.9 — Archival with triggers, backtesting, the risk committee and the third line
> outcome: The decision cycle closes: reopenable archive, backtesting, and the missing Phase 0 roles. Closes gaps 13 (part), 14, 15.
> spec: S27

- [ ] T194 Merge the Stage 9.9 specification
- [ ] T195 Add an "Archived" state with justification, a condition-based reopening trigger and quarterly review
- [ ] T196 Backtest incidents and near misses against the register
- [ ] T197 Add the risk committee as a collegiate approver alongside the individual authorizing manager
- [ ] T198 Add a third-line (audit) read-only assurance role
- [ ] T199 Test that a condition trigger fires once, backtesting never counts a post-incident registration as foreseen, and the third-line role cannot write

### [M48] Stage 9.10 — Third-party register: HECVAT, SBOM, concentration and exit plan
> outcome: The only discovery front with no instrument gets one. Closes gap 8.
> spec: S27

- [ ] T200 Merge the Stage 9.10 specification
- [ ] T201 Model third party as a first-class record, distinct from a generic organization
- [ ] T202 Add HECVAT assessment, SBOM, sub-processors, data location, SLA/RTO/RPO, right to audit, exit plan
- [ ] T203 Measure concentration by supplier, cloud and identity
- [ ] T204 Link the third-party record to the Stage 9.1 IT service and the Stage 9.11 data record
- [ ] T205 Test that concentration counts a supplier once per dependent critical process, and a partial HECVAT scores as incomplete

### [M49] Stage 9.11 — LGPD data catalogue: legal basis, purpose, retention, location and DPIA
> outcome: Compliance becomes demonstrable; flags 2 and 5 get something to derive from. Closes gap 11.
> spec: S27

- [ ] T206 Merge the Stage 9.11 specification
- [ ] T207 Add legal basis, purpose, retention, location, transfer and sensitivity marking on organizationData
- [ ] T208 Add DPIA as an artifact linked to the data record and the process
- [ ] T209 Link legal/contractual requirements on the risk register to the catalogue instead of free text
- [ ] T210 Test that sensitive data with no declared legal basis is a finding, and expired retention signals but does not auto-delete

### [M50] Stage 9.12 — AI governance: model inventory, flag 11 and model metrics
> outcome: The inventory and assurance the by-construction authority controls are missing. Closes gap 12.
> spec: S27

- [ ] T211 Merge the Stage 9.12 specification
- [ ] T212 Add a model inventory as a first-class record (purpose, data, vendor, version)
- [ ] T213 Put AI-component risks in the same register, with flag 11 derived from the inventory
- [ ] T214 Add model metrics: accuracy, recall, calibration, drift, human override rate
  - note: deliberately out of scope — adding AI to the risk workflow; the governance instrument must exist before the use
- [ ] T215 Test that the existing non-user-approval prohibitions still hold after this stage, and a model with no recorded evaluation is not treated as evaluated

**Track completion.** The track is done when the coverage analysis (S28) is re-run and the lines each
specification declared read ✅ — lines that stay 🟡 or ❌ are named with the reason.

## Backlog

- [ ] T216 Ship a Mobile Companion App: lightweight iOS/Android viewer for executive incident tracking and risk sign-off
- [ ] T217 Add Real-Time Collaboration: synchronized editing for IRPs and joint risk assessments
- [ ] T218 Add AI-Assisted Risk Scoring (LLM-based vulnerability analysis and mitigation proposals)
  - note: precondition — Stage 9.12 (AI governance) must land first; the methodology requires the governance instrument before the use
- [ ] T219 Move the BastionVault plugin onto BastionVault.IntegrationSdk, replacing the hand-written wire protocol (S35)
  - note: current plugin does not support KV v2 and misreads a v2 secret's nested `data.data`; four-stage migration with the 33 existing plugin tests as non-regression evidence

## Specs

| ID | Title | Path |
|----|-------|------|
| S1 | Desktop UI standard | docs/ui-standard.md |
| S2 | UX interaction standard | docs/ux-interaction-standard.md |
| S3 | UI standard audit (initial) | roadmap/UI_STANDARD_AUDIT.md |
| S4 | UI standard compliance plan | roadmap/UI_STANDARD_COMPLIANCE_PLAN.md |
| S5 | UI standard compliance audit (per-view evidence) | roadmap/UI_STANDARD_COMPLIANCE_AUDIT.md |
| S6 | Track 2 — GRC & reporting spec | docs/roadmap/TRACK_2_GRC_REPORTING.md |
| S7 | Track 3 — ASPM spec | docs/roadmap/TRACK_3_ASPM.md |
| S8 | Track 4 — Integrations spec | docs/roadmap/TRACK_4_INTEGRATIONS.md |
| S9 | Track 5 — Packaging spec | docs/roadmap/TRACK_5_PACKAGING.md |
| S10 | Release engineering operational guide | docs/packaging/release-engineering.md |
| S11 | Database uniformization plan | docs/plano-uniformizacao-banco.md |
| S12 | Milestone 6.1 spec — tooling & preparation | roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md |
| S13 | Milestone 6.2 spec — safe fixes & naming | roadmap/track-6/MILESTONE_6.2_SAFE_FIXES_NAMING.md |
| S14 | Milestone 6.3 spec — relationships & indexing | roadmap/track-6/MILESTONE_6.3_RELATIONSHIPS_INDEXING.md |
| S15 | Milestone 6.4 spec — types & dead schema | roadmap/track-6/MILESTONE_6.4_TYPES_DEAD_SCHEMA.md |
| S16 | Track 7 — Security spec | docs/roadmap/TRACK_7_SECURITY.md |
| S17 | Threat model | docs/security/THREAT_MODEL.md |
| S18 | ASVS L2 checklist | docs/security/ASVS_L2_CHECKLIST.md |
| S19 | Findings register | docs/security/FINDINGS.md |
| S20 | Security baseline report (2026-08-26) | docs/security/baseline-2026-08-26.md |
| S21 | Supply-chain policy | docs/security/SUPPLY_CHAIN.md |
| S22 | Secrets handling & rotation | docs/security/SECRETS.md |
| S23 | Data protection | docs/security/DATA_PROTECTION.md |
| S24 | Security burn-down | docs/security/BURN_DOWN.md |
| S25 | Triage SLA | docs/security/TRIAGE_SLA.md |
| S26 | Track 8 — Risk governance spec | docs/roadmap/TRACK_8_RISK_GOVERNANCE.md |
| S27 | Track 9 — MIGR-TI/IA spec | docs/roadmap/TRACK_9_MIGR_TI_IA.md |
| S28 | MIGR-TI/IA coverage analysis | docs/methodology/migr-ti-ia-coverage.md |
| S29 | Scanner importer field mappings | docs/features/scanner-importers.md |
| S30 | Notification channels | docs/features/notification-channels.md |
| S31 | Issue-tracker sync | docs/features/issue-tracker-sync.md |
| S32 | Enterprise authentication | docs/features/enterprise-authentication.md |
| S33 | Posture integrations | docs/features/posture-integrations.md |
| S34 | Jira Service Management | docs/features/jira-service-management.md |
| S35 | BastionVault integration SDK migration design | docs/features/bastionvault-integration-sdk-migration.md |
| S36 | Secret vaults | docs/features/secret-vaults.md |
| S37 | Settings form rollout | roadmap/SETTINGS_FORM_ROLLOUT.md |
