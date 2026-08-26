# Risk Governance, Approval Workflows and the Business Review Portal

Track 8. This is the layer that turns NetRisk's risk lifecycle into something an ISO 27001 / SOC 2 /
DORA auditor can sample: formal expiring acceptance, inherent versus residual scoring, enforced
approvals, a field-level trail, pushed review cadence, and a portal where the business — not the
security team — decides its own risks.

Related: [Risk Management](risk-management.md) · [Reports](reports.md) ·
[Notification channels](notification-channels.md) · [Security posture](../security/README.md)

---

## 1. Formal risk acceptance

An acceptance is a record, not a status. [`RiskAcceptance`](../../src/DAL/Entities/RiskAcceptance.cs)
carries the authorizing manager, the business justification, the compensating controls, a **snapshot
of the residual score at the moment of the decision**, and a **mandatory expiry**. The snapshot
matters: an acceptance is a decision about the risk as it was understood then, and a register that
re-reads today's score cannot show what was actually agreed.

| Operation | Where | Notes |
|---|---|---|
| Create | `POST /Risks/{id}/Acceptance` | Severity-band authority check; writes a `MgmtReview` so the timeline is shared |
| Renew | `POST /Risks/Acceptances/{id}/Renew` | Creates a successor linked by `renewed_from_id`; a revoked acceptance cannot be renewed |
| Revoke | `POST /Risks/Acceptances/{id}/Revoke` | Requires a reason, which is exported in the evidence pack |
| Expiring | `GET /Risks/Acceptances/Expiring` | Feeds the warning ladder and the dashboards |

[`RiskAcceptancesService`](../../src/ServerServices/Governance/RiskAcceptancesService.cs) runs the
expiry pass daily at 06:15. It warns at **T-30 and T-7** and reopens the risk when the acceptance
lapses. The warning ladder takes the *tightest* applicable threshold, not the first matching one —
the naive version warns at T-30 and then never fires T-7, which the test suite caught.

**Authority bands** come from the seeded `risk_levels` rows via `ResolveBand`. That table is keyless,
so the in-memory tests exercise the pure function and the end-to-end path is covered by
`DAL.IntegrationTests`.

## 2. Inherent versus residual

`risk_scoring` gained `residual_risk` (with history on `risk_scoring_history`), computed by a
swappable strategy. The shipped one,
[`MitigationPercentResidualStrategy`](../../src/ServerServices/Governance/MitigationPercentResidualStrategy.cs),
composes multiple mitigations as **1 − Π(1 − pᵢ)**: two 60 % controls give 84 %, not 120 %. Summing
is the obvious wrong answer and it produces residual scores below zero.

`next_review_date_uses` selects which score drives the review cadence. **The setting was not dormant —
it was gone.** Seeded in db_version 1, deleted in db_version 29, re-created in 80. Anything other than
`ResidualRisk` means inherent, deliberately: the setting is user-editable, and an installation that
types "residual" should get the conservative cadence rather than a crash in the morning job.

**Where both scores appear:** the register list (`8.0 → 2.0 (−6.0)`), the risk detail panel, and a
per-entity pre/post-treatment table in the Detailed Entities Risks report, ordered by smallest
reduction first.

**Where they do not:** the Impact-vs-Probability heatmap's *points do not move* when the residual
toggle is on. The axes are the matrix's likelihood and impact ratings; a residual score is a single
derived number with no likelihood/impact decomposition, so plotting it in a cell would put a risk
somewhere nobody rated it. The toggle changes which score is filtered and labelled, and a risk with no
residual is omitted from the residual view rather than drawn at its inherent position — that would
read as "treated to no effect".

## 3. The approval workflow

[`RiskWorkflowService`](../../src/ServerServices/Governance/RiskWorkflowService.cs) enforces three
things server-side, each switchable by a setting so an installation can adopt them in order.

**The state machine** (`risk_workflow_state_machine_enforced`) refuses `Closed` without a review and
`Mitigation Planned` without a mitigation, answering 422. One deliberate exception: `ReopenRisk` stays
on the unguarded save, because a reopen has to work on a legacy risk whose current status the machine
would refuse. Existing violations are reported by `FindLegacyViolationsAsync` and shown in the
governance admin screen instead of blocking work.

**Segregation of duties** (`risk_workflow_segregation_of_duties`) refuses a reviewer or acceptor who
is the submitter, the owner or the manager — **administrators included**. Break-glass
(`risk_workflow_segregation_break_glass`) requires a written reason, which is persisted on
`mgmt_reviews.segregation_override_reason` and exported in the evidence pack. An override nobody can
find afterwards is not an override, it is a bypass.

**Risk appetite** — [`RiskAppetite`](../../src/DAL/Entities/RiskAppetite.cs), global or per entity —
carries a dual-approval threshold and a hard acceptance ceiling. **No row is seeded.** A default
threshold is a policy decision made by whoever wrote the installer, and it would silently start
refusing acceptances an organisation never agreed to. With no row configured, gating is inactive and
the admin screen says so.

## 4. The audit trail

[`GovernanceAuditInterceptor`](../../src/DAL/Auditing/GovernanceAuditInterceptor.cs) is an EF
`SaveChanges` interceptor, so no service can forget to call it. One `audit_logs` row per changed
field, one summary row for a create or a delete, and a correlation id per save so a multi-row change
reads as one act.

It covers an **allowlist** of nine types — `Risk`, `RiskScoring`, `Mitigation`, `MitigationTask`,
`MgmtReview`, `RiskAcceptance`, `RiskAppetite`, `RiskReviewCampaignItem`, `EntityRiskReviewer`. Not
everything: a trail over a vulnerability import would write millions of rows nobody reads, and a
table that large stops being queryable, which defeats the purpose.

Attribution is end to end. `AuditableContext.AuditActor` carries the API user; background jobs write
as the `system` actor rather than as nobody. Retention defaults to 1 825 days
(`audit_log_retention_days`) and is **applied** by a nightly job — a documented retention policy that
is not implemented is worse than none, because it tells an operator the data is gone when it is not.

## 5. The evidence pack

`AuditTrailService.GetEvidencePackAsync` assembles one `GovernanceEvidencePack` per entity and period,
and **both output paths render the same object**, so the CSV and the PDF cannot describe different
evidence.

| Format | Route | What it is |
|---|---|---|
| CSV | `GET /AuditTrail/Evidence/Report?format=csv` | Four labelled sections plus a scope block; RFC 4180 quoting, leading `=`/`+`/`-`/`@` neutralised |
| PDF | `GET /AuditTrail/Evidence/Report?format=pdf` | Rendered by the 2.1 engine as `Report.Type == 3`, stored as an `NrFile` and listed with every other report |
| JSON | `GET /AuditTrail/Evidence` | The raw trail, for a client that wants to render its own |

Three selection rules are load-bearing, and each has a plausible wrong version:

* an acceptance **granted before the period and still in force** is included — filtering on
  `created_at` omits every standing exception, which is the single most relevant fact about a posture;
* a campaign item **nobody decided** is included — filtering on `decided_at` makes an unreviewed
  quarter read as a completed review with no risks in it;
* a change list **cut short by the row limit says so** — silently returning a full page reads as a
  complete trail.

The desktop reaches it through the Create Report dialog, which reveals an entity picker and a period
for this report only, defaulting to the last twelve months in UTC.

## 6. Review cadence and intake

`RiskReviewCadenceJob` (07:30 daily) walks the open register against the review-level cadence and
notifies through the Track 4.1 channels. It runs **after** both expiry passes, so a risk whose
acceptance lapsed overnight is in this morning's list rather than tomorrow's.

A risk that has never been reviewed becomes overdue **one cadence interval after submission**, not
immediately. The alternative makes the first notification cover the entire register, which is how a
notification channel gets muted.

`mitigation_tasks` are POA&M-style line items with an owner, a due date and a status, feeding the same
notifications. `PendingRisk` records can finally be **promoted** into a real risk (creating the risk
and its scoring in one transaction, linked back to the assessment answer) or **dismissed** with a
reason — before this, nothing promoted them and they accumulated indefinitely.

## 7. The business review portal

[`src/RiskPortal`](../../src/RiskPortal/README.md) is a separate ASP.NET Core Razor Pages application.
It consumes the REST API only and shares no database access; the DB-decoupled `WebSite` is untouched.
`CompileRiskPortal` and `PackageRiskPortal` build it.

**Who can use it.** `entity_risk_reviewers` appoints one or more reviewers per business entity.
Appointment also ensures a `user_entity_roles` row, because Track 2.3 scopes by that table — without
it the appointment grants nothing, which is a defect that only appeared when the portal was actually
run. Reviewers hold `business_risk_review` and deliberately **not** `riskmanagement`, so they cannot
read the register-wide `/Risks/Scores` or `/Risks/{id}/Appetite`; everything they need arrives through
`GET /RiskReviewCampaigns/{id}/Items`, which keeps them scoped to the campaign they were appointed to.

**Campaigns** are generated daily at 08:00 on calendar-aligned periods (quarterly by default, per-entity
override) with a unique `(entity, period)` index — so the job is idempotent by construction rather
than by convention, and a second run does not fill a reviewer's list with duplicates.

**The reviewer flow** is drag-to-rank plus a decision per risk. Ranking is progressive enhancement:
without JavaScript the same order is settable as numbers and posted normally. Each decision writes a
`MgmtReview`, so the desktop and the portal share one approval timeline.

| Decision | Effect |
|---|---|
| Accept | Creates a formal acceptance (§1), refused if it breaches the entity's appetite |
| Request mitigation | Creates `mitigation_tasks` with an owner and a due date (§6) |
| Escalate | Notifies a named senior approver; the escalation target is in the evidence pack |

The business rank is mirrored onto `risks.business_rank`, so it shows in the desktop register list and
in reports.

## 8. Quantitative scoring

**Anchors.** Every likelihood and impact level carries a written definition and a numeric range,
shown under the choice at rating time. A five-point scale labelled only Low/Medium/High is read
differently by different raters — the finding behind Budescu's work on verbal probability and Cox's
2008 critique of risk matrices — and a scale whose levels mean different things to different people
cannot be aggregated. The anchors live on the scale rows, so an installation that rewrites them for
its own appetite gets its own wording with no code change.

**`CalculateTotalRiskScore` is a triage heuristic, not a measurement.**
[`RiskCalculationTool`](../../src/Tools/Risks/RiskCalculationTool.cs) computes
`(calculated + 2 × contributing) / 3` — a weighted average of an ordinal matrix output and a
CVSS-derived quantity. Those are **non-commensurate units**: the matrix score is a rank, and averaging
ranks with a measured quantity produces a number with no dimension. It is useful for sorting a queue
and it is not a quantity to report to a board, put in a threshold, or difference between two periods.
Use the FAIR-lite result below when a number has to mean something.

**FAIR-lite.** `ScoringMethod = 3` takes calibrated ranges for loss-event frequency and magnitude and
runs [`MonteCarloRiskSimulator`](../../src/Tools/Risks/MonteCarloRiskSimulator.cs) — PERT-distributed
magnitude, Poisson event counts, seeded for reproducibility — producing annualized-loss percentiles, a
loss-exceedance curve and a before/after-mitigation comparison. The result maps into the existing risk
bands by monetary threshold (`quantitative_band_thresholds`, default 10 k / 100 k / 1 M).

It maps from the **mean** ALE, not the median. A low-frequency high-impact risk — "once a decade,
eight million" — has a median annualized loss of exactly zero, so a median-based mapping scored it
`0`. That is not a rounding problem; it is the wrong statistic for the question.

## Schema

| Version | Phase | What it adds |
|---|---|---|
| 80 | 11 | `risk_appetites`, `audit_logs`, `mitigation_tasks`; residual and quantitative columns on `risk_scoring`; scale anchors; `next_review_date_uses` re-created |
| 81 | 12 | `entity_risk_reviewers`, `risk_review_campaigns`, `risk_review_campaign_items`; the `business_risk_review` permission |
| 82 | 13 | `revoked_tokens`, `login_attempts`, `nr_files.entity_id` — the schema the deferred Track 7 findings needed |

All three are applied from version 79 against a real MariaDB by
[`Track8GovernanceSchemaTests`](../../src/DAL.IntegrationTests/Track8GovernanceSchemaTests.cs).

## Tests

| Area | Where |
|---|---|
| Workflow, SoD, appetite | `ServerServices.Tests/Track8/RiskWorkflowServiceInMemoryTest` |
| Acceptance lifecycle and expiry | `ServerServices.Tests/Track8/RiskAcceptancesServiceInMemoryTest` |
| Residual and quantitative | `ServerServices.Tests/Track8/ResidualAndQuantitativeInMemoryTest` |
| Intake and treatment tasks | `ServerServices.Tests/Track8/GovernanceIntakeAndTasksInMemoryTest` |
| Campaigns and reviewers | `ServerServices.Tests/Track8/RiskReviewPortalInMemoryTest` |
| Audit trail | `ServerServices.Tests/Track8/GovernanceAuditTrailInMemoryTest` |
| Evidence pack | `ServerServices.Tests/Track8/GovernanceEvidencePackInMemoryTest`, `GovernanceEvidenceReportRenderTest` |
| Monte Carlo | `Tools.Tests/Risks/MonteCarloRiskSimulatorTest` |
| Endpoints | `API.Tests/APITests/Track8ControllersTest`, `GovernanceEvidenceExportTest` |
| Jobs | `BackgroundJobs.Tests/Jobs/Governance/GovernanceJobsTest` |
| Client | `ClientServices.Tests/Services/RiskGovernanceRestServiceTest` |
| Portal | `RiskPortal.Tests` |
| Schema and cadence, real MariaDB | `DAL.IntegrationTests/Track8GovernanceSchemaTests`, `Track8CadenceBasisTests` |

## Known limitations

* The **heatmap toggle does not move points** (§2). Plotting a residual score on likelihood/impact
  axes would require decomposing it, which the model does not do.
* **No appetite is configured out of the box** (§3), so appetite gating does nothing until an
  organisation sets its own threshold. This is deliberate; it is also easy to mistake for a bug.
* The desktop governance surfaces are **compile- and lint-verified only** — the Avalonia GUI cannot be
  launched in the environment this track was built in. The portal *was* run end to end.
* Terminating **another** user's session is still not possible; see the ASVS checklist §3.3.4.
