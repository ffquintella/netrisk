# Track 2 — GRC Core & Reporting Engine: Detailed Specifications

> Status: **Backend complete — GUI not yet implemented** (Milestones 2.2–2.4 have REST API + `ServerServices` only; no `ClientServices` REST clients or `GUIClient` views exist yet, so they are not usable from the desktop app). · Roadmap: [ROADMAP.md → Track 2](../../ROADMAP.md)
> Research basis: web survey of GRC market demands and best practices (June 2026) — sources at the end of each milestone.

This track covers the GRC (Governance, Risk, and Compliance) core features, incident workflows, and data output templates. All new entities introduced here must be **born compliant** with the Track 6 schema conventions (snake_case, `fk_`/`idx_`/`uq_` prefixes, UTC `created_at`/`updated_at`, int-backed enums via `HasConversion<int>()` — see [CLAUDE.md](../../CLAUDE.md)) and reach production through the numbered-SQL `db_version` path.

---

## Milestone 2.1: Advanced Reporting Engine

**Market context.** GRC buyers' top complaint is needing IT/vendor help to change reports; "no-code customization" and audience-tailored output (executive vs. specialist views) are the differentiators. Pre-built template libraries and one-click report environments (risk registers, heatmaps, gap summaries auto-populated from data) are the demand signals.

### 2.1.1 Customizable report templates (logo, styling, sections)

**Data model**
- New `report_templates` table / `ReportTemplate` entity: `name`, `description`, `owner_id` (FK user), JSON layout definition, branding block (logo file id, primary/secondary colors, font choice, header/footer text), ordered list of **section descriptors**.
- Section descriptor types: cover page, risk register, heatmap, vulnerability summary, incident list, compliance grid, free rich-text block. Each carries its data source (an existing `ServerServices` query + Sieve filter string) and display options (table/chart/heatmap, severity color mapping).
- Templates are **versioned**: edits create a new version; generated reports record the version used so history is immutable.

**Rendering**
- PDF backend: **QuestPDF** (code-first fluent API, hot-reload preview, ~1000 PDFs/min/core, thread-safe). Each section type implements an `IReportSectionRenderer` strategy.
- Rendering an empty data set must produce a valid document (no crashes on zero rows).

**API & UI**
- CRUD API at `/report-templates`.
- GUIClient template designer view: section list with drag-to-reorder, live preview pane, logo upload (reuse the existing file-upload path), "save as copy" for ad-hoc variants. Follows the UI standard (button taxonomy, localized strings).
- Ship 3–5 built-in seed templates: Executive Risk Summary, Vulnerability Posture, Incident Review.

**Acceptance criteria**
- An admin creates a branded template with reordered sections without writing code.
- Empty data sets render valid documents.
- Editing a template does not mutate previously generated reports.

### 2.1.2 Scheduled exports via email (dashboards, compliance grids, open incidents)

**Data model**
- New `report_schedules` table / `ReportSchedule` entity: template id, frequency (daily/weekly/monthly + time + timezone), output format, recipient list, `enabled`, `last_run_at`, `last_status`.

**Behavior**
- Implemented as recurring **Hangfire** jobs in `BackgroundJobs`: generate → attach → send through the existing email path.
- Retries with exponential backoff; failures logged and surfaced in the schedule list UI.
- "Send test now" button — verifying email rendering before committing a schedule is the standard practice.
- Per-schedule audit of what was sent, when, and to whom.
- Attachment-size guard: fall back to a download link served by the API when over the SMTP attachment limit.

**Acceptance criteria**
- A weekly "open incidents" PDF arrives on schedule; disabling pauses cleanly.
- A failed SMTP run is retried and visible as a failed run in the UI.

### 2.1.3 PDF, CSV, and Excel export targets for all statistics tables

**Design**
- Single `IExportService` with `ExportAsync(dataset, format)` and three targets:
  - **PDF** — QuestPDF.
  - **XLSX** — **ClosedXML** (no Excel install required); use ClosedXML.Report templates so layout matches the report templates. Typed columns (dates as dates, numbers as numbers).
  - **CSV** — culture-safe writer: UTF-8 BOM, configurable delimiter, **formula-injection escaping** (prefix `=`, `+`, `-`, `@` cells).
- Export endpoints accept the same Sieve filter/sort string the grid is showing — "what you see is what you export."

**UI**
- Uniform export split-button on every statistics grid (Risks, Vulnerabilities, Incidents, Hosts, compliance tables), using the existing button taxonomy.

**Acceptance criteria**
- Every statistics table exports all three formats with the active filters applied.
- Excel exports have typed columns, not strings.

**Sources (2.1):** [Expert Insights — Top GRC Platforms](https://expertinsights.com/compliance/the-top-governance-risk-compliance-grc-platforms) · [Gartner Peer Insights — GRC Tools](https://www.gartner.com/reviews/market/governance-risk-and-compliance-tools-assurance-leaders) · [Riskonnect GRC Tool](https://riskonnect.com/solutions/grc-tool/) · [QuestPDF](https://www.questpdf.com/) · [ClosedXML.Report](https://github.com/ClosedXML/ClosedXML.Report)

---

## Milestone 2.2: Enhanced Assessments Workflow

**Market context.** Conditional show/hide logic and framework alignment are table stakes in questionnaire tooling; respondent fatigue is the #1 failure mode, so progressive disclosure matters. Cross-framework control tagging (one answer satisfying multiple frameworks) is a recurring demand.

### 2.2.1 Interactive paged assessment viewer (nesting, conditional logic, rich text)

**Data model**
- Extend the assessment model with: question **groups/pages**, parent-child question nesting, a `condition` JSON field per question (`{ "questionId": X, "operator": "equals|in|notEmpty", "value": ... }`), and a rich-text `explanation` help field (stored as Markdown, rendered in Avalonia).

**Behavior**
- Viewer: one page per group, previous/next navigation, left rail with page list + per-page completion state, validation on page-leave (required questions), final review page listing unanswered required items with jump links.
- Conditional logic evaluated client-side for show/hide **and re-enforced server-side at submission** (hidden questions' answers discarded / not required) — never trust the client.

**Acceptance criteria**
- An assessment with 3 pages, a nested follow-up question, and a condition ("show Q5 only if Q4 = Yes") behaves identically in the viewer and in server validation.

### 2.2.2 Progress trackers and draft auto-saving

**Data model**
- Per-respondent `AssessmentRun` with status enum (`Draft`, `InProgress`, `Submitted`, `Reviewed`) and `progress_pct` computed from answered/required **visible** questions.

**Behavior**
- Auto-save: per-field debounce save (~2s after last change) + periodic full-draft save; on reopen, resume at the last page; "saved at HH:mm" feedback in the viewer.
- Optimistic concurrency token on the run; two sessions editing the same draft → last-write-wins per answer, with a warning.

**Acceptance criteria**
- Killing the GUI mid-assessment loses at most the field being typed.
- The progress bar matches the review page's unanswered count.

### 2.2.3 Import assessment templates from industry standards (NIST, ISO 27001) via JSON/Excel

**Formats**
- Versioned JSON schema: metadata, pages, questions, types, conditions, scoring weights, framework-control mapping ids.
- Excel layout (parsed with ClosedXML): one row per question; columns for page, parent, type, options, condition.
- Round-trip: export an existing assessment to the same JSON/Excel for offline editing.

**Behavior**
- Importer is dry-run first: validate, show a diff/summary (N pages, M questions, K warnings), then commit. An invalid file fails the dry run with row-level errors and imports nothing.
- Map questions to framework controls where the source provides references (e.g., ISO 27001 Annex A ids), so answers can later feed compliance views.
- Ship starter packs: NIST CSF 2.0 and ISO 27001:2022 Annex A question sets as bundled JSON. **Licensing note:** model questions on the controls; do not reproduce the standard's text verbatim.

**Acceptance criteria**
- Importing a 100-question Excel produces a working paged assessment.
- Invalid files import nothing and report row-level errors.

**Sources (2.2):** [SiftHub — Security questionnaire best practices](https://www.sifthub.io/blog/information-security-questionnaire) · [UpGuard — ISO 27001 questionnaire template](https://www.upguard.com/blog/free-iso-27001-vendor-questionnaire-template) · [Linford & Co — ISO 27001 risk assessment](https://linfordco.com/blog/iso-27001-risk-assessment-guide/)

---

## Milestone 2.3: Multi-Entity & Multi-Tenant Support

**Research basis — the cardinal rules of multi-tenant RBAC:**
1. Every authorization decision must be tenant/entity-aware.
2. Roles are scoped per entity, not global.
3. No code path may allow a cross-entity read/write.
4. Auditors ask "who could access X on date T?" — keep role-assignment history.

### 2.3.1 Segregate assets, risks, and vulnerabilities by Business Entity

**Schema**
- Add `entity_id` FK (`fk_<table>_entity_id` + `idx_<table>_entity_id`, per Track 6 convention) to risks, hosts/assets, vulnerabilities, incidents, and assessments. Nullable during migration, with a backfill/assignment tool; enforced for new records afterwards.
- Applied via the established numbered-SQL + `upgrade-schema` phase mechanism (new manifest entry in `src/ConsoleClient/DB/SchemaUpgradePhases.yaml`), with census + post-apply validations like the Track 6 phases.

**Enforcement**
- Filtering is enforced **in `ServerServices`** as a global query predicate (EF global query filter or a mandatory scope parameter on every service method) — never only in controllers or the client.

### 2.3.2 Role-based scoped access per entity

**Schema**
- New `user_entity_roles` join table (user, entity, role) — a user can be Analyst in Entity A and Viewer in Entity B. A global-admin flag bypasses scoping.
- **Assignment history**: role rows are never hard-deleted; add `revoked_at` so point-in-time access questions are answerable.

**Enforcement**
- The API resolves the caller's entity set into claims at token issuance; `ServerServices` intersects every query with that set.
- Deny-by-default: a user with no entity assignment sees nothing.

**Definition of done includes negative tests**
- Explicit tests that entity-A users cannot read/update/delete entity-B records through *any* endpoint — including exports and reports.

### 2.3.3 Master Dashboard (cross-entity aggregate for administrators)

**Design**
- Admin-only view: per-entity cards (open risks by severity, open vulnerabilities, SLA breaches once 3.4 lands, incident counts) + trend lines; drill-down switches the entity scope.
- One aggregated endpoint (`GET /dashboard/master`) computing per-entity rollups server-side (no N+1 per-entity client calls); short server-side cache (1–5 min).

**Acceptance criteria**
- A non-admin calling the master endpoint gets 403.
- Numbers reconcile with each entity's own dashboard.

**Sources (2.3):** [WorkOS — Multi-tenant RBAC design](https://workos.com/blog/how-to-design-multi-tenant-rbac-saas) · [Permit.io — Multi-tenant authorization best practices](https://www.permit.io/blog/best-practices-for-multi-tenant-authorization) · [AWS — Multi-tenant RBAC/ABAC patterns](https://docs.aws.amazon.com/prescriptive-guidance/latest/saas-multitenant-api-access-authorization/avp-mt-abac-examples.html) · [Auth0 — Choosing an authorization model](https://auth0.com/blog/how-to-choose-the-right-authorization-model-for-your-multi-tenant-saas-application/)

---

## Milestone 2.4: Incident Response Automation (IRP)

**Research basis (SOAR practice).** Start by automating frequent, predictable incident types; use semi-automation with human approval gates for high-impact steps; measure MTTR/MTTD; treat playbooks as versioned, maintained artifacts.

### 2.4.1 Customizable IRP templates

**Data model**
- `IrpTemplate` + `IrpTemplateTask` entities: ordered tasks with title, rich-text instructions, default assignee rule (fixed user, role, or resolution rule), relative due offset ("T+4h from activation"), and dependency references between tasks.
- **Versioning:** activating an incident pins the template version — later template edits never rewrite history.
- An "applies to incident types/severities" matching rule per template.

**UI**
- CRUD in the existing IRP area; clone-from-existing.

### 2.4.2 Automatic task generation and assignment on incident creation

**Behavior**
- On incident create (and on type/severity change), a matching engine selects templates and instantiates the task list: resolves assignees, computes due dates from offsets, links tasks to the incident.
- Notifications to assignees via the existing email path (and Track 4 channels when available).
- **Idempotent:** re-evaluating a matching rule never duplicates already-instantiated tasks.
- **Graduated response (SOAR best practice):** template tasks flagged `requires_confirmation` are generated in a `Proposed` state that a coordinator approves before assignment — automation suggests, humans confirm sensitive steps.

**Acceptance criteria**
- Creating a "Ransomware / Critical" incident auto-creates the mapped plan's tasks with correct owners and due dates within seconds.
- The audit log records the template id + version used.

### 2.4.3 Task-dependency Gantt tracker

**Data model**
- Persist `depends_on` edges between IRP tasks; validate acyclicity on save.
- Compute critical path server-side (longest-duration chain); expose start/end/slack per task.

**UI**
- Gantt panel on the incident view (custom Avalonia control or `TreeDataGrid`-based timeline): bars colored by status, critical-path tasks highlighted, overdue in the standard alert color, dependency arrows, today-line marker.
- A blocked task (incomplete dependency) cannot be completed without an explicit override (recorded with who/why).

**Acceptance criteria**
- A 10-task plan with branching dependencies renders; the computed critical path matches manual calculation; completing a predecessor visibly unblocks successors.

**Sources (2.4):** [Swimlane — SOAR playbooks](https://swimlane.com/blog/soar-playbooks/) · [Radiant Security — SOAR playbook tips](https://radiantsecurity.ai/learn/soar-playbooks-key-functions-types-examples-and-tips-for-success/) · [Splunk — SOAR overview](https://www.splunk.com/en_us/blog/learn/soar-security-orchestration-automation-response.html)

---

## Dependencies & sequencing

- **2.3 (entity scoping)** should precede or be co-designed with Track 3.5.1 (API-token entity binding) and Track 4.3 (IdP group → entity mapping).
- The notification halves of **2.4.2** ride on Track 4.1 channels (email-only until then).
- **2.1.1 templates** are a prerequisite for **2.1.2 schedules**.
