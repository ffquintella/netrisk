# Change Log
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## [2.13.1] - 2026-06-17

### Fixed
- **`ServerServices.Tests` no longer fails to compile**: `ServiceBehaviorInMemoryTest` constructed `ReportsService` with the old three-argument signature after the QuestPDF rendering dependency was added; it now resolves the already-registered `IQuestPdfRenderingService` from the test DI container, so the whole test project (including the assessment dry-run import tests) builds and runs again. (`ServiceBehaviorInMemoryTest`)

## [2.13.0] - 2026-06-17

### Added
- **Interactive paged assessment-run viewer (GUIClient)** — completes Milestone 2.2: opening a run now launches a dedicated viewer with a left-rail page list (per-page completion state), previous/next navigation, and a final review page that lists unanswered required questions with jump-to-page links. Each question renders its rich-text `ExplanationMarkdown` help (via a new lightweight `MarkdownPresenter` control), nested sub-questions are indented, and answers are picked from the question's predefined options. Conditional show/hide is enforced server-side — the viewer fetches each page's visible questions through `GET /Assessments/runs/{runId}/pages/{pageNumber}/questions` and re-evaluates after every save. Draft answers auto-save with a ~2s debounce (`PATCH /Assessments/runs/{runId}/answers`), show a "saved at HH:mm" indicator, drive a live progress bar, and resume at the last page on reopen (`GET …/answers/draft`). Submitted runs open read-only. (`AssessmentRunViewerViewModel`, `AssessmentRunQuestionViewModel`, `AssessmentRunPageViewModel`, `AssessmentRunViewer.axaml`, `MarkdownPresenter`)
- **Assessment template import dialog with dry-run validation (GUIClient + server)**: an "Import template" button on the Assessments view opens a dialog to pick a JSON or Excel (`.xlsx`) template. The dialog **dry-runs first** — it calls a new `POST /Imports/assessment/preview` endpoint that validates the file and returns a summary (page/question counts, warnings, and row-level errors) **without writing anything**; the Import button stays disabled until the preview is valid. Invalid files import nothing and show row-level reasons. On confirm, the same file is committed via `POST /Imports/assessment`. (`AssessmentImportDialogViewModel`, `AssessmentImportDialog.axaml`, `ImportsController.PreviewAssessment`, `ImportsService`, `AssessmentImportPreview`)
- **Bundled assessment starter packs (GUIClient)**: the import dialog offers one-click **NIST CSF 2.0** and **ISO/IEC 27001:2022 Annex A** question sets (paged, with rich-text explanations), shipped as Avalonia assets under `Assets/AssessmentTemplates/`. Questions are paraphrased from the control outcomes (not reproduced verbatim) and serve as scaffolds — answer options are added afterward in the Questions tab. (`nist-csf-2.0.json`, `iso-27001-2022-annex-a.json`)
- **Dry-run validation in the import service (server)**: `IImportsService` gained `PreviewAssessmentFromJsonAsync`/`PreviewAssessmentFromExcelAsync`; parsing/validation is now shared between preview and commit, and committing an invalid template throws before any DB write (so invalid files import nothing). Covered by new `ImportsServiceInMemoryTest` cases. (`ImportsService`, `IImportsService`)
- **ClientServices REST methods for the assessment workflow**: `GetVisibleQuestionsForPageAsync`, `GetDraftAnswersAsync`, `SaveDraftAnswerAsync`, `PreviewTemplateAsync` and `ImportTemplateAsync` were added to `IAssessmentsService`/`AssessmentsRestService` to back the viewer and import dialog. (`AssessmentsRestService`)
- **File reports can now be generated from report templates (GUIClient + server)**: the "Create Report" dialog's report-type dropdown now lists every report template alongside the two built-in reports, so a template-based PDF can be produced as a regular file report (not only as a scheduled email export). Picking a template creates a report whose parameters carry the template id; the server renders the latest template version through the QuestPDF engine (same data source as scheduled exports) and stores the resulting PDF. (`CreateReportDialogViewModel`, `ReportTypeOption`, `ReportDialogResult`, `FileReportsViewModel`, `ReportParameters`, `ReportsService`)

### Changed
- **"Create Report" dialog restyled to the standard dialog visual identity (GUIClient)**: centered content, centered button bar with `IsDefault` on Create, a named window and consistent margins — matching the other edit dialogs (e.g. `EditEntityDialog`) instead of its bespoke left-aligned layout. (`CreateReportDialog.axaml`)

## [2.12.8] - 2026-06-17

### Changed
- **Report Template / Schedule Manager windows now follow the standard master/detail schema (GUIClient)**: the two manager windows were rebuilt to use the same layout and styling as the rest of the app (e.g. `IncidentsView`) instead of their bespoke schema — a `header`/`header2` title and section headers, a bottom control bar of `subButton`/`type2`/`type3` action buttons (Create/Update/Test/Delete) in place of the top `toolbar` border, a `GridSplitter` between list and detail, a `form_label` + `form_text2`/`form_long_text` detail grid (guarded by selection), dates rendered through `DateToFormatedStringConverter`, and the standard `footer`. (`ReportTemplateManagerWindow.axaml`, `ReportScheduleManagerWindow.axaml`)

### Fixed
- **Nullable-safety in the report manager view-models (GUIClient)**: `SelectedTemplate`/`SelectedSchedule` are now nullable and the Update/Delete/Test commands no-op when nothing is selected, avoiding a null-dereference on an empty selection. (`ReportTemplateManagerViewModel`, `ReportScheduleManagerViewModel`)
- **Deprecated `Watermark` replaced with `PlaceholderText` on `TextBox` (GUIClient)**: in the Edit Report Schedule dialog and the Risks panel filter. Dialog-result DTOs for report template/schedule editing now default their string properties to `string.Empty`. (`EditReportScheduleDialog.axaml`, `RisksPanelView.axaml`, `EditReportTemplateDialogResult`, `EditReportScheduleDialogResult`)

## [2.12.7] - 2026-06-17

### Fixed
- **Could not select a version in the Edit Report Schedule dialog (GUIClient)**: the dialog populates the "Versão" dropdown from the selected template's `Versions` navigation collection, but `GET /ReportTemplates` only eager-loaded `Owner`, so every template arrived with an empty `Versions` collection and the dropdown was always empty (leaving Save disabled). The endpoint now `.Include(t => t.Versions)` like `GetById` already did. (`ReportTemplatesController.GetAll`)

## [2.12.6] - 2026-06-17

### Fixed
- **Creating a child entity type (e.g. `organizationUnit`) failed with a server 500 instead of validation (GUIClient)**: definitions that require a parent (a mandatory property whose default value is the `"Parent"` sentinel) were submitted without a parent, and the server rejected them with `Parent is required` surfaced only as a generic `InternalServerError`. The add-entity flow now validates up front and shows a clear "select a parent entity" message (new `ParentRequiredMSG` localization) instead of committing.
- **Assessment run could not be saved — "Could not parse entity id from selection:" (GUIClient)**: the Entity `AutoCompleteBox` in the assessment-run dialog was missing its `SelectedItem` binding (the Host box had one), so the selected entity never reached `SelectedEntityName`. Saving then failed to parse the (empty) entity. Added `SelectedItem="{Binding SelectedEntityName}"`.
- **Opening dialogs crashed with "No service for type … has been registered" (GUIClient)**: a startup refactor switched `DialogService` to resolve dialog view-models from the DI container (`Program.ServiceProvider.GetRequiredService`) instead of instantiating them reflectively, but the dialog view-models were never registered. This crashed core flows such as **adding an entity** (`EditEntityDialogViewModel`) and the Reports **"+"** button (`CreateReportDialogViewModel`), among others. `GeneralServicesBootstrapper` now registers **every** concrete `DialogViewModelBase<>`-derived view-model by reflection, so all dialogs resolve (and future ones are covered automatically).
- **Report Template / Schedule Manager windows didn't follow the platform visual identity (GUIClient)**: the two manager windows rendered raw, unstyled Avalonia controls (plain grey buttons, no theming) against the dark app. They now match the rest of the GUI — a themed toolbar with Material icon buttons and tooltips, styled section headers (`header`/`header2`), a labelled detail panel (`label`/`formData`), and a footer bar. (`ReportTemplateManagerWindow.axaml`, `ReportScheduleManagerWindow.axaml`)

## [2.12.5] - 2026-06-17

### Added
- **Report-template designer (GUIClient)**: the template editor is no longer a raw-JSON form. It now has a structured section editor (add / remove / reorder Title, Text and Table sections), branding controls (primary/secondary color with live swatches, font, and logo upload), a **"New from preset"** picker shipping three built-in starters (Executive Risk Summary, Vulnerability Posture, Incident Review), a **"Save as copy"** action, and a **live rendered PDF preview** pane. Preview is served by a new `POST /ReportTemplates/preview` endpoint that renders the first page to a PNG with sample data via `QuestPdfRenderingService.RenderPreviewImageAsync` (exposed client-side through `IReportTemplatesService.RenderPreviewAsync`).
- **Scheduled-export configuration screen (GUIClient)**: the schedule editor replaces the raw cron string and recipients-JSON textboxes with a frequency builder (Daily / Weekly / Monthly + time + day + timezone, compiled to/parsed from a 5-field cron) and a recipient-list editor. The schedule manager list now surfaces each schedule's **last run time and status**, and a test run refreshes that status.
- **Export actions on the Reports views (GUIClient)**: the Risk Review table and the Risks-vs-Costs, Impact-vs-Probability, Entities-Risks and Vulnerabilities-by-Time charts gained an **Export** button. Export is client-side ("what you see is what you export") to CSV (UTF-8 BOM, formula-injection-escaped) and typed Excel (ClosedXML) via the new `Tools.GridDataExporter` helper.

## [2.12.4] - 2026-06-17

### Changed
- **GUIClient export controls**: replaced the separate PDF/CSV/Excel toolbar buttons on the Risks, Vulnerabilities, Hosts, and Incidents views with a single **Export** button that opens a modal dialog to pick the format. The export icon buttons also now follow the standard view toolbar look-and-feel (previously the Incidents/Hosts export buttons rendered as default unstyled buttons). Format selection lives in the shared `Tools.ExportFileSaver.PickFormatAsync` helper.

### Fixed
- **Unreadable PDF exports with many columns**: the default report layout dumped every entity property into a portrait A4 grid, squeezing ~28 columns into slivers that wrapped one character at a time. PDF reports now render in **landscape**, column headers are humanized (`ReportedByEntity` → `Reported By Entity`) so they wrap on word boundaries, and when a report has more columns than fit a readable grid (> 9) it automatically switches to a per-record **card layout** (label/value pairs, two per row) instead of an unreadable wide table. Narrow, column-selected templates keep the grid. (`QuestPdfRenderingService`)
- **GUIClient crash when saving with a malformed entity/host selection**: clicking **Save** in the assessment-run dialog threw `IndexOutOfRangeException` (crashing the whole app) when the entity field didn't contain the expected `Name (id)` format. Hardened the `Name (id)` parsing behind a shared, exception-free `Tools.String.LabelIdParser` helper and applied it across all affected GUI editors (assessment run, edit vulnerability, edit risk, entities-risks report, entity form) — invalid selections now log and abort gracefully instead of crashing, and names that themselves contain parentheses are parsed correctly.

## [2.12.3] - 2026-06-17

### Added
- Implemented the GUI for the Advanced Reporting Engine, including:
  - A report-template designer to create, update, and delete report templates.
  - A scheduled-export configuration screen to manage scheduled reports.
  - PDF, CSV, and Excel export actions on the Risks, Vulnerabilities, Hosts, and Incidents views.

## [2.12.2] - 2026-06-16

### Fixed
- **GUIClient assessment question editor**: corrected the window layout (fixed oversized/empty window, stretched the question box and reworked the answer-edit row so inputs and action buttons align), added hover tooltips to all answer/question buttons, and made **Guardar** commit an answer still being edited in the side fields before saving — previously that in-progress edit was silently discarded.
- **GUIClient assessment questions grid**: top-aligned the `ID` and `Ações` columns so their cells line up (the question text was bottom-aligned while the ID was centered).
- **GUIClient incident response plan window**: the per-attachment Download/Delete buttons rendered as blank squares because their icons had no explicit size inside the small buttons — sized the icons, enlarged the buttons, and added Download/Delete/Add tooltips.
- **GUIClient mitigation and management-review editors**: fixed both window layouts — replaced the contradictory `SizeToContent.WidthAndHeight` + fixed size with height-to-content sizing, stretched the right-hand text fields so they no longer overflow the window edge, removed the dead space at the bottom, and made the Save/Cancel buttons span a visible bottom row.

## [2.12.1] - 2026-06-16

### Fixed
- Authentication crashed for every user (`Table 'netrisk.user_entity_roles' doesn't exist`) because the multi-entity scoped roles feature shipped in 2.11.0 without its database migration. Added the missing migration `AddUserEntityRoles` and the corresponding numbered SQL (`DB/Structure/74.sql` + `DB/Data/74.sql`, `targetVersion` → 74), which also creates the other drifted tables/columns introduced alongside it (Reports redesign, IRP templates, assessment-run answers and `entity_id` scoping columns).

## [2.12.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.4 (Incident Response Automation - IRP)**: Implemented customizable Incident Response Plan (IRP) templates and automated task compilation/assignee notifications matching SOAR playbooks.
  - Created `IrpTemplate` and `IrpTemplateTask` database models under `DAL`, mapped via Fluent API configurations in `NRDbContext` with cascade deletion rules.
  - Implemented the `IrpAutomationService` workflow matching engine to automatically instantiate IRPs and tasks from blueprints when a matching incident is created.
  - Added support for dynamic relative due date offsets (e.g. T+4h) and human-in-the-loop task approval gates (`requires_confirmation` status proposed).
  - Integrated the automation trigger directly inside the `IncidentsService.CreateAsync` pipeline with non-conflicting DbContext scoping.
  - Created the REST-compliant `IrpTemplatesController` exposing `/IrpTemplates` CRUD endpoints.
  - Added full test coverage in `IrpAutomationServiceInMemoryTest` achieving 100% success.

## [2.11.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.3 (Multi-Entity & Multi-Tenant Support)**: Implemented data segregation by "Business Entity" and enforced role-based scoped access (RBAC) across assets, risks, and vulnerabilities.
  - Added `EntityId` FKs and navigations to core entities `Risk`, `Host`, `Incident`, and `Assessment` under `DAL` (where `Vulnerability` already has `EntityId`), mapped via Fluent API configurations in `NRDbContext`.
  - Created the `UserEntityRole` model to link users, entities, and roles, supporting active audit soft-deletion (`revoked_at` column).
  - Extended the authentication handlers `JwtAuthenticationHandler` and `BasicAuthenticationHandler` to query active user-entity assignments and inject them as `entity_id` and `scope` claims.
  - Developed the generic static helper `ApplyEntityScope` under `ServerServices` to dynamically filter queryable datasets based on user claims.
  - Integrated dynamic scoping directly into `RisksService` (including `GetAllAsync` and `GetUserRisks` sync query) to restrict dataset visibility at the service layer.
  - Created `UserAccessController` to manage user-entity-role assignments (Get, Assign, Revoke).
  - Added full integration test coverage in `MultiEntityScopedAccessTest` verifying user-scoped isolation and global admin bypass with 100% success.

## [2.10.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.2 (Enhanced Assessments Workflow)**: Implemented the backend database structures, visibility algorithms, auto-saving logic, and external template parsers for GRC assessments.
  - Extended `AssessmentQuestion` with ParentQuestionId (nesting), PageNumber (pagination), ConditionJson (rules), and ExplanationMarkdown (help text).
  - Extended `AssessmentRun` with ProgressPercentage and CurrentPageIndex.
  - Created the `AssessmentRunAnswer` model under `DAL` to support saving in-progress draft responses.
  - Implemented the on-the-fly conditional evaluation algorithm `GetVisibleQuestionsForPageAsync` inside `AssessmentsService` supporting 'equals', 'notempty', and 'in' logic operators.
  - Implemented `SaveDraftAnswerAsync` to securely upsert in-progress user responses.
  - Developed `ImportsService` supporting template importing from standard JSON files and Excel worksheets (NIST / ISO 27001) using ClosedXML.
  - Created `ImportsController` exposing the `/Imports/assessment` upload endpoint and added REST routes for auto-saving drafts and visibility checks in `AssessmentsController`.
  - Added comprehensive test coverage in `AssessmentsServiceGapInMemoryTest` and `ImportsServiceInMemoryTest` achieving 100% success.

## [2.9.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.1 (Phase 3: Scheduled GRC Reports)**: Implemented scheduled report runs, automated PDF compiles, and email dispatches with PDF attachments.
  - Added the `ReportSchedule` database model under `DAL`, mapped via Fluent API configurations in `NRDbContext` with cascade deletion rules.
  - Implemented the `ScheduledReportJob` background worker under `ServerServices` to generate dynamic PDF summaries of incidents and send attachment-bearing emails via `FluentEmail` in memory.
  - Developed the `ReportSchedulesController` in the `API` project with CRUD endpoints on `/ReportSchedules`, supporting active integration with the **Hangfire** scheduler (`RecurringJob.AddOrUpdate` / `BackgroundJob.Enqueue`).
  - Added full test coverage in `ScheduledReportJobInMemoryTest` using NSubstitute.
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.1 (Phase 2: Customizable Report Templates)**: Implemented the backend database structures, APIs, and fluid QuestPDF rendering engine for dynamic customizable templates.
  - Added `ReportTemplate` and `ReportTemplateVersion` database models under `DAL`, mapped via Fluent API configurations in `NRDbContext` following standard conventions.
  - Implemented the REST-compliant `ReportTemplatesController` with endpoints `GET`, `POST`, `PUT`, `DELETE` on `/ReportTemplates` to manage report templates with versioned layout and branding histories.
  - Introduced the modern **QuestPDF** library (v2026.6.0) under `ServerServices` and integrated the `IQuestPdfRenderingService`/`QuestPdfRenderingService` engine.
  - Configured QuestPDF for dynamic JSON layouts supporting logos, colors, customizable typography, title sections, body text, and complex table layouts.
  - Integrated QuestPDF directly into the main `ExportService` so standard PDF exports automatically use the brand-new, ultra-modern templates.
  - Added 100% test coverage in `QuestPdfRenderingServiceInMemoryTest`.
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.1 (Phase 1: Core Export Service)**: Implemented the backend server-side export engine including the `IExportService` contract and its concrete implementation `ExportService`.
  - Added support for generating CSV files safely against Formula Injection (CWE-1236) using UTF-8 BOM.
  - Added support for generating Excel (XLSX) spreadsheets using ClosedXML with strongly-typed columns and custom formatting.
  - Added a placeholder PDF table exporter using PDFsharp/MigraDoc with global FontResolver integration.
  - Implemented the generic `ExportController` with endpoint `GET /Export/{format}` allowing Sieve-filtered export of major entities (`Risk`, `Vulnerability`, `Host`, `Incident`) without pagination limits.
  - Added full test coverage in `ExportServiceInMemoryTest` achieving 100% success rate.

## [2.8.0] - 2026-06-12

### Changed
- **Track 6 — Milestone 6.4 Phase 6b (drop deprecated tables), `db_version` 73**: **DESTRUCTIVE** removal of everything deprecated in Phase 6a after the recorded observation window — drops all 23 `zz_deprecated_*` tables and finally the orphan columns `risks.regulation`/`risks.project_id`. The legacy `risks.status` text column is **intentionally kept** (its Phase 5 `status_id` replacement must coexist for one release before removal — not in this milestone). Gated by the tool: requires the `6a` Success entry in `schema_upgrade_log` aged ≥ the manifest's `observationDays` **and** explicit `--yes`; the automatic pre-phase backup is the only recovery path. The 23 entity classes are deleted from `DAL`. EF migration `Track6Phase6bDropDeprecatedTables` (its `Down()` is irreversible by design) + numbered SQL `Structure/Data/73.sql` (drops under `FOREIGN_KEY_CHECKS=0` since deprecated tables retained their inter-table FKs through the rename); applied via `database upgrade-schema --phase 6b --yes`. Verified end-to-end on MariaDB (every `zz_deprecated_*` gone, orphan columns dropped, `status` retained, `--yes`/observation gate enforced).
- **Track 6 — Milestone 6.4 Phase 6a (deprecate dead tables), `db_version` 72**: deprecated the 23 zero-reference tables (functional: `contributing_risks_impact`/`...likelihood`, `questionnaire_pending_risks`, `residual_risk_scoring_history`, `framework_control_test_results_to_risks`, `framework_control_type_mappings`, `permission_to_permission_group`, `mitigation_accept_users`, `risk_to_additional_stakeholder`/`...location`/`...technology`, `framework_control_test_comments`/`...audits`, `failed_login_attempts`, `user_pass_history`; enumeration: `control_phase`/`control_type`, `file_type_extensions`, `regulation`, `risk_function`, `test_status`, `threat_catalog`/`threat_grouping`) by **unmapping them from EF** (DbSets + `OnModelCreating` configs removed) and **RENAMING them to `zz_deprecated_*`** — reversible, data preserved, forgotten access fails loud. Also unmapped (no DDL) the orphan columns `risks.regulation`/`risks.project_id` (no live referent, zero code use), physically dropped in 6b. Security note: `failed_login_attempts`/`user_pass_history` confirmed unused (no login-lockout / password-reuse logic; `UserPassReuseHistory` is the live one and is **not** removed). EF migration `Track6Phase6aDeprecateDeadTables` (hand-written to RENAME, mirroring the numbered SQL, rather than EF's scaffolded `DropTable`) + numbered SQL `Structure/Data/72.sql`; applied via `database upgrade-schema --phase 6a`. Manifest `removalCandidates`/census corrected to the live snake_case table names (the PascalCase entity names never matched a case-sensitive DB). Verified end-to-end on MariaDB (≥23 tables renamed, originals gone, seeded row preserved through the rename, orphan columns retained for 6b).
- **Track 6 — Milestone 6.4 Phase 5 (status type standardization), `db_version` 71**: added `risks.status_id` (`int`) as the type-safe replacement for the free-text `risks.status` and backfilled it from the known status strings (`New`=0, `Mitigation Planned`=1, `Mgmt Reviewed`=2, `Closed`=3; unmapped legacy values left `NULL`). **Create-copy-coexist**: the legacy `status` column is retained — the old column is never dropped in the same release that introduces its replacement. The C# `Risk.StatusId` maps to a new `DAL.Enums.RiskStatus` (mirrors `Model.Risks.RiskStatus`, since `Model`→`DAL` is the project dependency direction so `DAL` cannot reference `Model`) via explicit `HasConversion<int>()`; `BiometricTransaction.TransactionResult` also gained an explicit `HasConversion<int>()` (model-only — its column is already `int`). EF migration `Track6Phase5StatusTypeStandardization` + numbered SQL `Structure/Data/71.sql`; applied via `database upgrade-schema --phase 5`. *(The temporal `ON UPDATE CURRENT_TIMESTAMP` columns were intentionally left as-is — they are audit timestamps that should keep auto-updating.)* Verified end-to-end on MariaDB (column is `int`, backfill mapping correct, unmapped value `NULL`, legacy `status` retained).
- **Track 6 — Milestone 6.3 Phase 4 (indexing + BLOB→text), `db_version` 70**: added hot-path indexes justified by the real Sieve filters/sorts (`ApplicationSieveProcessor`) and the risk listing query — `idx_vulnerabilities_first_detection`/`idx_vulnerabilities_last_detection`, `idx_hosts_status`/`idx_hosts_registration_date`, the composite `idx_risks_status_submission_date`, and `idx_user_email` — and dropped the redundant `UNIQUE` `id` index on `framework_control_tests` (already covered by the PK). Converted the text-bearing `blob` columns to proper text types and changed their C# properties from `byte[]` to `string`: `user.email` → `varchar(255)` (app-written UTF-8, direct conversion; MapsterConfiguration/`AuthenticationController`/`EmailController`/`UserCommand` simplified — the email map is now an identity), and `frameworks.name`/`description`, `framework_controls.long_name`/`description`/`supplemental_guidance`, `permissions.description` → `TEXT`. **Encoding note:** the legacy framework/permission BLOBs hold Windows-1252/latin1 seed bytes (they contain bytes like `0x94` that are *not* valid UTF-8 and the app never writes them), so they convert via a `latin1`→`utf8mb4` round-trip (lossless transcoding) rather than a direct `MODIFY` that would error; validate on a production clone first. `permissions.description` is returned raw by `GET /Users/permissions`, so that JSON field changes from base64 to plain text. EF migration `Track6Phase4IndexingBlobText` keeps the snapshot in sync; the numbered SQL `Structure/Data/70.sql` uses the latin1 round-trip and omits EF's incidental `risk_scoring` index rename (the real schema already names that index `id`). Applied via `database upgrade-schema --phase 4`. Verified end-to-end on MariaDB (cp1252 bytes transcode to the correct Unicode, app UTF-8 preserved, indexes present and in `EXPLAIN` possible_keys, redundant index gone).
- **Track 6 — Milestone 6.3 Phase 3 (relationships), `db_version` 69**: promoted the orphan correlation columns to navigable, indexed foreign keys to `user(value)` — `risks.owner`/`manager`/`submitted_by` (`fk_risks_*`), `framework_controls.control_owner` (`fk_framework_controls_control_owner`), `framework_control_tests.tester` (`fk_framework_control_tests_tester`), all made nullable with **`ON DELETE SET NULL`** and EF navigations (`Risk.OwnerUser`/`ManagerUser`/`SubmittedByUser`, `FrameworkControl.ControlOwnerUser`, `FrameworkControlTest.TesterUser`). Added `incidents.reported_by_id` (nullable FK `fk_incidents_reported_by`, `Incident.ReportedByUser`), **keeping the free-text `ReportedBy` column for external reporters** and best-effort backfilling the FK by exact, unambiguous `user.name` match. **Orphan-safe order:** dangling references are logged to a new `schema_upgrade_orphans` audit table *before* being NULLed (the log is the recovery record, since `Down()` cannot un-null), then the constraints are added — applying the constraint against un-cleaned data fails by design. Resolved the `IncidentToIncidentResponsePlan` mapping ambiguity (removed the dead commented block; the join is mapped once via `UsingEntity`). `Risk.ProjectId` has no live `projects` table, so it gains **no FK** and is flagged a Milestone 6.4 removal candidate. EF migration `Track6Phase3Relationships` + numbered SQL `Structure/Data/69.sql` (hand-authored for the orphan log/cleanup/backfill ordering EF can't express); applied via `database upgrade-schema --phase 3`. Verified end-to-end on MariaDB (orphans logged then nulled, valid refs untouched, backfill matches only unique names, `ON DELETE SET NULL` confirmed, FK columns indexed).
- **Track 6 — Milestone 6.2 Phase 1c (collation unification), `db_version` 68**: converted the entire schema to **utf8mb4 / utf8mb4_unicode_ci** — every legacy `utf8mb3` (and `utf8mb4_general_ci`) table, covering all 99 base tables present at `db_version` 67 (including legacy tables not mapped by EF), via `ALTER TABLE … CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci` (table default + all char columns) plus `ALTER DATABASE`. This lets text columns store 4-byte characters (emoji, etc.) that `utf8mb3` silently rejected. EF model collation annotations updated to match; migration `Track6Phase1cCollationUtf8mb4` keeps the snapshot in sync, but the numbered SQL `Structure/Data/68.sql` uses `CONVERT TO` (one statement per table) rather than EF's verbose per-column output. Applied via `database upgrade-schema --phase 1c`. Manifest phases 3–6b renumbered (+1, now `db_version` 69–73). Verified end-to-end on MariaDB: no `utf8mb3` table/column remains, existing data preserved, and a 4-byte emoji round-trips. *This completes the deferred 6.2 collation work; `dotnet-ef` is now pinned to 10.0.9 via a local tool manifest.*
- **Track 6 — Milestone 6.2 Phase 2b (column naming), `db_version` 67**: renamed the last stray PascalCase column `comments.IsAnonymous` → `is_anonymous` (the Phase 1b boolean fix had kept the legacy name). EF migration `Track6Phase2bIsAnonymousColumnRename` + numbered SQL `Structure/Data/67.sql`; applied via `database upgrade-schema --phase 2b`. Manifest phases 3–6b renumbered (+1, now `db_version` 68–72). Verified end-to-end on MariaDB (new column present, old gone, value preserved).
- **Track 6 — Milestone 6.2 Phase 1b (boolean width normalization), `db_version` 66**: normalized the genuine booleans `comments.IsAnonymous` and `framework_controls.deleted` from `tinyint(4)` to `tinyint(1)` (deferred from Phase 1). The C# properties changed from `sbyte` to `bool` (Pomelo maps `bool`↔`tinyint(1)`), along with the `SecurityControlStatistic.Deleted` DTO field and the call sites (`CommentsController`/`CommentsService`/`FixRequestController`/`VulnerabilityFixChatDialogViewModel`/`StatisticsController`). EF migration `Track6Phase1bBooleanNormalization` + numbered SQL `Structure/Data/66.sql`; applied via `database upgrade-schema --phase 1b`. Manifest phases 3–6b renumbered (+1, now `db_version` 67–71). Verified end-to-end on MariaDB (column type → `tinyint(1)`, 0/1 values preserved). *(Note: the `comments` column is still physically named `IsAnonymous` (PascalCase) — a snake_case rename is a separate naming gap, not part of the deferred width fix.)*
- **Track 6 — Milestone 6.2 Phase 2 (naming uniformization), `db_version` 65**: renamed the 8 PascalCase tables to snake_case (`Incidents`→`incidents`, `IncidentResponsePlans`→`incident_response_plans`, `IncidentResponsePlanTasks`/`...Executions`/`...TaskExecutions`, `IncidentToIncidentResponsePlan`→`incident_to_incident_response_plan`, `FaceIDUsers`→`face_id_users`, `BiometricTransaction`→`biometric_transactions`, `FixRequest`→`fix_requests`) and the hybrid columns (`vulnerabilities_to_actions.actionId`/`vulnerabilityId`→`action_id`/`vulnerability_id`; `reports.creationDate`/`creatorId`/`fileId`→`created_at`/`creator_id`/`file_id`; `hosts.FQDN`/`OS`→`fqdn`/`os`; `messages.Message`→`message`). EF migration `Track6Phase2NamingUniformization` + numbered SQL `Structure/Data/65.sql` (hand-cleaned to drop Pomelo's `DELIMITER`-based PK procedure — the join-table PK is composite, not auto-increment — so `MySqlConnector` can apply it). **RENAME only — no data loss; C# entity/DTO names unchanged** (mapping via `ToTable`/`HasColumnName`). Verified end-to-end against the real legacy schema on MariaDB (renames + row-count/value parity).
- **Track 6 — Milestone 6.2 Phase 1 (safe fixes), `db_version` 64**: renamed the typo indexes (`idx_biometic_id`/`idx_biometic_anchor` → `idx_biometric_transaction_id`/`idx_biometric_transaction_anchor`; `idx_irpt_sequencial`/`idx_irpt_optinal` → `idx_irpt_sequential`/`idx_irpt_optional`) and removed the illegal `0000-00-00` column defaults on `mgmt_reviews.next_review` (default dropped) and `mitigations.last_update` (→ `CURRENT_TIMESTAMP`) — these break MariaDB strict mode. Authored as EF migration `Track6Phase1SafeFixes` + numbered SQL `Structure/Data/64.sql`; applied via `database upgrade-schema --phase 1`. C# entities/DTOs unchanged. Boolean `tinyint(1)` normalization and broad collation unification are **deferred** (they need `sbyte`→`bool` type changes / a per-column survey, beyond Phase 1's rename-only safety). Verified end-to-end against the real legacy schema on MariaDB in `DAL.IntegrationTests`.

### Added
- **Track 6 (Database Uniformization) — 6.1 tooling foundation**: introduced the `schema_upgrade_log` audit table (EF entity + migration `20260611141630_SchemaUpgradeLog`, applied via numbered SQL `db_version` 63) that records every schema-upgrade run, and a data-driven phase manifest (`src/ConsoleClient/DB/SchemaUpgradePhases.yaml`) describing the Track 6 phases (1–6b) with their target `db_version`, census queries, post-apply validations, and destructive-phase gate metadata.
- **Track 6 — `netrisk-console database upgrade-schema` command**: new ConsoleClient operation with `--phase`, `--env`, `--check`, `--dry-run`, `--yes`, and `--output`. `--check` runs read-only pre-flight (connectivity, current-vs-expected `db_version`, phase SQL-file presence, and the destructive `6b` observation-window gate against `schema_upgrade_log`); `--dry-run` prints/writes the exact numbered SQL a phase would apply (both mutate nothing); and a real apply runs the full **backup → census → apply numbered SQL → post-apply validation → audit-log** sequence, aborting before any change if pre-flight fails and refusing destructive/prod runs without `--yes`. Post-apply validations cover index/foreign-key/column-type/table existence and custom scalar checks against `information_schema`. Backed by `ISchemaUpgradeService`/`SchemaUpgradeService`, the pure `SchemaUpgradePlanner`/`SchemaUpgradeManifestLoader`, and `SchemaUpgradeValidator`.
- **Track 6 — `netrisk-console database baseline`**: new ConsoleClient operation (Plan Phase 0) that records the pre-uniformization baseline — current `db_version`, pending EF migrations, model-vs-snapshot divergence (`HasPendingModelChanges`), and a row-count census of the Phase-6 removal candidates (data-driven from the manifest's `removalCandidates`) that recommends `drop` (empty/absent) vs `archive` (has data). Optional `--output` writes a Markdown report. Read-only.
- **Track 6 — `DAL.IntegrationTests` harness**: new test project using Testcontainers (`Testcontainers.MariaDb`) that boots a throwaway MariaDB container to verify the shipped `schema_upgrade_log` DDL, the EF entity round-trip, the full apply orchestration (apply + validate + audit-log, plus the validation-failure path), and the baseline census end-to-end against real MariaDB (NetRisk's production database). Tagged `Category=Integration` (requires Docker; exclude from the fast unit run with `--filter "Category!=Integration"`). Unit coverage for the tool is 31 tests in `ServerServices.Tests`.
- **Track 6 — conventions documented** in [CLAUDE.md](CLAUDE.md): the target schema convention (so new entities are born compliant), how migrations actually reach production (numbered SQL + `db_version`), and the schema-upgrade/baseline tooling. Completes Milestone 6.1 (the operational production-baseline *run* against the live prod DB remains, by nature, an ops step). See [roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md](roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md).

## [2.7.7] - 2026-06-11

### Changed
- **Test coverage for the data and server-service layers**: added unit tests raising `DAL` coverage to ~99% (excluding generated EF migrations) and `ServerServices` line coverage from ~11% to ~90%. New tests cover the change-auditing pipeline (`AuditableContext`, `Auditing.Base`) and the domain services, with at least one behavior test per service. Introduced an EF Core in-memory test harness (`InMemoryServiceTestBase`) and a `coverage.runsettings` that excludes generated migrations and genuinely-untestable I/O/rendering classes so the reported figure reflects testable logic. No production code changed.

## [2.7.6] - 2026-06-10

### Changed
- **File uploads now stream in chunks instead of a single request**: the GUI client previously POSTed the whole file base64-encoded inside one JSON body to `POST /Files`, so any attachment over ~22 MB exceeded Kestrel's 30 MB request-body limit and the connection was reset (surfaced to the user as a "Broken pipe"). `UploadFileAsync` now requests an upload id, sends the content in 5 MB chunks to `POST /Files/local/chunk`, then calls a new `POST /Files/local/complete` to finalize. This keeps every request small regardless of file size.

### Fixed
- **Chunked uploads were never persisted**: the server's chunk endpoint only reassembled the parts into a `.dat` file on disk and then stopped — it never created the `NrFile` database record, never stored the content (files are persisted as a DB blob), and never associated the file with its incident/risk/plan/task/mitigation, so a chunk-uploaded file was orphaned and never appeared as an attachment. Added `CompleteChunkedUpload` (and the `POST /Files/local/complete` endpoint) which reassembles the chunks, loads the content, persists the record with its entity association via the same path as a single-shot upload, and cleans up the temporary chunk files. The chunk endpoint no longer auto-combines, so finalization is the single authoritative reconciliation step.
- **API request-body limit raised and made configurable**: Kestrel's default 30 MB `MaxRequestBodySize` is now raised to 100 MB and configurable via `Files:MaxRequestBodySizeBytes`, protecting non-chunked endpoints and giving headroom for the chunk-finalize call.

## [2.7.5] - 2026-06-10

### Fixed
- **GUI crash when adding a file to an incident**: the "Add file" button on the Edit Incident window passed the window itself as a `CommandParameter` into `BtFileAddClicked`, a parameterless `ReactiveCommand<Unit, Unit>`. ReactiveUI rejects the type mismatch at execute time (`Command requires parameters of type System.Reactive.Unit, but received parameter of type EditIncidentWindow`), and the unhandled error tore down the app. The stale `CommandParameter` was removed — the handler already gets the window from its `ParentWindow` property.
- **GUI crash when a file upload fails**: file-add handlers awaited `FilesService.UploadFileAsync` with no error handling, so a failed upload (e.g. a dropped connection surfacing as `Broken pipe` / `RestComunicationException`) escaped the `ReactiveCommand` pipeline and crashed the process via ReactiveUI's default exception handler. The upload is now wrapped in a try/catch that logs the error and shows an error dialog (`ErrorUploadingFileMSG`) instead of crashing, across all five upload sites: incidents, risks, incident response plans, IRP tasks, and mitigations.

## [2.7.4] - 2026-06-09

### Fixed
- **macOS x64 GUI cross-publish (`PackageMacGUI`) failing on Apple Silicon with `NU3012`**: the Docker `linux/amd64` cross-publish does a from-scratch NuGet restore inside the container, where the online certificate revocation check flagged the author signatures on the ReactiveUI/Splat packages as revoked, aborting `dotnet publish`. (The host build never hit this because those packages were already restored and cached, so signature verification didn't re-run.) The Docker `dotnet publish` invocation now sets `NUGET_CERT_REVOCATION_MODE=offline` so restore skips the online revocation lookup. The thrown error was also misleading — it only surfaced Docker's image-pull progress because `RunProcess` includes just stderr in the exception message.

## [2.7.3] - 2026-06-09

### Fixed
- **Docker containers failing to start with a misleading `no such file or directory` on `/entrypoint.sh`**: the entrypoint scripts are stored in git as LF, but with no `.gitattributes` a build host configured with `core.autocrlf=true` (Windows) checked them out as CRLF, so `COPY entrypoint-*.sh /entrypoint.sh` baked a `#!/bin/bash\r` shebang into the image. The kernel then tried to exec the interpreter `/bin/bash\r`, which doesn't exist. Added a repository `.gitattributes` that pins line endings (LF for `*.sh`/source/config, CRLF only for Windows `*.bat`/`*.cmd`/`*.ps1`) and renormalized all previously-CRLF-tracked files to LF. As defense in depth, each Dockerfile now strips CRs from the entrypoint (`sed -i 's/\r$//'`) before `chmod`.

## [2.7.2] - 2026-06-09

### Fixed
- **Docker image builds failing during image export (`failed to Lchown ... no such file or directory`)**: every payload image did `COPY <payload> /netrisk` as root and then let puppet recursively re-own `/netrisk` (`file{'/netrisk': recurse => true}`). For the API/BackgroundJobs images this re-chowned the 177 MB `OpenFaceONNX.dll` (and the rest of the payload) into a second large layer, which tripped Docker Desktop's overlayfs/containerd snapshotter when extracting the layer on export. Ownership is now set once at copy time via `COPY --chown=7070:7070` (the numeric uid/gid of the puppet `netrisk` user) across all four Dockerfiles, and the redundant recursive `/netrisk` chown was dropped from the `api` and `backgroundjobs` puppet manifests. This both fixes the export failure and shrinks the images by not duplicating the payload across layers.

## [2.7.1] - 2026-06-08

### Fixed
- **`CreateAllDockerImages` Nuke target failing in `CreateDockerImageWebSite`**: the website image build unconditionally copied the Windows/Linux/macOS GUI installer artifacts into the image, so on hosts where a given platform was not packaged (e.g. the `.dmg` files on Windows) the missing source tripped Nuke's `source.DirectoryExists() || source.FileExists()` assertion and aborted the whole run. Installer copies now go through a `CopyInstallerIfPresent` helper that skips and logs a warning when an artifact is absent, so the image builds with whatever installers the current host produced.

## [2.7.0] - 2026-06-08

### Changed
- **Upgraded `Pomelo.EntityFrameworkCore.MySql` to `10.0.0-rtm.1`** (from `9.0.0`) to align the MySQL EF Core provider with the EF Core 10 packages already in use. The v10 build is sourced from the `uox-netrisk` Cloudsmith feed, which is now wired into the package source mapping.

## [2.6.2] - 2026-06-03

### Fixed
- **Clipped icons in `subButton` toolbars**: the `Button.subButton` style (add/edit/search/reload/delete toolbars on the Entities, Hosts, Incidents, and Risk views) never zeroed its default padding nor sized its child `MaterialIcon`, so the 25×25 button squeezed and clipped the glyph. Added `Padding=0` + centered content alignment and a `Button.subButton > MaterialIcon` rule sizing the icon to 16×16, mirroring the working `detailButton` pattern. Verified live on the Entities view.

## [2.6.1] - 2026-06-03

### Fixed
- **Broken "show search" toolbar icon**: the search toggle button on the Entities, Hosts, Incidents, and Risk views referenced `Kind="SelectSearch"`, which is not a valid Material Design Icons name, so the `MaterialIcon` control rendered fallback glyph text instead of an icon. Changed to `Kind="Magnify"` (the same icon already used by the search-execute buttons).

## [2.6.0] - 2026-06-03

### Added
- **macOS global menu redirection** (Milestone 1.4): a `NativeMenu` mirroring the application menu is attached to `MainWindow`. On Apple Darwin it surfaces in the system global menu bar and the in-window `Menu` is collapsed (bound to a new `IsNotMacOS` flag); on Windows/Linux the in-window menu is used as before.
- **Platform-native window-control alignment** (Milestone 1.4): the navigation bar is inset dynamically (`MainWindowViewModel.NavBarMargin`) so that, once the menu row collapses on macOS, its left-edge content clears the native top-left traffic-light controls. Platform probes consolidated into `Helpers/PlatformInfo`.
- **Keyboard accessibility sweep** (Milestone 1.4):
  - Global `Ctrl+P` opens the reporting/export surface from anywhere in the main window.
  - `Ctrl+S` (save) and `Esc` (dismiss) wired on the Risk and Incident edit windows.
  - Centralised `Esc` (dismiss) and `Ctrl/Cmd+S` (save, via the new `ISaveableDialog` opt-in) for every modal dialog inheriting `DialogWindowBase`.
  - `Ctrl+F` toggles the search panel on the Entities and Incidents views.
  - Logical `TabIndex` ordering plus `IsDefault`/`IsCancel` buttons on the Login window and entity dialog.
- **System tray integration** (Milestone 1.4): `Helpers/TrayIconManager` adds a Windows notification-area icon / macOS menu-bar extra with a quick-status preview (sign-in state and version, refreshed every 15s), an Open/Hide/Exit context menu, and minimise-to-tray behaviour on Windows.

### Fixed
- **macOS notification bell overlapping the traffic-light window controls**: the navigation bar's left inset (`NavBarMargin`, 80px on macOS) was bound on the `NavigationBar` element as a bare `{Binding NavBarMargin}`, which resolved against the control's own `NavigationBarViewModel` instead of the `MainWindowViewModel` that exposes the property — so it silently fell back to a zero margin and the notification bell sat under the native top-left window buttons. Bound the margin explicitly against the MainWindow's DataContext (`#MWindow.((dvm:MainWindowViewModel)DataContext).NavBarMargin`) so the bell clears the controls.

## [2.5.1] - 2026-06-03

### Fixed
- **Widespread broken bindings under compiled bindings**: enabling `AvaloniaUseCompiledBindingsByDefault` (Milestone 1.3) silently broke every `{Binding}` that targeted a non-public view-model member — compiled bindings can only reach public members, whereas the previous reflection bindings reached private ones. This left labels blank, tab headers falling back to the `ViewLocator` ("Not Found: GUIClient.Views.…View"), command buttons inert, and child-VM content panels empty (e.g. the entire `AdminWindow`). Audited all views against their `x:DataType` view-models and promoted the 194 bound members across 26 view-models (plus `UserInfoViewModel`) from `private` to `public`. Verified live: `UserInfo` and `AdminWindow`/`UsersView` now render fully.

## [2.5.0] - 2026-06-03

This release includes new features and improvements.

### Added
- **Compiled bindings enabled globally** (`AvaloniaUseCompiledBindingsByDefault=true` in GUIClient): every view now declares an explicit `x:DataType`, giving compile-time binding validation and faster rendering with a lower RAM footprint. (Milestone 1.3)
- **High-performance virtualizing `TreeDataGrid`** for the dense vulnerability grid, replacing the `DataGrid`. Source/columns are built in code-behind (`FlatTreeDataGridSource<Vulnerability>`) reusing the existing converters and status cell template, with two-way selection sync.
- TreeDataGrid via the `libs/TreeDataGrid.Avalonia` submodule (MIT, .NET-Foundation source ported to Avalonia 12; security-reviewed), since Avalonia 12's official `Avalonia.Controls.TreeDataGrid` package is now commercially licensed
- Explicit `VirtualizingStackPanel` on the primary dense data lists (incidents, hosts, risks, users, notifications) to enforce UI virtualization and guard against accidental regressions
- `RiskScoringPair` record (replaces `Tuple<Risk, RiskScoring>`) so the vulnerability risk panel binds with compiled bindings
- Project docs: `CLAUDE.md`, `ROADMAP.md`, per-feature docs under `docs/features/`, `docs/ui-standard.md`
- Transitive pin for `Tmds.DBus.Protocol` 0.92.0 in GUIClient (addresses GHSA-xrw6-gwf8-vvr9)
- Transitive pin for `System.Security.Cryptography.Xml` 10.0.7 in API.Tests and ServerServices.Tests (addresses GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf)
- UI standard compliance audit (`roadmap/UI_STANDARD_AUDIT.md`) and remediation plan (`roadmap/UI_STANDARD_COMPLIANCE_PLAN.md`)

### Fixed
- **macOS window dragging restored**: the custom title-bar `Menu` stretched the full window width with `ElementRole="User"` (non-draggable), leaving no `TitleBar` surface to grab; set `HorizontalAlignment="Left"` so the menu only occupies its items and the rest of the title-bar row is draggable again.
- **`--environment` argument parsing** in `GUIClient`: now accepts both `--environment=dev` and `--environment dev` forms, guards against a missing value, and corrects the prior bug that validated the wrong variable (plus the "Unkown environment" typo).
- Compile-time binding errors surfaced by enabling compiled bindings (previously silent, failing reflection bindings): added missing `StrActions` (AssessmentViewModel), `StrNotifications` (NavigationBarViewModel), `IsViewOperation`/`IsCreateOperation` (EditIncidentViewModel), and `CanCancel`/`CanClose` (IncidentResponsePlanTaskViewModel); corrected stale `ElementName`/`#name` references in `EditIncidentWindow`, `IncidentResponsePlanTaskWindow`, `EditMgmtReview`, `MainWindow`, and `AssessmentView`; typed the TreeViewItem style bindings in `EntitiesView`

### Changed
- **GUIClient UI compliance pass**: all Avalonia views now conform to the UI standard — hardcoded hex/named Background and Foreground colors removed from layout containers, all dialog/action/navigation buttons carry canonical CSS classes (`dialog1`, `dialog2`, `operation`, `subButton`, `navigation`, etc.), fixed-width inputs replaced with `MinWidth`, navigation buttons carry `Classes="navigation"`, `Classes="dark"` applied to modal windows. Semantic state colors (ProgressRing spinner, notification bell, FaceID status icons) preserved intentionally.

### Changed
- **Avalonia 11.3.11 → 12.0.1** across GUIClient, AvaloniaExtraControls, and the Aura.UI submodule. Trade-offs documented in ROADMAP.md (dev-tools overlay removed, tab drag-reorder removed, SVG assets replaced by Material icons, `SpacedGrid` replaced by native `Grid` spacing).
- ReactiveUI 22.3.1→23.2.1, ReactiveUI.Avalonia 11.3.8→12.0.1, Splat 17→19
- Material.Icons.Avalonia 2.4→3.0, MessageBox.Avalonia 3.x→12.x, Deadpikle.AvaloniaProgressRing 0.10→0.11
- LiveChartsCore family 2.0.0-rc5.4 → 2.1.0-dev-292
- SkiaSharp 3.119.2 → 3.119.3-preview.1.1 (required by Avalonia.Skia 12)
- Spectre.Console 0.51→0.55.2, Spectre.Console.Cli 0.51→0.55.0, Serilog.Sinks.Spectre 0.5→0.6.0 (breaking: `Command.Execute` now takes `CancellationToken`; visibility `protected`)
- Dependency refresh across all projects (patch/minor updates):
  - Serilog 4.3.0→4.3.1, Serilog.Sinks.Console 6.0.0→6.1.1, Serilog.Extensions.Hosting 9→10, Serilog.Extensions.Logging 9→10
  - Microsoft.Extensions.* 10.0.2→10.0.7 (Hosting, Localization, Configuration.Abstractions, DependencyInjection, DependencyInjection.Abstractions, DependencyModel)
  - Microsoft.AspNetCore.Authentication.JwtBearer 10.0.2→10.0.7, SystemWebAdapters 2.2.1→2.3.0
  - System.IdentityModel.Tokens.Jwt 8.15.0→8.17.0, System.Drawing.Common 10.0.2→10.0.7
  - BCrypt.Net-Next 4.0.3→4.1.0, DeviceId 6.9→6.11, Polly 8.5.2→8.6.6
  - MySqlConnector 2.4.0→2.5.0, MySqlBackup.NET.MySqlConnector 2.6.5→2.7.0
  - SkiaSharp family 3.119.1→3.119.2
  - Microsoft.ML.OnnxRuntime 1.23.2→1.24.4
  - JetBrains.Annotations 2025.2.2→2025.2.4, xunit.runner.visualstudio 3.1.4→3.1.5, Microsoft.NET.Test.Sdk →18.5.0
  - Tools.InnoSetup 6.4.3→6.7.1
- `MainWindow.axaml`: removed `ExtendClientAreaToDecorationsHint`, dead acrylic border, and redundant nested Grid wrappers; simplified layout to `RowDefinitions="Auto, Auto, *"` (menu → navigation → content)
- `NavigationBar.axaml`: replaced fragile level-index ancestor bindings (`$parent[7]`/`$parent[6]`) with type-safe `$parent[views:MainWindow]` lookups
- UI compliance pass across all GUIClient views: removed inline `Background`/`Foreground` hex literals, added canonical button classes (`dialog1`, `dialog2`, `operation`, `subButton`, `navigation`), converted fixed `Width=` to `MinWidth=` on form inputs, migrated form `StackPanel`s to responsive `Grid` layouts with `form_label` classes
- `LoginWindow.axaml`: migrated form to responsive `Grid`, added `dialog1`/`dialog2` button classes with icons
- `CloseDialog.axaml`, `FixRequestDialog.axaml`: button classes normalized, `Classes="dark"` added to window
- `NavigationBar.axaml`: `Classes="navigation"` added to all nav buttons

### Fixed
- High-severity transitive vulnerabilities in `Tmds.DBus.Protocol` and `System.Security.Cryptography.Xml`
- GUIClient startup crash on Avalonia 12 caused by `LiveChartsCore.SkiaSharpView.Avalonia` 2.0.1 still targeting Avalonia 11 APIs (`Avalonia.Input.Gestures.PinchEvent`)
- `libs\Aura.UI\Aura.UI.sln` now loads cleanly after aligning the remaining Aura.UI test/desktop sample projects with `.NET 10` + Avalonia 12 and excluding the legacy Blazor gallery sample from the solution
- MainWindow top-bar overlap where native OS title bar and custom `<Menu>` rendered in the same zone (caused by `ExtendClientAreaToDecorationsHint="True"` without the matching transparency stack)
- Navigation bar buttons crashing with `NullReferenceException` / `ArgumentNullException` after layout flattening due to hardcoded ancestor-level bindings resolving to `null`



## [2.2.0] - 2026-02-06

This is a major maintenance release with .NET 10 upgrade and significant UI improvements.

### Added
- Responsive window layouts for EditRiskWindow (controls now expand/contract with window resizing)
- Responsive DataGrid columns in RisksPanelView with user controls for reordering, resizing, and sorting
- Tooltips to status icons in RisksPanelView for better user experience
- AssetTargetFallback configuration to support .NET 8/9 packages in .NET 10 projects

### Changed
- **Upgraded to .NET 10.0** with C# 13 language support across all projects
- Updated NuGet package source mapping to allow all packages from nuget.org by default
- Upgraded Hangfire from 1.8.21 to 1.8.23
- Upgraded MySqlConnector from 2.4.0 to 2.5.0
- Upgraded Newtonsoft.Json from 13.0.3 to 13.0.4 (security update)
- Upgraded Microsoft.Extensions.DependencyInjection from 9.0.9 to 9.0.12
- EditRiskWindow now starts at 1200x800 with minimum size of 900x650
- EditRiskWindow controls converted from fixed-width StackPanels to responsive Grid layouts
- RisksPanelView DataGrid columns now use star-sizing for proportional distribution
- Updated submodules: NessusParser, Aura.UI, netrisk-plugin-sdk, reliable-rest-client-wrapper
- Improved horizontal and vertical responsiveness across GUIClient views

### Fixed
- Resolved duplicate Applications.resx resource conflicts
- Fixed EF Core dependency warnings in DAL project
- Fixed ServerServices compile warnings (CS8603 nullable reference warnings in MapsterConfiguration)
- Fixed ServerServices CS0219 warning (unused variable in FaceIDService)
- Fixed PDFsharp restore failures
- Fixed API build failures and license gating issues
- Fixed GUI client build errors during migration
- Fixed Avalonia ReactiveUI startup issues
- Fixed EditRiskWindow buttons not staying at bottom during vertical resize
- Fixed EditRiskWindow right panel overlapping dropdowns on narrow windows
- Fixed risk deletion and closure bugs
- Package dependency warnings across all projects resolved



## [2.1.4] - 27/09/2025

This is a maintenance release with several bug fixes and improvements.

### Added

### Changed

### Fixed
- Risk closure bug


## [2.1.0] - 27/08/2025

This is a maintenance release with several bug fixes and improvements.

### Added
- A search on the incident response plan list
- Risk calculation command line command
- Plugin system
- FaceId plugin verification
- FaceId registration
- FaceId verification for risk closure
- Created the security classification entity
- Created the organization data entity
- Created the organization data group entity

### Changed
- Layout improvements on the incident window
- Changed the position of the edit button on the entities view
- Upgraded several packages to the latest version
- Incident ReportedByEntity field is now nullable
- Upgraded to Avalonia 11.3
- Upgraded to .NET 9
- Upgraded LiveCharts
- Bussiness process entitiy has new fields

### Fixed
- Return to the first pagination on the risk vulnerability list after selecting a new risk
- The search on the incident response plan list
- Bug in risk association
- Contributing score no longer considers closed vulnerabilities
- Bug in closing incident response plan window

## [2.0.7] - 2025-08-01

This is a bug fix release.

### Added

### Changed
- Filter to only show approved incidents response plans on the incident window
- Layout improvements on the incident window

### Fixed
- Risk vulnerability pagination
- Risks loading time
- Added missing scroll view on the incidents window
- Removed leftover foreign key on the incident response plan


## [2.0.6] - 2025-07-01

This is a bug fix release.

### Added
- Risk vulnerability pagination

### Changed


### Fixed
- Risks loading time


## [2.0.0] - 2025-06-01

This is a new major release that brings some new features and improvements.

### Added
- Incident Management
- Incident Response Plans
- New Dashboard graphics and improved performance
- Last import date on vulnerability data
- Filters on the entity list

### Changed
- Ordering of the entity list
- Filters for the multi select fields
- Risk filter location

### Fixed
- Several bug fixed - please see [Github issues](https://github.com/ffquintella/netrisk/issues)

## [1.7.1] - 2024-11-06

This is a new major release that brings some new features and improvements.

### Added

- Vulnerability chat tracking and improved e-mail communication
- New Dashboard graphics and improved performance
- Started to use .net migrations as a way to manage the database schema

### Changed

- The way risk catalogs are stored and managed

### Fixed

- Several bug fixed - please see [Github issues](https://github.com/ffquintella/netrisk/issues)

## [1.6.1] - 2024-10-15

...

