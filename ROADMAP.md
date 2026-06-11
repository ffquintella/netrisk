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

This track focuses on the GRC (Governance, Risk, and Compliance) core features, incident workflows, and data output templates.

#### Milestone 2.1: Advanced Reporting Engine
*Enable rich, customizable risk reports and automated exports.*
*   [ ] Introduce customizable report templates allowing organizations to define their logo, styling, and sections.
*   [ ] Support scheduled exports of dashboards, compliance grids, and open incidents via email.
*   [ ] Add PDF, CSV, and Excel export targets for all statistics tables.

#### Milestone 2.2: Enhanced Assessments Workflow
*Optimize how organizations collect, triage, and score vulnerability and compliance questionnaires.*
*   [ ] Build an interactive, paged assessment viewer supporting nested questions, conditional show/hide logic, and rich-text explanations.
*   [ ] Implement progress trackers and draft auto-saving to prevent data loss.
*   [ ] Support importing assessment templates from industry standards (e.g., NIST, ISO 27001) via JSON/Excel.

#### Milestone 2.3: Multi-Entity & Multi-Tenant Support
*Enable managed risk monitoring across distinct organizational subdivisions.*
*   [ ] Segregate assets, risks, and vulnerabilities by "Business Entity".
*   [ ] Introduce role-based scoped access (e.g., users can only view risks belonging to their assigned Business Entity).
*   [ ] Add a central Master Dashboard for administrators to view aggregated posture metrics across all entities.

#### Milestone 2.4: Incident Response Automation (IRP)
*Close the loop on incident management with active workflows.*
*   [ ] Create customizable Incident Response Plan (IRP) templates.
*   [ ] Support automatic task generation and assignment when an incident of a specific type is created.
*   [ ] Build task-dependency Gantt trackers to visualize critical paths during emergency response.

---

### Track 3: Vulnerability Aggregation & Finding Lifecycle (ASPM)

This track bridges GRC with Application Security Posture Management (ASPM), allowing organizations to ingest, deduplicate, and triage automated scanner outputs.

#### Milestone 3.1: Extensible Scanner Importers
*Provide a unified plugin interface to feed findings from any security tool.*
*   [ ] Define a generic `IVulnerabilityImporter` plugin contract in the `netrisk-plugin-sdk` (input: report stream; output: normalized `Vulnerability` + `Host` + `CVEDetail` models).
*   [ ] Refactor the legacy, built-in Nessus parser onto the new extensible contract.
*   [ ] Write native importers for: OWASP ZAP, Trivy, Semgrep, OpenVAS, Burp Suite, Snyk, Grype, and GitHub Dependabot.
*   [ ] API Modernization: Generalize `POST /vulnerabilities/import/{importerName}/{fileId}` with dynamic importer discovery via `IPluginsService`.
*   [ ] GUIClient Modernization: Build a dynamic importer selector inside the vulnerability import dialog.

#### Milestone 3.2: Finding Lifecycle & Audit Trails
*Establish a rigorous triage state-machine for individual findings.*
*   [ ] Add granular lifecycles: `Active`, `Verified`, `FalsePositive`, `OutOfScope`, `Duplicate`, `RiskAccepted`, `Mitigated`.
*   [ ] Implement an audit logging mechanism to track state transitions (who, when, why) on individual findings.
*   [ ] Introduce a dedicated `RiskAcceptance` entity containing expiration dates, authorizing managers, and business justifications.
*   [ ] Implement a background job (Hangfire) to automatically re-open expired risk-acceptance agreements.

#### Milestone 3.3: Intelligent Deduplication Engine
*Prevent database bloat from repeated automated scans using pluggable matching strategies.*
*   [ ] Extend the default hash-based lookup with modular strategies: `HashBased`, `UniqueIdFromTool`, `LegacyHashCode`, `Custom`.
*   [ ] Ensure importing updates existing open findings rather than creating duplicates, while maintaining historical scan logs.
*   [ ] Build an administration UI to toggle and configure deduplication heuristics per scanner type.

#### Milestone 3.4: SLA Tracking & Aging
*Enforce compliance boundaries with automated service level agreements (SLAs).*
*   [ ] Introduce `SlaConfiguration` schemas defining max triage/remediation days per severity (Critical, High, Medium, Low).
*   [ ] Implement computed fields tracking `SlaDueDate` and `DaysOverdue` on open findings.
*   [ ] Automate email and webhook breach notifications as target deadlines approach.

#### Milestone 3.5: CI/CD-First Integration API
*Integrate NetRisk directly into automated build pipelines.*
*   [ ] Implement scoped, non-interactive API-token authentication optimized for CI runners.
*   [ ] Support bulk, idempotent direct upload endpoints: `POST /vulnerabilities/import/{importer}` accepting raw payloads.
*   [ ] Publish official, copy-pasteable GitHub Actions, GitLab CI, and Azure Pipelines task recipes.
*   [ ] Support exit-code gating patterns (e.g., fail builds if new Critical vulnerabilities are imported).

---

### Track 4: Integrations & Notification Channels

This track focuses on connecting NetRisk with external messaging platforms, issue trackers, and enterprise identity systems.

#### Milestone 4.1: Unified Notification Channels
*Broadcast alerts to platforms where security and engineering teams already communicate.*
*   [ ] Implement an extensible `INotificationChannel` interface.
*   [ ] Write native notification channel providers for: Email, Slack, Microsoft Teams, and generic Webhooks.
*   [ ] Allow administrators to configure event-triggered notifications (e.g., dispatch a Slack alert when a new Critical risk is recorded or when an SLA breach occurs).

#### Milestone 4.2: Bi-directional Issue Sync
*Align security triage with development workflows.*
*   [ ] Create a modular issue tracker integration core.
*   [ ] Support creating and linking developer tasks directly from vulnerability records to **Jira**, **GitHub Issues**, **GitLab Issues**, and **Azure DevOps Work Items**.
*   [ ] Implement bi-directional synchronization (e.g., closing a linked Jira ticket automatically transitions the NetRisk finding to `Mitigated` or schedules a re-verify task).

#### Milestone 4.3: Hardened Enterprise Authentication
*Secure access with standard enterprise single sign-on (SSO).*
*   [ ] Support SAML 2.0 and OIDC authentication protocols.
*   [ ] Implement automated User Provisioning via SCIM.
*   [ ] Support hardware-based authentication tokens (YubiKey, WebAuthn) for administrative accounts.

---

### Track 5: Native Packaging & Release Engineering

This track automates artifact production, ensuring secure and seamless software distribution.

#### Milestone 5.1: Automated Code-Signing Pipelines
*Eliminate OS-level safety warnings and establish verified publisher trust.*
*   [ ] **Windows Authenticode:** Integrate automatic executable signing during the `PackageWindowsGUI` target execution inside Nuke (`build/Build.cs`).
*   [ ] **macOS Developer ID & Notarization:** Automate signing and notarization through Apple's notarization servers during the `PackageMacGUI` execution.

#### Milestone 5.2: Modern Native Installers
*Provide streamlined, native installation packages matching platform standards.*
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
*   [ ] **Collation unification (deferred — own pass):** a survey shows it is a wholesale `utf8mb3` → `utf8mb4` conversion of ~88 tables (not a few mixed columns), with InnoDB index-key-length risk; scoped as a separate focused migration rather than a Phase 1 leftover.
*   [x] **Phase 2 (`db_version` 65):** renamed the 8 PascalCase tables (`Incidents`, `IncidentResponsePlan*`, `BiometricTransaction`, `FaceIDUsers`, `FixRequest`) and the hybrid camelCase columns (`vulnerabilities_to_actions`, `reports`, `hosts`, `messages`) to snake_case via `RenameTable`/`RenameColumn` — C# entities and DTOs unchanged (mapping via `ToTable`/`HasColumnName`).
*   [x] Applied through `database upgrade-schema --phase 1|2`; verified end-to-end against the real legacy schema on MariaDB in `DAL.IntegrationTests` (renames + row-count/value parity).

#### Milestone 6.3: Relationships & Indexing for Performance (Plan: Phases 3–4)
*Every correlation column becomes a real, navigable, indexed foreign key.* — Spec: [roadmap/track-6/MILESTONE_6.3_RELATIONSHIPS_INDEXING.md](roadmap/track-6/MILESTONE_6.3_RELATIONSHIPS_INDEXING.md)
*   [ ] Add FK constraints + EF navigations for orphan id columns (`Risk.Owner`/`Manager`/`SubmittedBy`, `FrameworkControl.ControlOwner`/`Tester`, …), with logged orphan-data cleanup beforehand.
*   [ ] Index hot filter/sort columns validated against real service queries and Sieve filters; remove redundant indexes; convert text-bearing BLOB columns to proper `varchar`/`TEXT`.

#### Milestone 6.4: Type Standardization & Dead Schema Removal (Plan: Phases 5–6)
*Consistent temporal/status types, then staged removal of unused objects.* — Spec: [roadmap/track-6/MILESTONE_6.4_TYPES_DEAD_SCHEMA.md](roadmap/track-6/MILESTONE_6.4_TYPES_DEAD_SCHEMA.md)
*   [ ] Standardize `created_at`/`updated_at` as UTC DATETIME; migrate `risks.status` (varchar) to an int-backed enum via create-copy-coexist-remove.
*   [ ] Deprecate the ~24 unreferenced tables (rename to `zz_deprecated_*`, unmap from EF), observe for one release, then archive dumps and drop — gated by the upgrade tool.

---

### Track 7: Security Review & Hardening

A full, end-to-end security review of the codebase across every tier (API, ServerServices, DAL, ClientServices, GUIClient, BackgroundJobs, WebSite, Plugins), producing a prioritized findings register and a remediation backlog. As a security/GRC product, NetRisk should hold itself to the standards it helps customers enforce. The output of 7.1 feeds concrete, scheduled work into 7.2–7.5.

#### Milestone 7.1: Comprehensive Security Audit
*Establish a baseline by systematically reviewing the code against a recognized standard.*
*   [ ] Threat-model the request flow (GUIClient → ClientServices → API → ServerServices → DAL) and document trust boundaries, data flows, and assets.
*   [ ] Audit the codebase against the OWASP ASVS / Top 10, covering: authN/authZ, input validation, injection (SQL/EF, command, path), secrets handling, crypto usage, deserialization, SSRF, and file-upload/import paths (Nessus and future scanner importers).
*   [ ] Produce a prioritized findings register (severity, affected tier, exploitability, proposed fix) checked into [docs/security/](docs/security/), and triage each finding into the milestones below.
*   [ ] Run the repo's own `/security-review` over the current branch as a recurring gate and capture the baseline report.

#### Milestone 7.2: Dependency & Supply-Chain Security
*Know and control what ships in the binaries and submodules.*
*   [ ] Enable automated dependency scanning (Dependabot / `dotnet list package --vulnerable`) across all projects and the `libs/` submodules.
*   [ ] Generate and publish an SBOM as part of the Nuke `Package*` targets.
*   [ ] Pin and verify submodule provenance (`NessusParser`, `Aura.UI`, `netrisk-plugin-sdk`, `reliable-rest-client-wrapper`); document an upgrade/patching policy.

#### Milestone 7.3: AuthN/AuthZ & Secrets Hardening
*Close gaps in identity, access control, and secret management.*
*   [ ] Verify every API controller enforces authorization (no unintentionally anonymous endpoints) and that role/entity scoping is applied consistently in `ServerServices`.
*   [ ] Audit token issuance/validation, session lifetime, password and FaceID/biometric flows, and lockout/brute-force protections.
*   [ ] Confirm no secrets are committed; standardize on user-secrets/environment/secret-store and document rotation.

#### Milestone 7.4: Data Protection & Transport Security
*Protect data in transit and at rest.*
*   [ ] Enforce TLS configuration and certificate validation on all client↔server and outbound integration calls.
*   [ ] Review encryption of sensitive columns and uploaded files at rest; validate hashing/KDF choices.
*   [ ] Harden CORS, security headers, and cookie flags on the `API` and `WebSite`.

#### Milestone 7.5: Continuous Security in CI/CD
*Make security verification automatic and non-regressing.*
*   [ ] Add SAST and secret-scanning steps to the build/CI pipeline that fail on new high-severity findings.
*   [ ] Establish a coordinated vulnerability disclosure policy (`SECURITY.md`) and an internal triage SLA for reported issues.
*   [ ] Schedule periodic re-audits (each minor release) and track remediation burn-down against the 7.1 findings register.

---

## 🔮 Ideas & Future Explorations

The following concepts are under consideration and are not yet committed to any active milestone track:

- **Mobile Companion App:** Lightweight iOS and Android viewer for executive incident tracking and risk sign-off.
- **Real-Time Collaboration:** Synchronized document editing for Incident Response Plans and joint risk assessments.
- **AI-Assisted Risk Scoring:** Large Language Model integrations to automatically analyze vulnerabilities, correlate threat intelligence, and propose mitigation strategies.
