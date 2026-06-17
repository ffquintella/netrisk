# NetRisk Product Roadmap

This document tracks the strategic direction and planned features for NetRisk. To allow the team to select features freely and adapt to changing priorities, this roadmap is organized as **modular Milestone Tracks**. Each track represents a major area of capability, allowing features to be scheduled, developed, and released independently or in mixed-milestone batches.

For shipped changes, see [CHANGELOG.md](CHANGELOG.md).

---

## Guiding Principles

- **FOSS Risk Management:** Professional-grade GRC tools accessible to small/medium organizations.
- **Secure by Default:** Designed with deep-tier segregation, role-based access, and enterprise security standards.
- **Cross-Platform:** Full feature parity on Windows, Linux, and macOS.
- **Modular Architecture:** Segregated API, Avalonia GUI, background jobs, and a pluggable system core.

---

## 🗺️ Milestone Tracks

```
┌────────────────────────────────────────────────────────────────────────┐
│ MODULAR MILESTONE TRACKS:                                              │
│                                                                        │
│   Track 1: Modern Desktop Experience (UI/UX Compliance)                │
│   Track 2: GRC Core & Reporting Engine (Vulnerability/Risk)            │
│   Track 3: Vulnerability Aggregation & Finding Lifecycle (ASPM)        │
│   Track 4: Integrations & Notification Channels (Slack/Jira/Teams)     │
│   Track 5: Native Packaging & Release Engineering                      │
│   Track 6: Database Uniformization & Schema Health                     │
│   Track 7: Security Review & Hardening                                 │
└────────────────────────────────────────────────────────────────────────┘
```

---

### Track 1: Modern Desktop Experience (UI/UX Compliance)

This track focuses on performance tuning, visual standardization, and desktop ergonomics to achieve a world-class user experience.

> **Reference item — UI-STD-001: Full `docs/ui-standard.md` compliance sweep** *(Priority: High)*
> The source standard lives at [docs/ui-standard.md](docs/ui-standard.md). A refreshed static audit of `GUIClient/Views/**/*.axaml` on 2026-04-29 found broad divergence: 23 files with hard-coded color literals, 39 with literal user-facing strings, 29 with hard-coded window titles, 28 button-class deviations, and 18 fixed-width input/layout violations. The line-item remediation work is tracked by the milestones below; the supporting audit and plan live in [roadmap/UI_STANDARD_AUDIT.md](roadmap/UI_STANDARD_AUDIT.md) and [roadmap/UI_STANDARD_COMPLIANCE_PLAN.md](roadmap/UI_STANDARD_COMPLIANCE_PLAN.md).
> **Acceptance criteria:** every file under `GUIClient/Views/**/*.axaml` has a recorded pass/fail compliance status; no unapproved hard-coded colors, spacing, or typography values remain; reusable style classes/resources are used where required; verification evidence is captured in a follow-up audit document; and a UI compliance checklist is added to the PR template (or a CI validation step for XAML style rules).

#### Milestone 1.1: Visual Theme Standardization (Completed)
*Align all 67 views of the desktop client with the uniform visual standard to eliminate design and token drift.*
*   [x] **Color & Depth Tokenization:** Replace inline hex colors (`#222222`, `#666666`) and named brushes (`Azure`, `Green`, `Red`) with semantic class references from `WindowStyles.axaml` and `DarkStyles.axaml` to enforce the 5-plane depth model.
*   [x] **String Extraction (Localization):** Move all user-facing English strings and window titles (such as "Save", "Cancel", "Score", "ID") into localized `.resx` resource dictionaries and bind them via `Str*` VM properties.
*   [x] **Button Taxonomy Enforcement:** Re-class legacy and unclassed buttons to follow the canonical button taxonomy (`dialog1`, `dialog2`, `operation`). Implement unified icon+text stacks on all 28 button-bearing views.
*   [x] **Responsive Form Sizing:** Convert fixed-width form layouts (e.g., inputs with static `Width`) to responsive `Grid`/`SpacedGrid` columns and `MinWidth` constraints using the standard spacing scale (xxs to xxl).
*   [x] **Theme Protection Audits:** Integrate automated lint checks inside Nuke builds or pre-commit hooks to detect and reject inline hex colors or unclassed button tags in AXAML files.

#### Milestone 1.2: Shell Backdrop & Material Stabilization (Completed)
*Deliver beautiful glassmorphic window compositions with clean, solid-color fallbacks across various host OS window managers.*
*   [x] **MainWindow Acrylic Panel:** Refactor `MainWindow.axaml` to wrap content in a layout-compliant acrylic/Mica panel structure.
*   [x] **Windows 11 Mica Integration:** Implement conditional platform-aware materials to apply native Mica backdrops for stable, high-performance backgrounds.
*   [x] **macOS Vibrancy Integration:** Apply native Apple system Vibrancy on sidebar and navigation panels under Darwin.
*   [x] **Graceful Fallbacks:** Ensure backgrounds gracefully degrade to a high-contrast solid background (`#282928` / `surface/base`) when desktop compositions or GPU acrylics are unsupported (e.g., older Linux desktops or virtual machines).
*   [x] **Sizing Constraints:** Enforce standard window sizing (e.g., MinWidth and MinHeight constraints) globally.
*   [x] **Code Cleanup:** Purge the legacy, unreferenced scratch file `src/GUIClient/Views/teste.axaml`.

#### Milestone 1.3: Compiled Bindings & Rendering Optimization (Completed)
*Unleash extreme rendering speeds, minimize RAM footprint, and enable compile-time binding safety.*
*   [x] **Explicit DataType Bindings:** Declare explicit `x:DataType="vm:ClassName"` across all 85+ views.
*   [x] **VM Refactoring:** Resolve compile-time binding errors on legacy, reflection-based view-models.
*   [x] **Enable Compiled Bindings Globally:** Flip the configuration flag to `true` in `netrisk.sln`:
    `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`.
*   [x] **UI Virtualization:** Enforce virtualization on all list containers, and implement the high-performance `TreeDataGrid` container for dense incident and vulnerability grids.
*   [x] **Binding Visibility Hardening:** Audit all views against their `x:DataType` view-models and promote bound members (labels, commands, child VMs, collections) from `private`/`protected` to `public`, since compiled bindings — unlike the old reflection bindings — can only reach public members. (Fixed post-migration regressions in `UserInfo`, `AdminWindow`/`UsersView`, and 24 other views; shipped in 2.5.1.)

#### Milestone 1.4: Platform-Native Ergonomics & Accessibility (Completed)
*Ensure the app feels like a local, native utility rather than a port, and optimize it for keyboard and mouse precision.*
*   [x] **macOS Global Menu Redirection:** A `NativeMenu` mirroring the window menu is attached to `MainWindow`; on Apple Darwin it is hoisted into the system global menu bar while the in-window `Menu` is collapsed (`IsNotMacOS`). On Windows/Linux the native menu is ignored and the in-window menu is shown.
*   [x] **Window Control Alignment:** macOS renders its traffic-light controls top-left natively; the navigation bar is inset dynamically (`NavBarMargin`) so its left-edge content clears those controls once the menu row collapses on Darwin.
*   [x] **Keyboard Accessibility Sweep:** Global `Ctrl+P` (Print/Export → Reports) on `MainWindow`; `Ctrl+S` (Save) + `Esc` (Dismiss) on edit windows and — centralised in `DialogWindowBase` via `ISaveableDialog` — across all modal dialogs; `Ctrl+F` (Search) on the Entities and Incidents views; logical `TabIndex` order plus `IsDefault`/`IsCancel` buttons on the Login and entity forms.
*   [x] **System Tray Integration:** `TrayIconManager` adds a Windows tray icon / macOS menu-bar extra with a quick-status preview (sign-in state + version, refreshed every 15s), Open/Hide/Exit context menu, and minimise-to-tray on Windows.

---

### Track 2: GRC Core & Reporting Engine

This track focuses on the GRC (Governance, Risk, and Compliance) core features, incident workflows, and data output templates. Detailed, research-backed specifications for every milestone live in [docs/roadmap/TRACK_2_GRC_REPORTING.md](docs/roadmap/TRACK_2_GRC_REPORTING.md).

> **Status legend:** `[x]` shipped end-to-end (backend + GUI) · `[~]` backend complete, **GUI not yet implemented** · `[ ]` not started.
> Milestones 2.1–2.4 below are backend-only: the REST API and `ServerServices` exist, but there are no `ClientServices` REST clients or `GUIClient` views, so these capabilities are **not yet usable from the desktop app**. The pre-existing basic Reports / Assessments / Entities / Incidents screens are *not* these enhanced features.

#### Milestone 2.1: Advanced Reporting Engine
*Enable rich, customizable risk reports and automated exports.* — Spec: [docs/roadmap/TRACK_2_GRC_REPORTING.md § 2.1](docs/roadmap/TRACK_2_GRC_REPORTING.md#milestone-21-advanced-reporting-engine)
*   [x] Introduce customizable report templates allowing organizations to define their logo, styling, and sections (backend + GUI designer with section reorder, branding/logo, presets, and live rendered preview).
*   [x] Support scheduled exports of dashboards, compliance grids, and open incidents via email (`ScheduledReportJob` backend + GUI schedule editor with frequency/recipient builders and run-status surfacing).
*   [x] Add PDF, CSV, and Excel export targets for all statistics tables (backend export service + GUI export actions on entity grids and Reports views).
*   [x] **GUI:** report-template designer, scheduled-export configuration screen, and PDF/CSV/Excel export actions on statistics tables.

#### Milestone 2.2: Enhanced Assessments Workflow
*Optimize how organizations collect, triage, and score vulnerability and compliance questionnaires.* — Spec: [docs/roadmap/TRACK_2_GRC_REPORTING.md § 2.2](docs/roadmap/TRACK_2_GRC_REPORTING.md#milestone-22-enhanced-assessments-workflow)
*   [x] Build an interactive, paged assessment viewer supporting nested questions, conditional show/hide logic, and rich-text explanations.
*   [x] Implement progress trackers and draft auto-saving to prevent data loss.
*   [x] Support importing assessment templates from industry standards (e.g., NIST, ISO 27001) via JSON/Excel.
*   [x] **GUI:** paged assessment-run viewer with conditional show/hide, rich-text rendering, progress tracker, draft auto-save, and a template-import dialog.

#### Milestone 2.3: Multi-Entity & Multi-Tenant Support
*Enable managed risk monitoring across distinct organizational subdivisions.* — Spec: [docs/roadmap/TRACK_2_GRC_REPORTING.md § 2.3](docs/roadmap/TRACK_2_GRC_REPORTING.md#milestone-23-multi-entity--multi-tenant-support)
*   [~] Segregate assets, risks, and vulnerabilities by "Business Entity" (Backend complete — enforced server-side; no dedicated GUI surface).
*   [~] Introduce role-based scoped access (e.g., users can only view risks belonging to their assigned Business Entity) (Backend complete — enforced server-side).
*   [~] Add a central Master Dashboard for administrators to view aggregated posture metrics across all entities (Backend complete — GUI pending).
*   [ ] **GUI:** central Master Dashboard view aggregating posture metrics across all entities.

#### Milestone 2.4: Incident Response Automation (IRP)
*Close the loop on incident management with active workflows.* — Spec: [docs/roadmap/TRACK_2_GRC_REPORTING.md § 2.4](docs/roadmap/TRACK_2_GRC_REPORTING.md#milestone-24-incident-response-automation-irp)
*   [~] Create customizable Incident Response Plan (IRP) templates (Backend complete — GUI pending; no `ClientServices` REST client yet).
*   [~] Support automatic task generation and assignment when an incident of a specific type is created (Backend complete via `IrpAutomationService` — GUI config pending).
*   [~] Build task-dependency Gantt trackers to visualize critical paths during emergency response (Backend data model only — no Gantt visualization in GUI).
*   [ ] **GUI:** IRP template editor, automation-rule configuration screen, and a task-dependency Gantt/critical-path view.

---

### Track 3: Vulnerability Aggregation & Finding Lifecycle (ASPM)

This track bridges GRC with Application Security Posture Management (ASPM), allowing organizations to ingest, deduplicate, and triage automated scanner outputs. Detailed, research-backed specifications for every milestone live in [docs/roadmap/TRACK_3_ASPM.md](docs/roadmap/TRACK_3_ASPM.md).

#### Milestone 3.1: Extensible Scanner Importers
*Provide a unified plugin interface to feed findings from any security tool.* — Spec: [docs/roadmap/TRACK_3_ASPM.md § 3.1](docs/roadmap/TRACK_3_ASPM.md#milestone-31-extensible-scanner-importers)
*   [ ] Define a generic `IVulnerabilityImporter` plugin contract in the `netrisk-plugin-sdk` (input: report stream; output: normalized `Vulnerability` + `Host` + `CVEDetail` models).
*   [ ] Refactor the legacy, built-in Nessus parser onto the new extensible contract.
*   [ ] Write native importers for: OWASP ZAP, Trivy, Semgrep, OpenVAS, Burp Suite, Snyk, Grype, and GitHub Dependabot.
*   [ ] API Modernization: Generalize `POST /vulnerabilities/import/{importerName}/{fileId}` with dynamic importer discovery via `IPluginsService`.
*   [ ] GUIClient Modernization: Build a dynamic importer selector inside the vulnerability import dialog.

#### Milestone 3.2: Finding Lifecycle & Audit Trails
*Establish a rigorous triage state-machine for individual findings.* — Spec: [docs/roadmap/TRACK_3_ASPM.md § 3.2](docs/roadmap/TRACK_3_ASPM.md#milestone-32-finding-lifecycle--audit-trails)
*   [ ] Add granular lifecycles: `Active`, `Verified`, `FalsePositive`, `OutOfScope`, `Duplicate`, `RiskAccepted`, `Mitigated`.
*   [ ] Implement an audit logging mechanism to track state transitions (who, when, why) on individual findings.
*   [ ] Introduce a dedicated `RiskAcceptance` entity containing expiration dates, authorizing managers, and business justifications.
*   [ ] Implement a background job (Hangfire) to automatically re-open expired risk-acceptance agreements.

#### Milestone 3.3: Intelligent Deduplication Engine
*Prevent database bloat from repeated automated scans using pluggable matching strategies.* — Spec: [docs/roadmap/TRACK_3_ASPM.md § 3.3](docs/roadmap/TRACK_3_ASPM.md#milestone-33-intelligent-deduplication-engine)
*   [ ] Extend the default hash-based lookup with modular strategies: `HashBased`, `UniqueIdFromTool`, `LegacyHashCode`, `Custom`.
*   [ ] Ensure importing updates existing open findings rather than creating duplicates, while maintaining historical scan logs.
*   [ ] Build an administration UI to toggle and configure deduplication heuristics per scanner type.

#### Milestone 3.4: SLA Tracking & Aging
*Enforce compliance boundaries with automated service level agreements (SLAs).* — Spec: [docs/roadmap/TRACK_3_ASPM.md § 3.4](docs/roadmap/TRACK_3_ASPM.md#milestone-34-sla-tracking--aging)
*   [ ] Introduce `SlaConfiguration` schemas defining max triage/remediation days per severity (Critical, High, Medium, Low).
*   [ ] Implement computed fields tracking `SlaDueDate` and `DaysOverdue` on open findings.
*   [ ] Automate email and webhook breach notifications as target deadlines approach.

#### Milestone 3.5: CI/CD-First Integration API
*Integrate NetRisk directly into automated build pipelines.* — Spec: [docs/roadmap/TRACK_3_ASPM.md § 3.5](docs/roadmap/TRACK_3_ASPM.md#milestone-35-cicd-first-integration-api)
*   [ ] Implement scoped, non-interactive API-token authentication optimized for CI runners.
*   [ ] Support bulk, idempotent direct upload endpoints: `POST /vulnerabilities/import/{importer}` accepting raw payloads.
*   [ ] Publish official, copy-pasteable GitHub Actions, GitLab CI, and Azure Pipelines task recipes.
*   [ ] Support exit-code gating patterns (e.g., fail builds if new Critical vulnerabilities are imported).

---

### Track 4: Integrations & Notification Channels

This track focuses on connecting NetRisk with external messaging platforms, issue trackers, and enterprise identity systems. Detailed, research-backed specifications for every milestone live in [docs/roadmap/TRACK_4_INTEGRATIONS.md](docs/roadmap/TRACK_4_INTEGRATIONS.md).

#### Milestone 4.1: Unified Notification Channels
*Broadcast alerts to platforms where security and engineering teams already communicate.* — Spec: [docs/roadmap/TRACK_4_INTEGRATIONS.md § 4.1](docs/roadmap/TRACK_4_INTEGRATIONS.md#milestone-41-unified-notification-channels)
*   [ ] Implement an extensible `INotificationChannel` interface.
*   [ ] Write native notification channel providers for: Email, Slack, Microsoft Teams, and generic Webhooks.
*   [ ] Allow administrators to configure event-triggered notifications (e.g., dispatch a Slack alert when a new Critical risk is recorded or when an SLA breach occurs).

#### Milestone 4.2: Bi-directional Issue Sync
*Align security triage with development workflows.* — Spec: [docs/roadmap/TRACK_4_INTEGRATIONS.md § 4.2](docs/roadmap/TRACK_4_INTEGRATIONS.md#milestone-42-bi-directional-issue-sync)
*   [ ] Create a modular issue tracker integration core.
*   [ ] Support creating and linking developer tasks directly from vulnerability records to **Jira**, **GitHub Issues**, **GitLab Issues**, and **Azure DevOps Work Items**.
*   [ ] Implement bi-directional synchronization (e.g., closing a linked Jira ticket automatically transitions the NetRisk finding to `Mitigated` or schedules a re-verify task).

#### Milestone 4.3: Hardened Enterprise Authentication
*Secure access with standard enterprise single sign-on (SSO).* — Spec: [docs/roadmap/TRACK_4_INTEGRATIONS.md § 4.3](docs/roadmap/TRACK_4_INTEGRATIONS.md#milestone-43-hardened-enterprise-authentication)
*   [ ] Support SAML 2.0 and OIDC authentication protocols.
*   [ ] Implement automated User Provisioning via SCIM.
*   [ ] Support hardware-based authentication tokens (YubiKey, WebAuthn) for administrative accounts.

#### Milestone 4.4: Trend Micro Vision One Integration
*Integrate with Trend Micro Vision One for asset, risk, vulnerability, and posture synchronization.* — Spec: [docs/roadmap/TRACK_4_INTEGRATIONS.md § 4.4](docs/roadmap/TRACK_4_INTEGRATIONS.md#milestone-44-trend-micro-vision-one-integration)
*   [ ] Establish connection management with region-aware API keys and test connection utilities.
*   [ ] Automate computer inventory synchronization mapping endpoints from Trend Micro to NetRisk Hosts.
*   [ ] Ingest and map CVE vulnerabilities from at-risk devices, including virtual patching status detection.
*   [ ] Synchronize risk scores (0-100) and posture metrics to aggregate into the NetRisk entity-wide Cyber Risk Index.

#### Milestone 4.5: SecurityScorecard Integration
*Integrate with SecurityScorecard for domain-level cyber rating, factor scores, vulnerability, and issue synchronization.* — Spec: [docs/roadmap/TRACK_4_INTEGRATIONS.md § 4.5](docs/roadmap/TRACK_4_INTEGRATIONS.md#milestone-45-securityscorecard-integration)
*   [ ] Establish connection management with domain targeting and token authentication tests.
*   [ ] Automate posture synchronization retrieving overall grade, score, and the 10 risk factor details.
*   [ ] Ingest and map domain-level CVE vulnerabilities and potential exposures.
*   [ ] Synchronize active security issues and findings (missing SPF, ports, SSL, etc.) under custom categories.

---

### Track 5: Native Packaging & Release Engineering

This track automates artifact production, ensuring secure and seamless software distribution. Detailed, research-backed specifications for every milestone live in [docs/roadmap/TRACK_5_PACKAGING.md](docs/roadmap/TRACK_5_PACKAGING.md).

#### Milestone 5.1: Automated Code-Signing Pipelines
*Eliminate OS-level safety warnings and establish verified publisher trust.* — Spec: [docs/roadmap/TRACK_5_PACKAGING.md § 5.1](docs/roadmap/TRACK_5_PACKAGING.md#milestone-51-automated-code-signing-pipelines)
*   [ ] **Windows Authenticode:** Integrate automatic executable signing during the `PackageWindowsGUI` target execution inside Nuke (`build/Build.cs`).
*   [ ] **macOS Developer ID & Notarization:** Automate signing and notarization through Apple's notarization servers during the `PackageMacGUI` execution.

#### Milestone 5.2: Modern Native Installers
*Provide streamlined, native installation packages matching platform standards.* — Spec: [docs/roadmap/TRACK_5_PACKAGING.md § 5.2](docs/roadmap/TRACK_5_PACKAGING.md#milestone-52-modern-native-installers)
*   [ ] **Windows:** Compile native, silent-install `.msi` setups and modern sandboxed `.msix` containers.
*   [ ] **macOS:** Automate the assembly of drag-and-drop `.dmg` bundles.
*   [ ] **Linux:** Package and publish **Flatpak** and **Snap** containers to provide platform-agnostic, sandbox-isolated deployments.

---

### Track 6: Database Uniformization & Schema Health

This track standardizes the database schema (naming, relationships, indexing, types) and removes dead tables/columns — with zero data loss. The full multi-phase plan, including the per-phase migration strategy and risk analysis, lives in [docs/plano-uniformizacao-banco.md](docs/plano-uniformizacao-banco.md). Each milestone below has a detailed specification under [roadmap/track-6/](roadmap/track-6/).

#### Milestone 6.1: Upgrade Tooling & Preparation (Plan: Tool + Phase 0) (Completed)
*Build the safety net before touching the schema.* — Spec: [roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md](roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md)
*   [x] Extend the ConsoleClient `database` command with `upgrade-schema --phase <n> [--env homolog|prod] [--check] [--dry-run] [--yes] [--output]`: pre-flight checks, automatic backup, phase apply over the numbered-SQL `db_version` path, post-apply validations (index/FK/column-type/table/custom), a destructive-phase observation gate, and a `schema_upgrade_log` audit trail — all driven by the versioned manifest `src/ConsoleClient/DB/SchemaUpgradePhases.yaml`. (Apply runs over the existing numbered-SQL mechanism, not `Database.Migrate()`; EF migrations remain the source for generating that SQL.)
*   [x] Production baseline tooling via `database baseline`: row-count census of removal candidates (recommends drop vs archive), pending-migration + model-vs-snapshot divergence report, optional Markdown output. (The one-time *run* against the live prod DB, including the full dump, remains an ops step.)
*   [x] Document the target naming convention (snake_case tables/columns, `fk_`/`idx_`/`uq_` prefixes, UTC DATETIME, `tinyint(1)`, int+enum, no BLOB-for-text) in [CLAUDE.md](CLAUDE.md) so new entities are born compliant.
*   [x] Verified by `ServerServices.Tests` (unit) and `DAL.IntegrationTests` (Testcontainers MySQL, `Category=Integration`).

#### Milestone 6.2: Safe Fixes & Naming Uniformization (Plan: Phases 1–2) (Completed)
*Low-risk corrections and snake_case convergence — renames only, no drops.* — Spec: [roadmap/track-6/MILESTONE_6.2_SAFE_FIXES_NAMING.md](roadmap/track-6/MILESTONE_6.2_SAFE_FIXES_NAMING.md)
*   [x] **Phase 1 (`db_version` 64):** fixed invalid `0000-00-00` defaults (`mgmt_reviews.next_review` default dropped; `mitigations.last_update` → `CURRENT_TIMESTAMP`) and the index typos (`biometic`/`sequencial`/`optinal`).
*   [x] **Phase 1b (`db_version` 66):** boolean `tinyint(4)` → `tinyint(1)` for `comments.IsAnonymous` and `framework_controls.deleted` (C# `sbyte` → `bool` end-to-end).
*   [x] **Phase 2b (`db_version` 67):** snake_cased the last stray column `comments.IsAnonymous` → `is_anonymous`.
*   [x] **Phase 1c (`db_version` 68):** collation/charset unification — converted all 99 base tables to `utf8mb4` / `utf8mb4_unicode_ci` via `CONVERT TO` (wholesale `utf8mb3` → `utf8mb4`), so text columns can store 4-byte characters. Verified on MariaDB: no `utf8mb3` remains, data preserved, emoji round-trips.
*   [x] **Phase 2 (`db_version` 65):** renamed the 8 PascalCase tables (`Incidents`, `IncidentResponsePlan*`, `BiometricTransaction`, `FaceIDUsers`, `FixRequest`) and the hybrid camelCase columns (`vulnerabilities_to_actions`, `reports`, `hosts`, `messages`) to snake_case via `RenameTable`/`RenameColumn` — C# entities and DTOs unchanged (mapping via `ToTable`/`HasColumnName`).
*   [x] Applied through `database upgrade-schema --phase 1|2`; verified end-to-end against the real legacy schema on MariaDB in `DAL.IntegrationTests` (renames + row-count/value parity).

#### Milestone 6.3: Relationships & Indexing for Performance (Plan: Phases 3–4) (Completed)
*Every correlation column becomes a real, navigable, indexed foreign key.* — Spec: [roadmap/track-6/MILESTONE_6.3_RELATIONSHIPS_INDEXING.md](roadmap/track-6/MILESTONE_6.3_RELATIONSHIPS_INDEXING.md)
*   [x] **Phase 3 (`db_version` 69):** FK constraints + EF navigations for the orphan id columns (`Risk.Owner`/`Manager`/`SubmittedBy`, `FrameworkControl.ControlOwner`, `FrameworkControlTest.Tester` → `user`, all `ON DELETE SET NULL`), with dangling references logged to `schema_upgrade_orphans` before being nulled. Added `incidents.reported_by_id` (FK, free-text `ReportedBy` kept, best-effort name backfill); resolved the `IncidentToIncidentResponsePlan` join ambiguity; `Risk.ProjectId` flagged for 6.4 removal (no live `projects` table). *(Note: `FrameworkControl` has no `Tester` column — that was plan-doc drift; only `FrameworkControlTest.Tester` exists.)*
*   [x] **Phase 4 (`db_version` 70):** Sieve/query-justified hot-path indexes (vulnerabilities first/last detection, hosts status + registration_date, risks `status,submission_date` composite, user email); dropped the redundant `UNIQUE id` index on `framework_control_tests`; converted text-bearing BLOB columns to `varchar`/`TEXT` (C# `byte[]` → `string`) — `user.email` direct (UTF-8), legacy framework/permission BLOBs via a `latin1`→`utf8mb4` round-trip (cp1252 seed bytes).
*   [x] Applied through `database upgrade-schema --phase 3|4`; verified end-to-end on MariaDB in `DAL.IntegrationTests` (orphan cleanup ordering, `ON DELETE SET NULL`, FK indexing, BLOB→text encoding round-trip) + EF-model-metadata unit tests in `ServerServices.Tests`.

#### Milestone 6.4: Type Standardization & Dead Schema Removal (Plan: Phases 5–6) (Completed)
*Consistent temporal/status types, then staged removal of unused objects.* — Spec: [roadmap/track-6/MILESTONE_6.4_TYPES_DEAD_SCHEMA.md](roadmap/track-6/MILESTONE_6.4_TYPES_DEAD_SCHEMA.md)
*   [x] **Phase 5 (`db_version` 71):** migrated `risks.status` (varchar) to an int-backed enum via create-copy-coexist — added `risks.status_id` (`int`, `DAL.Enums.RiskStatus` + explicit `HasConversion<int>()`), backfilled from the known status strings (unmapped legacy values left `NULL`), legacy `status` retained for one release. Added an explicit `HasConversion<int>()` to `BiometricTransaction.TransactionResult` (model-only). *(The `ON UPDATE CURRENT_TIMESTAMP` temporal columns were intentionally left as-is — they are audit timestamps that should keep auto-updating, so no temporal-type sweep was needed.)*
*   [x] **Phase 6a (`db_version` 72):** deprecated the 23 unreferenced tables (rename to `zz_deprecated_*`, unmap from EF) and unmapped the orphan columns `risks.regulation`/`risks.project_id` — reversible, data preserved. Confirmed `failed_login_attempts`/`user_pass_history` carry no live lockout/reuse logic (`UserPassReuseHistory` is the live one, not removed). Manifest `removalCandidates`/census corrected to the live snake_case table names.
*   [x] **Phase 6b (`db_version` 73):** after the observation window, dropped the 23 `zz_deprecated_*` tables and the orphan columns, gated by the upgrade tool (requires the aged `6a` Success in `schema_upgrade_log` + `--yes`; automatic backup is the recovery dump). The legacy `risks.status` is **not** dropped here — its `status_id` replacement must coexist a release first (a future phase removes it).

---

### Track 7: Security Review & Hardening

A full, end-to-end security review of the codebase across every tier (API, ServerServices, DAL, ClientServices, GUIClient, BackgroundJobs, WebSite, Plugins), producing a prioritized findings register and a remediation backlog. As a security/GRC product, NetRisk should hold itself to the standards it helps customers enforce. The output of 7.1 feeds concrete, scheduled work into 7.2–7.5. Detailed, research-backed specifications for every milestone live in [docs/roadmap/TRACK_7_SECURITY.md](docs/roadmap/TRACK_7_SECURITY.md).

#### Milestone 7.1: Comprehensive Security Audit
*Establish a baseline by systematically reviewing the code against a recognized standard.* — Spec: [docs/roadmap/TRACK_7_SECURITY.md § 7.1](docs/roadmap/TRACK_7_SECURITY.md#milestone-71-comprehensive-security-audit)
*   [ ] Threat-model the request flow (GUIClient → ClientServices → API → ServerServices → DAL) and document trust boundaries, data flows, and assets.
*   [ ] Audit the codebase against the OWASP ASVS / Top 10, covering: authN/authZ, input validation, injection (SQL/EF, command, path), secrets handling, crypto usage, deserialization, SSRF, and file-upload/import paths (Nessus and future scanner importers).
*   [ ] Produce a prioritized findings register (severity, affected tier, exploitability, proposed fix) checked into [docs/security/](docs/security/), and triage each finding into the milestones below.
*   [ ] Run the repo's own `/security-review` over the current branch as a recurring gate and capture the baseline report.

#### Milestone 7.2: Dependency & Supply-Chain Security
*Know and control what ships in the binaries and submodules.* — Spec: [docs/roadmap/TRACK_7_SECURITY.md § 7.2](docs/roadmap/TRACK_7_SECURITY.md#milestone-72-dependency--supply-chain-security)
*   [ ] Enable automated dependency scanning (Dependabot / `dotnet list package --vulnerable`) across all projects and the `libs/` submodules.
*   [ ] Generate and publish an SBOM as part of the Nuke `Package*` targets.
*   [ ] Pin and verify submodule provenance (`NessusParser`, `Aura.UI`, `netrisk-plugin-sdk`, `reliable-rest-client-wrapper`); document an upgrade/patching policy.

#### Milestone 7.3: AuthN/AuthZ & Secrets Hardening
*Close gaps in identity, access control, and secret management.* — Spec: [docs/roadmap/TRACK_7_SECURITY.md § 7.3](docs/roadmap/TRACK_7_SECURITY.md#milestone-73-authnauthz--secrets-hardening)
*   [ ] Verify every API controller enforces authorization (no unintentionally anonymous endpoints) and that role/entity scoping is applied consistently in `ServerServices`.
*   [ ] Audit token issuance/validation, session lifetime, password and FaceID/biometric flows, and lockout/brute-force protections.
*   [ ] Confirm no secrets are committed; standardize on user-secrets/environment/secret-store and document rotation.

#### Milestone 7.4: Data Protection & Transport Security
*Protect data in transit and at rest.* — Spec: [docs/roadmap/TRACK_7_SECURITY.md § 7.4](docs/roadmap/TRACK_7_SECURITY.md#milestone-74-data-protection--transport-security)
*   [ ] Enforce TLS configuration and certificate validation on all client↔server and outbound integration calls.
*   [ ] Review encryption of sensitive columns and uploaded files at rest; validate hashing/KDF choices.
*   [ ] Harden CORS, security headers, and cookie flags on the `API` and `WebSite`.

#### Milestone 7.5: Continuous Security in CI/CD
*Make security verification automatic and non-regressing.* — Spec: [docs/roadmap/TRACK_7_SECURITY.md § 7.5](docs/roadmap/TRACK_7_SECURITY.md#milestone-75-continuous-security-in-cicd)
*   [ ] Add SAST and secret-scanning steps to the build/CI pipeline that fail on new high-severity findings.
*   [ ] Establish a coordinated vulnerability disclosure policy (`SECURITY.md`) and an internal triage SLA for reported issues.
*   [ ] Schedule periodic re-audits (each minor release) and track remediation burn-down against the 7.1 findings register.

---

## 🔮 Ideas & Future Explorations

The following concepts are under consideration and are not yet committed to any active milestone track:

- **Mobile Companion App:** Lightweight iOS and Android viewer for executive incident tracking and risk sign-off.
- **Real-Time Collaboration:** Synchronized document editing for Incident Response Plans and joint risk assessments.
- **AI-Assisted Risk Scoring:** Large Language Model integrations to automatically analyze vulnerabilities, correlate threat intelligence, and propose mitigation strategies.
