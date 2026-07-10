# Track 8 — Risk Governance & Approval Workflows: Detailed Specifications

> Status: **Planned** · Roadmap: [ROADMAP.md → Track 8](../../ROADMAP.md)
> Research basis: deep-research pass (July 2026) comparing NetRisk against ISO/IEC 27001:2022 (clause 6.1.3), NIST SP 800-37 Rev. 2 / SP 800-30 Rev. 1, COSO ERM (2017), DORA (Art. 6), SOC 2 (CC3.x), the peer-reviewed risk-matrix literature (Cox 2008; Thomas/Bratvold/Bickel 2014; Krisper 2021; Hubbard & Seiersen), FAIR / Open Group O-RT, and approval-workflow patterns documented for ServiceNow IRM and eramba. Sources at the end of each section.

NetRisk's risk module implements a solid SimpleRisk-style qualitative lifecycle (`New → Mitigation Planned → Mgmt Reviewed → Closed`) with severity-banded reviewer permissions and severity-driven review cadences. What it lacks is the **governance layer** that auditors and regulators actually test: a formal, expiring risk-acceptance artifact; residual-vs-inherent risk; multi-level, segregated approvals; a field-level audit trail; proactive review notifications; and a business-facing review surface. This track closes those gaps and adds a dedicated **Business Risk Acceptance Portal** web application.

---

## The Gap: NetRisk vs. risk-governance best practice

Each gap below records what best practice expects (with sources), what NetRisk does today (with code references), and the target state. The milestones in this document map 1:1 onto these gaps.

### G1 — No formal risk-acceptance artifact with expiry *(closed by 8.1)*

**Best practice.** ISO/IEC 27001:2022 clause 6.1.3 requires the organization to obtain **risk owners' formal approval of the risk treatment plan and formal acceptance of residual risks**, documented with rationale and an attributable trail — auditors sample exactly the "risk register, treatment plan, SoA, residual approvals" evidence set. Mature GRC platforms model acceptance as a first-class, **time-bound** record: ServiceNow IRM ships an out-of-the-box "Risk acceptance approval" workflow and time-bound policy exceptions (typically 30/90 days) that expire and are monitored; eramba's exception management attaches an owner and a review/expiry date to each exception linked to the risk.

**NetRisk today.** "Acceptance" is only `PlanningStrategy = Accept` on the mitigation (`src/DAL/Entities/Mitigation.cs`) plus a `MgmtReview` with next step "Accept until Next Review" (`src/DAL/Entities/MgmtReview.cs`, `src/DAL/Entities/NextStep.cs`). There is no authorizing manager, no justification field, no expiration date, and nothing reopens the risk when the acceptance lapses. (Track 3.2 already planned a `RiskAcceptance` entity for *findings*; this track generalizes it.)

**Target.** A `risk_acceptances` entity capturing who authorized (with authority checks), business justification, the residual score at acceptance time, and an expiry date; a Hangfire job that reopens/notifies on expiry; full history retained.

### G2 — No residual vs. inherent risk *(closed by 8.2)*

**Best practice.** ISO 27001 auditors verify **pre/post-treatment risk scores demonstrating mitigation effectiveness**; threshold-escalated approval routing in GRC platforms keys off the *residual* score. NIST SP 800-30 distinguishes assessed risk before and after response.

**NetRisk today.** One score (`RiskScoring.CalculatedRisk` + vulnerability-driven `ContributingScore`, combined in `src/Tools/Risks/RiskCalculationTool.cs`) and a free `Mitigation.MitigationPercent`. The `next_review_date_uses = InherentRisk` setting hints at the concept, but no residual score exists anywhere.

**Target.** Persisted inherent and residual scores, residual historized alongside `RiskScoringHistory`, surfaced in GUI, reports, and used by 8.3 escalation and 8.1 acceptance records.

### G3 — Single-step approval; no segregation of duties or escalation *(closed by 8.3)*

**Best practice.** NIST SP 800-37 Rev. 2 models authorization as an **explicit, documented, non-delegable decision by a senior official** over an evidence package (plans, assessment reports, POA&M). DORA Art. 6(4) requires structural **segregation between risk owners and the risk-control function** (three lines of defense). ServiceNow practitioners route acceptance approvals to senior management when thresholds are crossed (e.g., residual rating above a bar, or multi-business-unit impact); sign-off must come from a person with budget and accountability, not "any user".

**NetRisk today.** Any user holding the matching severity-band permission (`review_insignificant` … `review_veryhigh`, `src/API/Security/PermissionPolicyProvider.cs`) can single-handedly approve. Nothing prevents a risk's own submitter/owner/manager from reviewing their own risk; admins bypass all checks. There is no second-level sign-off and no threshold-based escalation.

**Target.** Maker-checker rule (reviewer ≠ submitter/owner/manager), configurable dual-approval above a severity/appetite threshold, and risk-appetite settings that drive routing (see G8).

### G4 — No server-side workflow state machine *(closed by 8.3)*

**Best practice.** Auditors expect workflow evidence to be **enforced**, not conventional; SOC 2 CC3.2 expects a documented, consistently applied assessment/treatment process.

**NetRisk today.** Except for the close/reopen guards in `RisksController` (`CloseRisk` rejects double-close; `ReopenRisk` requires Closed), status transitions are set client-side on save (`RisksService.SaveRisk` persists whatever status the client sends). A risk can reach `Closed` without any management review, or sit in `Mitigation Planned` with no mitigation row.

**Target.** `RisksService` validates every transition against an explicit state machine (e.g., `Closed` requires a `MgmtReview`; `MitigationPlanned` requires a `Mitigation`), returning domain errors the GUI surfaces.

### G5 — No field-level audit trail *(closed by 8.4)*

**Best practice.** Auditors expect a complete, exportable trail where **every risk-treatment action, approval, and exception is time-stamped and attributable to a person**. DORA Art. 6(6)–(7) additionally requires auditable records plus formal follow-up of findings to closure. (This exact deficiency — approval status changes not captured in history — is what pushed eramba users away from custom-field workarounds.)

**NetRisk today.** Approvals persist as `MgmtReview` rows; everything else is Serilog lines (`Logger.Information("User:{UserValue}…")`). There is no per-field change history on risks/mitigations/reviews — nobody can answer "who lowered this impact from 4 to 2, and when". `RiskScoringHistory` covers scores only.

**Target.** A generic EF `SaveChanges`-interceptor audit log (entity, field, old/new value, user, UTC timestamp) covering the risk-governance aggregate, plus an auditor evidence export.

### G6 — Review cadence is pull-only *(closed by 8.5)*

**Best practice.** DORA Art. 6(5) mandates review **at least annually and after major incidents** (event-triggered); every commercial GRC tool notifies owners of overdue reviews and expiring exceptions.

**NetRisk today.** The machinery exists — `ReviewLevel` seeds cadence by severity (Very High = 30d … Insignificant = 360d), `MgmtReview.NextReview` stores the date, and `RisksService.GetToReview` / `GetRisksNeedingReview` find stale risks — but nothing *pushes*: no Hangfire job emails or alerts anyone. Overdue reviews are only visible if someone opens the right screen.

**Target.** Scheduled notification jobs (overdue reviews, expiring acceptances) over the Track 4.1 `INotificationChannel` abstraction, plus event-triggered review requests (e.g., linked critical vulnerability or incident).

### G7 — Matrix-only scoring with a mathematically shaky composite *(addressed by 8.7)*

**Best practice.** The peer-reviewed literature (Cox 2008 *Risk Analysis*; Thomas/Bratvold/Bickel 2014; Krisper 2021) shows ordinal likelihood×impact matrices suffer range compression, risk inversion, ranking reversal, and undefined ordinal arithmetic, with no published empirical evidence they improve decisions; research (Budescu et al.) also shows raters substitute their own meanings for verbal labels unless levels carry explicit quantitative definitions. FAIR (Open Group O-RT) is the documented quantitative alternative: calibrated range inputs, Monte Carlo simulation, loss-exceedance output. Matrices remain acceptable for *triage and communication* — not as the sole prioritization engine.

**NetRisk today.** Only `ScoringMethod = 1` (Classic matrix) is ever written; `Likelihood`/`Impact` levels are bare labels with no quantitative anchors; and `CalculateTotalRiskScore = (calculated + 2·contributing) / 3` (`src/Tools/Risks/RiskCalculationTool.cs`) averages an ordinal matrix output with a CVSS-derived quantity — non-commensurate units.

**Target.** Quantitative anchors on every likelihood/impact level (shown at rating time), documentation of the composite as a triage heuristic, and a FAIR-lite quantitative `ScoringMethod` option (range inputs + Monte Carlo + loss-exceedance view) — a genuine differentiator among open-source GRC tools. Dropping the matrix is *not* proposed.

### G8 — No risk appetite/tolerance model *(closed by 8.3)*

**Best practice.** COSO ERM (2017) requires risk appetite aligned to strategy; DORA Art. 6(8) requires the risk-tolerance level to be an explicit, documented artifact. In tooling, appetite is what turns scores into behavior ("risks above X cannot be accepted below role Y").

**NetRisk today.** `RiskLevel` thresholds (Low/Medium/High/Very High) are display bands only — they color the GUI and pick review cadence but gate nothing.

**Target.** Appetite thresholds (global and per business entity) that drive 8.3 approval routing and 8.1 acceptance authority, and appear on dashboards ("N risks above appetite").

### G9 — Broken intake and thin treatment plans *(closed by 8.5 / 8.1)*

**Best practice.** NIST RMF documents remediation as a **POA&M** — tasks with milestones, owners, and dates; ISO auditors want treatment plans with timelines, responsibilities, and status. Risk identification inputs should flow into the register.

**NetRisk today.** `PendingRisk` rows are created from assessment answers (`AssessmentAnswer.SubmitRisk`) but **no live code promotes them to risks** — a dead intake pipeline. `Mitigation` has a single `PlanningDate` and a percent; no milestones/tasks.

**Target.** A pending-risk triage endpoint + GUI (promote/dismiss), and mitigation task line-items with owner/due-date/status (POA&M-style), which the portal (8.6) can assign work through.

**Sources (gap analysis):** [NIST SP 800-30 Rev. 1](https://csrc.nist.gov/pubs/sp/800/30/r1/final) · [NIST SP 800-37 Rev. 2](https://csrc.nist.gov/pubs/sp/800/37/r2/final) · [COSO ERM guidance](https://www.coso.org/guidance-erm) · [DORA Art. 6](https://www.digital-operational-resilience-act.com/Article_6.html) · [Cox 2008 — What's Wrong with Risk Matrices?](https://onlinelibrary.wiley.com/doi/10.1111/j.1539-6924.2008.01030.x) · [Thomas/Bratvold/Bickel 2014 — The Risk of Using Risk Matrices](https://maverisk.nl/wp-content/uploads/TheRiskofUsingRiskMatrices.pdf) · [Krisper 2021 — Problems with Risk Matrices](https://arxiv.org/pdf/2103.05440) · [Open Group O-RT (FAIR taxonomy)](https://pubs.opengroup.org/security/o-rt/) · [SOC 2 risk-assessment criteria (Linford & Co)](https://linfordco.com/blog/soc-2-risk-assessment-criteria/) · [ISO 27001 clause 6.1.3 guidance](https://hightable.io/iso-27001-clause-6-1-3-information-security-risk-treatment/) · ServiceNow community threads on risk-acceptance workflows and eramba forum threads on exception approvals (platform-behavior claims are community-sourced, not vendor-verified).

---

## Milestone 8.1: Formal Risk Acceptance & Time-Bound Exceptions

Generalizes (and supersedes) the finding-scoped `RiskAcceptance` planned in Track 3.2.3/3.2.4 — one entity serves both risks and findings.

### 8.1.1 `risk_acceptances` entity (Track 6-compliant schema)

- Table `risk_acceptances`: `id`, `risk_id` (FK `fk_risk_acceptances_risk_id`, nullable), `vulnerability_id` (FK, nullable — finding-level acceptance for Track 3.2), `accepted_by_id` (FK → `user`, the authorizing manager), `requested_by_id` (FK → `user`), `justification` (TEXT, required), `residual_score_at_acceptance` (float), `start_date`, `expiry_date` (DATETIME NOT NULL — acceptance without expiry is not representable), `status` (int enum: `Active`, `Expired`, `Revoked`, `Renewed`), `revoked_by_id`/`revoked_at`/`revocation_reason`, `created_at`/`updated_at` (UTC). Exactly one of `risk_id`/`vulnerability_id` must be set (CHECK + service validation).
- EF entity + navigations, Mapster DTOs, numbered `Structure`/`Data` SQL per the CLAUDE.md two-step migration ritual.
- Constraint: creating an acceptance requires the caller to hold the `review_*` permission matching the risk's **residual** severity band (from 8.2), and — once 8.3 lands — to satisfy appetite/escalation rules.

### 8.1.2 Lifecycle service + API

- `IRiskAcceptancesService`: `Create`, `Renew` (new row linked to predecessor, fresh justification required), `Revoke`, `GetByRisk`, `GetExpiring(days)`. Accepting sets the risk's planning strategy/next step coherently and writes a `MgmtReview` row so the existing review history stays the single approval timeline.
- API: `POST/GET /risks/{id}/Acceptances`, `PUT /risks/{id}/Acceptances/{aid}/Revoke`, `GET /riskacceptances/expiring?days=n`. Policy: `RequireMgmtReviewAccess` + band check.

### 8.1.3 Expiry automation

- Hangfire job (daily): acceptances past `expiry_date` → status `Expired`, risk reopened to `Management Review` needed state, owner + acceptor notified (via 8.5 channels). Warning notifications at T-30/T-7 days.
- `GET /risks/ToReview` and the GUI risk list gain an "acceptance expiring/expired" flag.

### 8.1.4 GUI

- Risk edit view: "Acceptance" panel (current acceptance, history, renew/revoke) replacing the ambiguous reliance on `PlanningStrategy = Accept` alone.
- Tests: `ServerServices.Tests` (authority checks, expiry job, renew chain), `API.Tests` (policies), `ClientServices.Tests`.

**Acceptance criteria:** a risk can only be in an "accepted" state with a live `risk_acceptances` row naming an authorizer with adequate authority, a justification, and a future expiry; expiry provably reopens and notifies; the full chain is exportable (8.4).

---

## Milestone 8.2: Inherent vs. Residual Risk

### 8.2.1 Model & calculation

- Add `residual_risk` (float) to `risk_scorings` (+ snapshot column in `risk_scoring_histories`). v1 formula: `residual = inherent × (1 − effective_mitigation_percent)`, where `effective_mitigation_percent` derives from `Mitigation.MitigationPercent` and validated control percentages (`MitigationToControl.ValidationMitigationPercent`); document the formula and keep it swappable (strategy interface) so 8.7 quantitative scoring can supply its own residual.
- `RiskCalculationService` computes both on its existing Hangfire schedule; `next_review_date_uses` setting now actually selects inherent vs residual for cadence.

### 8.2.2 Surfacing

- GUI risk list/edit: both scores + delta; heatmap gains an inherent/residual toggle (`StatisticsService`).
- Reports: pre/post-treatment scores per risk (the ISO 27001 evidence item); entity PDF report updated.

**Acceptance criteria:** every scored risk exposes inherent and residual values with history; acceptance records (8.1) snapshot residual; auditors can pull a pre/post-treatment table per entity.

---

## Milestone 8.3: Approval Workflow Engine — State Machine, Segregation of Duties, Escalation, Appetite

### 8.3.1 Server-side state machine (G4)

- Transition table enforced in `RisksService` (single choke point; controllers stay thin): `New → MitigationPlanned` requires a mitigation; `→ ManagementReview` requires a `MgmtReview`; `→ Closed` requires latest review outcome ≠ "Request Risk review" or an active acceptance; `Closed → MitigationPlanned` only via the existing reopen path. Invalid transitions throw a domain exception surfaced by the GUI.
- Legacy data: risks in states that violate the machine are flagged (report), never auto-mutated.

### 8.3.2 Segregation of duties / maker-checker (G3)

- Rule: the acting reviewer of a `MgmtReview` (and the authorizer of a `RiskAcceptance`) must not be the risk's `SubmittedBy`, `Owner`, or `Manager`. Enforced in `MgmtReviewsService`/`RiskAcceptancesService`; **admins do not bypass** (configurable break-glass setting, off by default, logged loudly when used).
- New seeded setting group `risk_workflow` in `settings`.

### 8.3.3 Risk appetite & threshold escalation (G8)

- `risk_appetites` table: scope (global / `entity_id`), `max_acceptable_residual` (float), `dual_approval_threshold` (float), `created_at`/`updated_at`; admin GUI to manage.
- Behavior: residual above `dual_approval_threshold` ⇒ acceptance/approval requires a **second, distinct** approver holding the top band (`review_veryhigh`); residual above `max_acceptable_residual` ⇒ acceptance blocked (must mitigate or raise appetite — an explicit, audited act).
- Dashboards/reports: "risks above appetite" count per entity.

### 8.3.4 Second-approval plumbing

- `mgmt_reviews` gains `second_reviewer_id` (FK, nullable) + `second_review_at`; a review awaiting counter-signature holds the risk in `ManagementReview` state. API: `POST /risks/{id}/MgmtReviews/{rid}/Countersign`.

**Acceptance criteria:** no self-approval path exists (test-proven, including admin); above-threshold acceptances demonstrably require two distinct qualified approvers; appetite breaches block acceptance and are visible on dashboards.

---

## Milestone 8.4: Field-Level Audit Trail & Auditor Evidence Export

### 8.4.1 Change auditing (G5)

- EF Core `SaveChangesInterceptor` in `DAL` writing `audit_logs`: `id`, `entity_type`, `entity_id`, `field`, `old_value`, `new_value` (varchar/TEXT), `user_id` (FK), `occurred_at` (UTC), `correlation_id`. Scope v1: `risks`, `risk_scorings`, `mitigations`, `mgmt_reviews`, `risk_acceptances`, `risk_appetites` (allowlist, not global — keeps volume sane).
- The acting user flows from the API auth context into `NRDbContext` (ambient current-user accessor) so writes are attributable end-to-end, including background jobs (system user).
- Indexes: `idx_audit_logs_entity_type_entity_id`, `idx_audit_logs_occurred_at`. Retention policy setting + cleanup job.

### 8.4.2 Evidence export (auditor pack)

- `GET /risks/{id}/AuditTrail` + report: per entity/period, export (PDF/CSV via the 2.1 engine) of: register with inherent/residual, treatment plans, review history with reviewers/outcomes, acceptances with authorizers/expiry, and the field-level trail — the ISO 27001 / SOC 2 / DORA evidence folder generated from live data instead of screenshots.

**Acceptance criteria:** for any governance record, "who changed what, when" is answerable from the DB and exportable; MgmtReview/acceptance decisions are attributable without consulting Serilog files.

---

## Milestone 8.5: Review Cadence Automation & Intake Repair

### 8.5.1 Overdue-review notifications (G6)

- Daily Hangfire job over the existing `GetToReview`/`GetRisksNeedingReview` + `ReviewLevel` cadence: notify risk owner/manager on overdue or never-reviewed risks; digest mode per user. Built on Track 4.1 `INotificationChannel` (email first; Slack/Teams/webhook arrive with Track 4).
- Event-triggered reviews (DORA-style): a new linked Critical vulnerability or incident on a risk flags it "review requested" regardless of cadence.

### 8.5.2 Pending-risk triage (G9)

- Implement the missing `PendingRisk → Risk` promotion: `IRisksService.PromotePendingRisk(pendingId, edits)` and `DismissPendingRisk(pendingId, reason)`; API `POST /risks/pending/{id}/promote|dismiss`; GUI triage list under Risks. Promotion carries assessment linkage for traceability.

### 8.5.3 Mitigation task line-items (POA&M-lite, G9)

- `mitigation_tasks`: `id`, `mitigation_id` (FK), `title`, `owner_id` (FK), `due_date`, `status` (int enum), `completed_at`, `created_at`/`updated_at`. Surfaced in the mitigation GUI; overdue tasks feed the same notification job; the portal (8.6) creates these when a reviewer chooses "mitigate".

**Acceptance criteria:** an overdue review provably produces a notification without anyone opening the app; assessment-generated risks reach the register through the GUI; mitigation work is trackable to named owners and dates.

---

## Milestone 8.6: Business Risk Acceptance Portal (web application)

A lightweight, business-facing web app where **risk persons appointed by each business entity** (one or more per entity) periodically review, **rank**, and decide on their entity's risks — accepting them (via 8.1) or commissioning mitigation work (via 8.5.3) — without needing the desktop GUI or GRC training. This is the "second line writes, first line decides" surface that DORA's three-lines model and ISO 27001's risk-owner-approval requirement both point at.

> **Not the existing `WebSite`.** `WebSite` is deliberately DB-decoupled (SQLite + signed periodic sync) and must stay that way; the portal is a **new project** with live, authenticated API access.

### 8.6.1 Project & architecture

- New project `src/RiskPortal` — ASP.NET Core (Razor Pages or Blazor Server; decide at implementation via a short ADR) consuming the existing REST `API` through `ClientServices`-style typed clients (no direct DAL reference — same layering rule as GUIClient).
- Deployment: standalone Kestrel service beside the API; Nuke targets `CompileRiskPortal` / `PackageRiskPortal`; config via user-secrets/env (`Server:Url`).
- Authentication: existing NetRisk accounts (and SSO once Track 4.3 lands); mobile-friendly responsive layout (executive audience) — this also satisfies part of the "Mobile Companion App" idea for risk sign-off.

### 8.6.2 Reviewer designation & scoping

- `entity_risk_reviewers`: `id`, `entity_id` (FK), `user_id` (FK), `is_primary` (tinyint(1)), `appointed_by_id` (FK), `created_at`. One or more reviewers per business entity, managed by entity admins in the desktop GUI.
- New permission `business_risk_review`; portal access is entity-scoped through the Track 2.3 RBAC (a reviewer sees only their entities' risks). SoD rules from 8.3 apply — a reviewer cannot decide a risk they own/submitted.

### 8.6.3 Periodic review campaigns

- `risk_review_campaigns`: `id`, `entity_id` (FK), `period_start`/`period_end`, `due_date`, `status` (int enum: `Open`, `Completed`, `Overdue`, `Cancelled`), `created_at`. `risk_review_campaign_items`: `id`, `campaign_id` (FK), `risk_id` (FK), `rank` (int — the reviewer's business-priority ordering), `decision` (int enum: `Pending`, `Accepted`, `MitigationRequested`, `Escalated`), `decision_notes`, `decided_by_id` (FK), `decided_at`.
- Hangfire job generates campaigns per entity on a configurable cadence (default quarterly; per-entity override), pre-populated with the entity's open risks — ordered by residual score — plus anything overdue per `ReviewLevel` or with an expiring acceptance. Reviewers are notified with a deep link (8.5 channels).

### 8.6.4 Reviewer experience

- Dashboard: pending campaigns, progress (n of m decided), due date; overdue campaigns re-notify and are visible to admins.
- Review screen per campaign: the entity's risks with subject, category, inherent/residual score and trend (from `RiskScoringHistory`), current mitigation summary, linked open vulnerabilities/incidents; **drag-to-rank** ordering persisted to `rank`.
- Per-risk decision:
  - **Accept** → creates a `RiskAcceptance` (8.1) — justification + expiry mandatory; blocked/escalated by appetite rules (8.3) with a clear explanation when a second approver is needed;
  - **Request mitigation** → creates/updates `mitigation_tasks` (8.5.3) with owner and due date, and flags the risk's manager;
  - **Escalate** → routes the item to a named senior approver with a note (for risks the reviewer can't decide).
- Completing all items closes the campaign and writes a summary `MgmtReview` per decided risk, so the desktop review history and the portal are one timeline.

### 8.6.5 Governance outputs

- `rank` is stored as the entity's **business priority** and surfaced in the desktop GUI risk list and reports (sortable column) — business ranking finally feeds technical prioritization.
- Campaign results feed the 8.4 evidence export: "entity X's appointed reviewers reviewed N risks on date D; decisions and justifications attached" — precisely the periodic-review evidence ISO 27001/DORA auditors request.
- Statistics: campaign completion rates, decision mix, average time-to-decide per entity.

### 8.6.6 Tests & security

- `RiskPortal` UI logic covered like GUIClient VMs; campaign/decision services in `ServerServices.Tests`; API policies in `API.Tests`; the portal goes through the Track 7 controller-authorization sweep before first release (new outward-facing surface).

**Acceptance criteria:** an entity admin can appoint reviewers; campaigns auto-generate and notify; a reviewer can complete an entire periodic review (rank + accept/mitigate/escalate every risk) from a browser; every decision materializes as first-class records (acceptance, tasks, reviews) visible in the desktop app and the audit trail.

---

## Milestone 8.7: Quantitative Scoring Option (FAIR-lite) & Scale Anchors

### 8.7.1 Quantitative anchors on qualitative scales (cheap, do first)

- Add `definition` (TEXT) + optional numeric bounds (`probability_min/max`, `impact_min/max` monetary) to the `likelihood`/`impact` lookup tables; GUI shows the definition at rating time. Rationale: research shows raters substitute their own meanings for bare labels (Budescu et al.; Cox 2008).
- Document `CalculateTotalRiskScore` as a triage heuristic in [docs/features/risk-management.md](../features/risk-management.md) (it mixes ordinal and CVSS scales); revisit the weighting once residual (8.2) exists.

### 8.7.2 FAIR-lite scoring method

- New `ScoringMethod = 3 (Quantitative)`: per-risk inputs as calibrated ranges — loss-event frequency (min/most-likely/max, events/year) and loss magnitude (min/most-likely/max, currency) — per the Open Group O-RT taxonomy.
- Monte Carlo engine in `Tools` (PERT/triangular sampling, ≥10k iterations, seeded/reproducible): outputs annualized-loss-exposure percentiles (P10/P50/P90) and a loss-exceedance curve; results cached on `risk_scorings` (new columns), recomputed by the existing calculation job on input change.
- GUI: quantitative input editor + LEC chart; risks scored quantitatively map into `RiskLevel` bands via configurable monetary thresholds so lists/heatmaps/appetite rules keep working; before/after-mitigation re-run for control-ROI comparison.
- Explicitly out of scope: threat-capability/control-strength FAIR sub-factors (full FAIR), calibration training content.

**Acceptance criteria:** a risk can be scored with ranges instead of a matrix cell and produces a defensible loss-exposure distribution; both methods coexist per risk register; every qualitative level carries a written definition.

---

## Sequencing & dependencies

```
8.1 Acceptance ──┐
8.2 Residual ────┼──► 8.3 Workflow engine ──► 8.6 Portal
                 │         (appetite, SoD)        ▲
8.4 Audit trail ─┘                                │
8.5 Cadence & intake ─────────────────────────────┘
8.7 Quantitative (independent; anchors 8.7.1 can ship any time)
```

- **8.1 + 8.2 first** (auditor-facing, roadmap-validated, schema-heavy — get the Track 6-compliant entities right early). 8.4 can proceed in parallel (pure infrastructure).
- **8.3** needs residual (routing key) and benefits from acceptance existing.
- **8.5** is independent except 8.5.3 which 8.6 consumes.
- **8.6 (portal)** consumes 8.1 (accept), 8.3 (appetite/SoD), 8.5.3 (tasks) — schedule last in the governance cluster.
- **8.7** is methodologically independent; 8.7.1 (anchors) is small and can ship with any milestone.

Cross-track: notifications ride Track 4.1; entity scoping rides Track 2.3 (backend done); the portal enters the Track 7 security-audit scope; all new schema follows Track 6 conventions and the numbered-SQL upgrade path.
