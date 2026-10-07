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
of them. Track 10 is planned too; its foundation milestone (M52) corrects defects found in Track 3, 4
and 8 code while specifying the index, and each correction is its own task rather than an edit to a
ticked box.

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

### [M51] Hosts View Redesign
> outcome: The Hosts view is a header-plus-tabs master-detail whose panes resize, exposes criticality,
> environment and owner, filters by status, team, criticality and environment, and shows a
> field-level change history for every host.
> spec: S38

- [x] T230 Rebuild the Hosts view as header card + resizable tabbed detail (Vulnerabilities, Overview, Services, History, Comments) with persisted pane width and tab, facet filters and paging (S38)
  - note: double-clicking a vulnerability is a no-op — the Vulnerabilities view can be navigated to but not opened on one finding — and the column chooser stays out of scope (S38 §8); the four low-use columns are in the grid with `IsVisible="False"`
  - note: Reload now refreshes the current page and the environment facet with the filters kept, where it used to clear the search; the pane width is applied and saved from the view's code-behind because a `ColumnDefinition` cannot carry a binding
  - note: verified by build, `LintUi` and `GUIClient.Tests` source scans only — the layout (splitters against star rows, tab restore, Ctrl+F focus) has not been observed in a running client
- [x] T231 Render host services as icon chips on the Overview tab and as a full Services tab, and wire the History tab (S38)
  - note: the per-service vulnerability count is computed from the host's own findings (`Vulnerability.HostServiceId`), because `GET /Hosts/{id}/Services` does not populate `HostsService.Vulnerabilities`
  - note: History shows the host's rows only (service rows are not merged, S38 §8) and renders team ids, criticality levels and statuses by name; the list is reloaded with each selection, so a save from the edit dialog — which does not await its PUT — can appear one selection late
- [x] T232 Expose host criticality, environment and owner in the detail view and edit dialog with tokenized criticality/severity pills (S38)
  - note: the client has one (dark) theme, so the twelve `NrCriticality*` tokens alias Semi's dark palette with no light variant; `GUIClient.Tests` now references the Avalonia-free `Material.Icons` package so the service icon map is checked against the real `MaterialIconKind` enum
- [x] T233 Allow filtering hosts by criticality, environment, owner, source and risk score, add the environments facet endpoint and total-count paging on the client (S38)
  - note: `lastVerificationDate` is filterable too, and `criticality==null` selects the "Not set" hosts; only `source` has a translated alias (pt-BR `origem`), the other new columns answer to their invariant names
- [x] T234 Record a field-level change history for hosts and expose it via `GET /Hosts/{id}/History` (S38)
  - note: import actors are set where the import holds its own context — finding ingestion (`<Importer> import`, so Vision One findings read `Trendmicro-visionone import`), Jira Assets and the Trend Micro inventory/risk-score syncs; the legacy `/Vulnerabilities` Nessus importer writes hosts through `IHostsService`, which opens a context per call, so its rows carry the caller's login (or `system`) until that path takes an actor
  - note: `HostsService` rows are recorded but not merged into the host's History (S38 §8) and the generic `/AuditTrail/{type}/{id}` reader refuses both host types, because `audit_logs` has no entity id to scope by
  - note: a host inserted with a database-generated id has its create row recorded against the provisional key the interceptor sees before the insert (existing interceptor behaviour), so on MariaDB a host's History starts at its first edit
- [x] T235 Add a per-host open-vulnerability severity summary endpoint feeding the header card (S38)
  - note: "open" is the Master Dashboard's closed set, moved to `Model.Status.ClosedStatuses` so both read one definition; `Total` counts every finding on the host, `Open.Total` the open ones; the batch route has no client method yet because the list does not call it in this milestone

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
- [x] T223 Recover the desktop client's session in place when its token is rejected mid-use (e.g. expired across a machine sleep/hibernate), instead of leaving it stuck resending the same rejected token with no way back but quitting
- [x] T224 Release and recover the shared LiteDB configuration-store mutex when a caller's read/write throws, instead of every config access (including the auth token) failing the same way forever after just one bad LiteDB open

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
- [x] T225 Show a toast confirming how many Assets object types were loaded, instead of the click's only visible effect being an edit-mode combo box the operator had to double-click a row to check
- [x] T226 Report why a Jira Assets call failed in transit (timeout, dropped connection, oversized response) instead of "(no response body)"
- [x] T227 Give a Jira Assets object search 120 s per page instead of 30 s, so a slow instance no longer cuts an import short
  - note: found with T226 — the preview stopped at 700 and 900 objects with "timed out after 30s" while the same page answered in about 1 s from a workstation; 120 s is a ceiling chosen from that, not a measured p99

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
the fifteen prioritized gaps from that analysis's §12, grouped by dependency. **Status: in progress —
Stage 9.1 is implemented (T144–T148: schema version 88, `/RiskChain`, the coverage metric and their desktop
screens); Stage 9.2 is implemented (T150–T155: schema version 89, the structured scenario, evidence confidence,
standalone hypotheses, near misses and the duplicate warning); Stage 9.3 is implemented (T156–T160: schema version
90, business impact analysis, cascading dependencies, restoration tests and the weighted continuity threat); the
runtime observations and human/security review of all three are pending.** Does not reopen Track 8. Detailed specifications:
[docs/roadmap/TRACK_9_MIGR_TI_IA.md](docs/roadmap/TRACK_9_MIGR_TI_IA.md) (S27).

> **Two gates apply to every stage.** Gate 1 — a complete, reviewed specification (eleven required
> sections) merges under `docs/roadmap/track9/9.N-<slug>.md` before the first implementation commit.
> Gate 2 — no item is ticked without the tests its specification planned: happy path, every guard/error
> branch, a regression test that fails on the pre-fix code, schema idempotence/replay, negative
> authorization cases. See [src/AI_TESTING_INSTRUCTIONS.md](src/AI_TESTING_INSTRUCTIONS.md).

### [M39] Stage 9.1 — The linkage chain: objective → process → IT service → data → asset
> outcome: A risk traces to a strategic objective, not only to one generic entity. Closes gap 1.
> spec: S27, S41

- [x] T143 Merge the Stage 9.1 specification with all eleven sections (S41)
  - note: not yet merged and not yet human- or security-reviewed — ticked at the user's request to start implementation; the reviewer agent's nine findings (2026-10-06) are addressed in the spec's first amendment
- [x] T144 Model the strategic objective as a first-class entity
  - note: `strategicObjective` entity type and `businessProcess.strategicObjectives` (schema 2.5); appears in EntitiesView through the configuration, with no view change
- [x] T145 Add an itService type to the entity schema (technical owner, processes served)
  - note: also adds `businessProcess.criticality` (shared with T242) and the `Application`/`team` labels found missing in all three resource files
- [x] T146 Link risks to each optional, queryable link of the chain
  - note: `risk_chain_links` (schema 88), `/RiskChain`, legacy mirroring, statistics union, the risk-detail chain block and EditRiskChainDialog; the S41 §8 runtime observations are still to be recorded on the PR
- [x] T147 Compute a critical-process coverage metric counting only processes marked critical
  - note: `GET /RiskChain/Coverage/CriticalProcesses` and report 7 "Critical process coverage"; the ratio is unrounded on the server and shown to one decimal
- [x] T148 Test chain traversal with a missing middle link and legacy single-link coexistence
  - note: S11, G2, K5, L1–L8 and the copy/replay/race cases of Track9RiskChainSchemaTests; the four runtime observations of S41 §8 are still to be recorded on the PR

### [M40] Stage 9.2 — Structured scenario, record discrimination and evidence confidence
> outcome: Four scenario fields plus a confidence level make the Phase 2 quality rules machine-verifiable. Closes gap 2.
> spec: S27, S42

- [x] T149 Merge the Stage 9.2 specification
  - note: written as S42 with all eleven sections in the same change as the implementation, and ticked with it as the M40 delivery brief asked (as T143 was for M39) — not yet merged and not yet human- or security-reviewed
- [x] T150 Split cause/threat, vulnerability/condition, central event and consequences into separate fields
  - note: four nullable `text` columns on `risks` (schema 89), an editor section with the methodology's template sentence and a detail block; optional by design (S42 D2)
- [x] T151 Add an evidence confidence level (confirmed / indicative / hypothesis)
  - note: `risks.evidence_confidence`, NULL reads "not declared"; a promoted pending risk starts as Hypothesis unless the triager says otherwise
- [x] T152 Support standalone hypothesis records, not only ones originating from an assessment answer
  - note: `POST /Risks/Pending` (origin Standalone, author recorded) and a form on the pending-risks tab; promotion writes `HYP-{id}` and makes the author the submitter
  - note: the queue is entity-scoped (S42 amendment 2): `pending_risks.entity_id`, back-filled from the raising assessment; list, promote and dismiss go through the model's query filter, a new hypothesis is filed under an entity in the caller's scope, and a cross-entity write is a 403 rather than a 500 — the desktop risk editor still blanks `reference_id` on its next save (pre-existing, S42 §11 defect 4), so the durable link is `pending_risks.promoted_risk_id`
- [x] T153 Distinguish a near miss from an incident
  - note: `incidents.kind`, orthogonal to the threat category; only the Master Dashboard stops counting near misses as open incidents — IRP automation, notifications and exports are unchanged (S42 D9)
- [x] T154 Add duplicate-risk detection on (central event, consequence) as a warning, not a block
  - note: deliberately out of scope — back-filling the four fields from existing free text; legacy risks keep them null
  - note: `POST /Risks/ScenarioDuplicates` under `RequireRiskmanagement`, exact match after normalising case, accents, spacing and trailing punctuation; the risk editor asks before saving and a failed check never blocks the save
- [x] T155 Test that a legacy risk with all four fields null stays editable, listable and scorable
  - note: E1–E3 of `RiskScenarioInMemoryTest`; the five runtime observations of S42 §8 are still to be recorded on the PR

### [M41] Stage 9.3 — BIA: MTPD/MAO, RTO, RPO and cascading dependencies
> outcome: Continuity fields that flag 4, Gate A and the restoration metric depend on. Closes gap 4.
> spec: S27, S43

- [x] T156 Merge the Stage 9.3 specification
  - note: S43 with all eleven sections, amended 2026-10-07 with the user's answers (unverified counts at a configurable weight; validity configurable; self-attestation visible only; global-scope writes); merged in bfcd84cd before the implementation — human and security review of the implementation still pending
- [x] T157 Declare MTPD/MAO, RTO and RPO on the process and the IT service, plus process criticality
  - note: `business_impact_analyses` (schema 90), `PUT/DELETE /Continuity/Subjects/{id}/Bia` under `bia_manage` and global scope; process criticality is derived from the MTPD and wins over the declared property in the coverage metric (S43 D4) — blocking edits of the declared property stays with T241
- [x] T158 Model dependencies with cascading effect
  - note: `bia_dependencies`; cycles are accepted and reported, never refused (S43 D7); the requirement is not additive along the chain (D8); third-party dependencies wait for Stage 9.10
- [x] T159 Add restoration-test records comparable against the declared RTO/RPO
  - note: insert-only `restoration_tests` with void, the verification against the latest valid test, the weighted threat (unverified = 0.5 by default) and report 8; validity and weight are audited `settings` rows editable by administrators only
- [x] T160 Test that a declared RTO with no restoration test reads as unverified, not met; cyclic dependencies don't recurse forever
  - note: V1, P1, C7–C9 (a ring of 10 000 nodes), T7 and PC5 plus Track9ContinuitySchemaTests Q1–Q8, which need Docker and were not run here; the S43 §8 runtime observations are still to be recorded on the PR

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

## Track 10 — Consolidated Cyber Risk Index & Overview Dashboard

One configurable-weight 0–100 index, the **ICR (Índice Consolidado de Risco)**, over the risk register
and its management reviews, acceptances and campaigns, vulnerabilities, incidents, assessments, the
CMDB, the entity and process map, Trend Micro Vision One, SecurityScorecard and Tenable — shown as a
desktop "Cyber Risk Overview" with trend, contributing categories and set-based drill-down by unit,
process, activity, application, asset class and source. Methodology (pt-BR):
[docs/methodology/icr-indice-consolidado-de-risco.md](docs/methodology/icr-indice-consolidado-de-risco.md)
(S39). Dashboard and configuration screens:
[docs/features/risk-overview-dashboard.md](docs/features/risk-overview-dashboard.md) (S40). **Status:
planned, nothing started.**

> **The index monitors; it never decides.** MIGR-TI/IA gates A→D stay the decision path: no service on
> that path reads the index (enforced by `IcrIsNotADecisionInputTest`), the index is never registered as
> a KRI, and only a Gate A condition from M43 may floor the displayed value — always shown next to the
> unfloored one. Weights live in versioned profiles approved with segregation of duties and an IT Risk
> Committee reference, and a profile change is a marked discontinuity on the trend.

> **Ordering.** M52 lands before any snapshot is published, and T236 lands before or with T237: giving
> background jobs a real scope otherwise switches on the overwrite of quantitative risks. Interim mode
> reaches scoped users only after the 8–12 week shadow pilot (T258) and after hierarchical entity scope
> (T292), which changes authorization for the whole product and gets its own security review. Each
> source added by M56–M57 enters as a new profile version with a discontinuity marker. M59 consumes
> Track 9 and does not duplicate it.

> **Product-owner decisions (2026-10-05).** A scope claim on a unit covers its subunits but never its
> parent (T292); profiles are approved by the Risk Manager or Risk Administrator role (T293); Tenable
> is integrated through Tenable Vulnerability Management cloud with a Tenable One licence, so Security
> Center moves to the backlog (T269); Vision One CREM credits and API permissions are available
> (T271–T273); the Master Dashboard is retired once the interim index is published (T294); at
> publication only the risk team (Risk Analysts, Risk Managers and Risk Administrators) sees the index
> (T293); band labels are bilingual, Portuguese and English, like the rest of the product (T260); and
> entity risk context stays editable only with global scope, with unit-manager editing kept for when
> unit managers use the product (T295).

### [M52] ICR foundation: the data the index depends on
> outcome: The nightly snapshot can read a trustworthy register, finding set and entity map — jobs see
> data, quantitative risks are not overwritten, bands apply one way, a closed finding is closed
> everywhere, and the entity context that moves weights is permissioned and audited.
> spec: S39

- [ ] T236 Stop the residual pass and the 2-hourly matrix recalculation from overwriting quantitative (`ScoringMethod = 3`) risks, landing before or with T237 (S39)
- [ ] T237 Run background jobs with unrestricted entity scope and register the five scheduled-but-unregistered Track 8 jobs, with tests that every recurring job resolves and reads seeded rows (S39)
  - note: found while specifying the index — the background principal carries no scope claim, so every Hangfire job reads `DenyAll`; the scheduled Vision One/SecurityScorecard syncs, the residual pass, campaigns and cadence currently see zero rows
- [ ] T238 Apply risk bands through one `Faixa` function (inclusive lower bounds, reachable Very High) in `Tools`, replacing the three divergent evaluations (S39)
- [ ] T239 Route desktop close/reject of findings through the finding lifecycle and backfill `status_id` for legacy-closed rows, so "open" means the same everywhere (S39)
- [ ] T240 Backfill `risks.entity_id` from `risk_to_entity` and define scenario attribution as their deduplicated union (S39)
- [ ] T241 Gate entity create/update/delete behind `entities_manage` and the risk-context properties behind `entity_risk_context` (writes require global scope), and add `Entity` and its properties to the field-level audit (S39, S40)
- [ ] T242 Add `criticality` and `internetFacing` properties and an `activity` type under `businessProcess` in a new entity-configuration version (S39)
- [ ] T292 Make entity scope hierarchical: a claim on an entity grants its descendants in the entity tree (`entities.parent`) and never its ancestors or siblings, for reads and for the write guard, with regression tests on every scoped record type (S39, S40)
  - note: product-owner decision 2026-10-05; authorization-sensitive (Track 7 rules: regression test failing on the pre-fix code, behaviour observed at runtime); links held only in EAV properties (a process listing a unit in `organizationUnit`) do not grant access, so a process is visible to a unit's users when it is parented under that unit

### [M53] ICR engine, versioned profiles and daily snapshots
> outcome: A nightly job computes the ICR for every pre-computed node from an approved, versioned
> profile and stores node and object rows; the API serves the overview, trend, drill-down,
> contributions and change attribution with entity scope respected.
> spec: S39
> depends-on: M52

- [ ] T243 Add the `risk_index_profiles`, `risk_index_snapshots` and `risk_index_object_days` tables and the `risk_index_view`, `risk_index_configure` and `risk_index_approve` permissions (S39, S40)
- [ ] T293 Seed the Risk Manager and Risk Administrator roles where absent and grant them `risk_index_view`, `risk_index_configure`, `risk_index_approve` and `entity_risk_context`, and grant Risk Analyst `risk_index_view` and `risk_index_configure`, never the Administrator role (S39, S40)
  - note: product-owner decision 2026-10-05 — the Risk Manager or Risk Administrator acts for the IT Risk Committee when approving profiles; only `Administrator` and `RiskAnalyst` are seeded today (`Data/1.sql:294-295`)
- [ ] T244 Implement the ICR engine: anchors, max-plus-damped-remainder, power mean with tail floor and population quotas, conservative inclusion, headline, coverage, quality and ignorance interval (S39)
- [ ] T245 Compute review credit and governance signals from reliable fields only — qualified reviews, acceptances by date, campaign decisions, tasks and SLA (S39)
- [ ] T246 Resolve the entity closure (tree plus EAV process/application links, activities under processes) and set-based node membership with an "Unassigned" node (S39, S40)
- [ ] T247 Add the 05:00 UTC `RiskIndexSnapshotJob` (node scope members, per-class daily values, discontinuity-marker detection), its retention job and the `IcrIsNotADecisionInputTest` allowlist (S39, S40)
- [ ] T248 Compute Euler composition, indicator sensitivity and Aumann–Shapley change attribution per node (S39)
- [ ] T249 Expose the read API for overview, categories, trend, drill-down nodes, objects, inventory, change attribution and governance indicators, with scope-aware partial results, per-module redaction, a scope- and permission-keyed cache and a budget for derived computations (S40)
- [ ] T250 Expose the profile lifecycle API — draft, validation, 90-day sensitivity preview, submit, approval eligibility, segregated approval with an explicit (non-admin-implied) permission and committee reference, reject, retire with its own justification, and activation bridge (S39, S40)

### [M54] Cyber Risk Overview dashboard
> outcome: The desktop client shows one headline number with its band, trend, contributing categories,
> asset-class tabs and drill-down by unit, process, activity, application, asset class and source,
> with every point traceable to the objects and signals behind it.
> spec: S40
> depends-on: M53

- [ ] T251 Add the Cyber Risk Overview module with the headline card — integer value, band label, unfloored value, quality and coverage, seals and chips — gated by `risk_index_view` (S40)
- [ ] T252 Draw the trend with band areas, the EWMA line, floor shading and discontinuity markers over 30, 90 and 365 days (S40)
- [ ] T253 Show the contributing categories and, per asset-class tab, the monthly category summary, top risk factors and attack-surface visibility (S40)
- [ ] T254 Drill down by unit, process, activity, application, asset class, source, criticality and environment through a breadcrumb and an always-visible Explore panel with one drill state, with a node detail and object list (S40)
- [ ] T255 Show "what moved", top contributions with "held by" and indicator sensitivity, and an object detail with factor readings and freshness (S40)
- [ ] T256 Show inventory visibility per source (assessed, stale, presumed, not assessed), the always-visible data-quality strip with unassigned and default-criticality fractions, and the governance and review indicators shown beside the index (S40)
- [ ] T257 Derive chart colours from theme tokens through a tested palette helper, since `LintUi` does not scan C# (S40)
- [ ] T258 Approve a pilot profile, run the 8–12 week shadow pilot with backtesting, then approve the publication profile (as a rebaseline if calibration requires it) before publishing interim mode to the risk team (S39, S40)
  - note: starts once T251–T253 and T259–T262 are done; the pilot profile is approved through the M55 screens (S40 §14.1)
- [ ] T294 Retire the Master Dashboard (module, navigation entry, `GET /Dashboard/Master`, its service, DTOs, client service and tests) once the interim index is published to scoped users (S40)
  - note: product-owner decision 2026-10-05; two dashboards with different numbers would confuse, and `PostureScore` is not methodology

### [M55] ICR configuration screens
> outcome: Weights and parameters change only through a validated draft profile with a sensitivity
> preview and a segregated approval, and sources, vendor-to-entity mappings and entity risk context are
> configured from the admin window.
> spec: S40
> depends-on: M53

- [ ] T259 List profile versions with status, effective dates and a parameter diff between any two versions (S40)
- [ ] T260 Edit a draft profile — category weights, grouped parameters, presets and bands with Portuguese and English labels — with inline validation including the joint tail lock (S40)
- [ ] T261 Run and read the sensitivity preview (node deltas, band changes, Kendall τ, tornado, effective register weight) and submit with justification (S40)
- [ ] T262 Approve or reject a submitted profile with server-computed segregation-of-duties checks, self-grant refusal, committee and board references, show the activation bridge, and suspend publication (S40)
- [ ] T263 Configure enabled sources and freshness windows and map Vision One asset groups/tags and SecurityScorecard connections to entities (S40)
- [ ] T264 Edit entity risk context — criticality, internet exposure, data classification and activities — under the dedicated permission, in all-or-nothing batches with change history (S40)

### [M56] Tenable integration
> outcome: Tenable Vulnerability Management (cloud, with the Tenable One licence) findings, assets,
> VPR, EPSS, ACR and AES reach NetRisk through the export APIs, not only through `.nessus` uploads.
> spec: S40
> depends-on: M52

- [ ] T265 Add a Tenable Vulnerability Management connection with vault-capable credentials that are never logged, the outbound-policy HTTP client, a test probe and a queued manual sync run by the jobs host with a heartbeat (S40)
- [ ] T266 Sync findings incrementally through the vulnerability export API (VPR v2, EPSS, exploit maturity) into the finding pipeline (S40)
- [ ] T267 Sync assets through the asset export API onto hosts by the identity chain, collecting ACR and AES as per-source readings that the index uses only when the approved profile enables them (S40)
- [ ] T268 Attribute Tenable assets to entities through a tag-to-entity mapping, after T265 and T267 (S40)

### [M57] Vision One and SecurityScorecard posture expansion
> outcome: Vendor posture is kept per source with history, and Vision One's posture, asset groups,
> internet-facing assets, identities, cloud assets, apps and alerts reach the index as scored objects
> or reference indicators.
> spec: S40
> depends-on: M52

- [ ] T270 Store vendor scores per source with history and stop writing `entities.cyber_risk_index` (S39, S40)
- [ ] T271 Sync Vision One `securityPosture` and `assetGroups` as reference indicators (S40)
- [ ] T272 Sync Vision One internet-facing FQDNs/IPs, accounts, cloud assets and local apps as scored objects (S40)
- [ ] T273 Sync Vision One Workbench alerts as an attack-intensity indicator, not a scored factor (S40)
- [ ] T274 Backfill SecurityScorecard score history, keep per-factor issue score impact, and store a missing factor as null rather than 0 (S40)
- [ ] T275 Persist Vision One last-detect time, exploit attempts, global exploit activity and EPSS separately, and record a criticality source with precedence (S39, S40)
- [ ] T276 Compute posture sync due-dates so a 24-hour interval syncs daily (S39)

### [M58] Governance and asset data quality for the index
> outcome: The review, incident, assessment and asset data the index reads with a documented
> workaround is corrected at the source, so each workaround can be retired.
> spec: S39

- [ ] T277 Align the `MgmtReview` review and next-step constants with the seeded lookups (S39)
- [ ] T278 Stamp management review dates in UTC on the server, route desktop reviews through `CreateReviewAsync`, and expose overdue reviews honouring acceptance expiry (S39)
- [ ] T279 Add incident severity and a UTC resolved date, and audit incidents field by field (S39)
- [ ] T280 Link assessment answers by option id, record submission time and score runs on the server (S39)
- [ ] T281 Record a scan-only `last_assessed_at` on hosts and match finding hosts by external id before IP (S39)
- [ ] T282 Add a host-to-entity assignment path with an explicit re-stamp policy, and a target application on code-scanner imports (S39)
- [ ] T283 Audit the product inputs that move the index (`review_levels`, `risk_levels`, settings and SLA configurations) (S39)
- [ ] T290 Persist the residual ALE mean (`risk_scoring.quant_residual_ale_mean`) so the monetary panel can show residual Σ E[L] (S39)
  - note: the simulator already computes the residual mean (`QuantitativeRiskService.cs:122`) but only P10/P50/P90 are stored
- [ ] T291 Import the Jira Assets internet-facing attribute onto hosts and the application-to-server relationship onto host–application links, so `m_net` reads CMDB evidence and the full-mode linkage chain can attribute hosts to applications (S39)

### [M59] ICR full mode: consuming Track 9
> outcome: The index runs in full mode — drill-down along the linkage chain, BIA criticality, KEV/EPSS,
> the Gate A floor, Top Risks, portfolio P95/CVaR, KRIs and the third-party category — consuming each
> Track 9 stage as delivered.
> spec: S39
> depends-on: M39, M40, M41, M42, M43, M44, M45, M46, M47, M48, M49

- [ ] T284 Drill down by process, activity, IT service, data and asset through the M39 linkage chain, with criticality inheritance (S39)
- [ ] T285 Consume BIA process criticality (M41) and the KEV/EPSS combined prioritisation (M42) (S39)
  - note: data sensitivity for Δ_dados comes from the M49 catalogue when delivered (S39 §16)
- [ ] T286 Apply the Gate A display floor from the M43 predicate and host the Top Risks list (S39, S40)
- [ ] T287 Show portfolio P95/CVaR (M45), KRI and Gate B seals (M46), evidence confidence (M40) and the target level (M44) (S39, S40)
- [ ] T288 Enable the third-party category from the M48 register, and committee approval and third-line read access from M47 (S39)

## Backlog

- [ ] T216 Ship a Mobile Companion App: lightweight iOS/Android viewer for executive incident tracking and risk sign-off
- [ ] T217 Add Real-Time Collaboration: synchronized editing for IRPs and joint risk assessments
- [ ] T218 Add AI-Assisted Risk Scoring (LLM-based vulnerability analysis and mitigation proposals)
  - note: precondition — Stage 9.12 (AI governance) must land first; the methodology requires the governance instrument before the use
- [ ] T219 Move the BastionVault plugin onto BastionVault.IntegrationSdk, replacing the hand-written wire protocol (S35)
  - note: current plugin does not support KV v2 and misreads a v2 secret's nested `data.data`; four-stage migration with the 33 existing plugin tests as non-regression evidence
- [x] T228 Let users read and add comments on a host from the Hosts view
  - note: reuses the existing `comments.host_id` column; the comment is attributed server-side to the authenticated user
- [x] T229 Make the Jira Assets admin tab scrollable so its imported-objects grid is reachable
- [ ] T269 Add Tenable Security Center as a second client behind the same normaliser (S40)
  - note: moved from M56 on 2026-10-05 — the organisation uses Tenable Vulnerability Management cloud with Tenable One; kept for installations that run Security Center
- [ ] T289 Add a read-only executive Cyber Risk Overview page to the RiskPortal (S40)
  - note: the portal is Razor Pages over the API with no chart library; a page needs the S40 read API and inline SVG or a library vetted under S21
- [ ] T295 Let unit managers edit the risk context (criticality, internet exposure, data classification) of entities in their own subtree, requiring global scope only for a process shared by more than one unit (S40)
  - note: product-owner decision 2026-10-05 — not now, unit managers do not use the product yet; the write rule already lives in one policy (`RiskContextWritePolicy`) so this lands without an API or screen change

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
| S38 | Hosts view redesign | docs/features/hosts-view-redesign.md |
| S39 | ICR — consolidated risk index methodology | docs/methodology/icr-indice-consolidado-de-risco.md |
| S40 | Cyber Risk Overview dashboard and ICR configuration screens | docs/features/risk-overview-dashboard.md |
| S41 | Stage 9.1 — linkage chain specification | docs/roadmap/track9/9.1-linkage-chain.md |
| S42 | Stage 9.2 — structured scenario, record discrimination and evidence confidence specification | docs/roadmap/track9/9.2-structured-scenario.md |
| S43 | Stage 9.3 — BIA: MTPD/MAO, RTO, RPO and cascading dependencies specification | docs/roadmap/track9/9.3-bia-continuity.md |
