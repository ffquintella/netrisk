# Archive with Reopening Conditions, Incident Backtesting, the Risk Committee and the Third Line

Track 9, Stage 9.9 (M47). It closes MIGR-TI/IA's decision cycle. The fourth Phase 4 decision, **archive**, becomes a
record: a justification, the conditions that **reopen** it (once), and a **quarterly review**. **Backtesting** checks
incidents and near misses against the register, and the dates decide what was foreseen. The **risk committee** is a
collegiate approver beside the individual authorizing manager. The **third line** (internal audit) is a role that
reads everything and writes nothing.

Full specification, with every decision and its rejected alternative:
[S50 — docs/roadmap/track9/9.9-archive-backtesting-committee.md](../roadmap/track9/9.9-archive-backtesting-committee.md).

Related: [KRIs and reassessment triggers](kri-reassessment-metrics.md) · [Risk governance](risk-governance.md) ·
[Mandatory flags and Gate A](risk-flags-gate-a.md)

---

## 1. The archive

`POST /RiskArchive/Risks/{riskId}` (`RequireCloseRisk`) archives an **open** risk. It takes:

- a justification;
- a closure reason;
- one or more **reopening conditions**. Each condition is one of the six Phase 7 reassessment triggers of Stage 9.8,
  optionally with a sentence saying what specifically would bring the risk back.

Archiving closes the risk through its own closure. Every list, job and gate that skips closed risks therefore skips an
archived one, with no fifth status. Archiving also records the Phase 4 decision "archive" and sets the first review a
quarter away.

It is checked like a closure: Gate A and the Track 8 state machine, which needs a review or a live acceptance. It is
also checked like a decision: the risk's submitter, owner or manager may not archive it, and the third line may not.
It is refused when it watches KRI breaches while a linked KRI is beyond its tolerance, because that condition already
holds.

**Reopening, once.** A reassessment event of a watched type that reaches the archived risk reopens the archive. The
event can reach it in two ways:

- it is declared naming the risk, through `/Monitoring/Reassessment/Events`;
- it is a breach of a KRI linked to the risk.

The reopening happens in the same write as the reassessment trigger. The risk returns to the status it had before it
was archived, the closure goes, the risk is flagged for review, and `risk.archive_reopened` is announced. A later event
reaches an open risk, so it raises an ordinary trigger, not a second reopening. A KRI that stays breached for thirty
days reopens the archive once.

**The quarterly review.** `POST /RiskArchive/Risks/{riskId}/Reviews` (`RequireMgmtReviewAccess`) either keeps the
archive or reopens it. Keeping it moves the next review a quarter on. Keeping it is a decision, so Gate A and
segregation of duties apply. The `RiskArchiveReview` job runs at 07:15, after the KRI evaluation and before the review
cadence. It announces each due review once per due date (`risk.archive_review_due`).

A person can also reopen the archive with `POST /RiskArchive/Risks/{riskId}/Reopen`, giving a reason.

If the risk is reopened through `DELETE /Risks/{id}/Closure`, the archive becomes **superseded**. The record stays,
but it no longer governs the risk.

## 2. Backtesting

`PUT /Backtesting/Incidents/{id}` (`RequireSubmitRisk`) records which registered risks describe an incident or near
miss. To record that none does, send an empty list and `noCorrespondingScenario: true`. An empty list on its own is
never taken as an answer.

The outcome is computed, never declared:

| Outcome | When |
|---|---|
| Not assessed | nobody has assessed it yet; it counts as neither foreseen nor unforeseen |
| Not foreseen | assessed, and no risk matches |
| **Registered after the occurrence** | every matched risk was registered at or after the incident; counted as **not foreseen** |
| Foreseen and treated | a risk registered **before** the incident matches, and it was not cut when the incident happened |
| Foreseen but dismissed | every matching risk registered before the incident had been cut (archived, closed, accepted, or decided "monitor/accept" or "archive"); this is a **false negative** of the cut |

The occurrence is the earliest of the incident's start, report and creation dates.

`GET /Backtesting` reports a period (the last year by default) with these figures:

- the counts by outcome;
- the **unforeseen rate**;
- the **false-negative rate**;
- the share of incidents assessed.

The outcome does not depend on who asks. A matched risk outside the caller's scope is counted but not shown. The
metrics panel's M9 (`GET /Monitoring/Metrics`) is now computed from this report and from the archives reopened in the
period.

## 3. The risk committee

An administrator constitutes a committee (`/RiskCommittees`, `RequireAdminOnly`). A committee has members and requires
*k* approvals, between 2 and 50. It belongs either to the organization, and can then decide any risk, or to one
entity, and can then decide only that entity's risks.

Submitting an acceptance or a renewal to a committee is `POST /RiskCommittees/{id}/Decisions`
(`RequireMgmtReviewAccess`). Members vote with `POST /RiskCommittees/Decisions/{id}/Votes`. Any signed-in user can call
it, and the service accepts the vote only from a member. Votes are approve, reject or abstain, and a vote is final.

The vote that reaches *k* approvals creates the acceptance in the same write, through the same gates as an individual
acceptance: Gate A, one live acceptance, the appetite ceiling, the tail and the indicators. All of these are checked
again at that moment. It does not check the individual severity band: the *k* distinct approvals are the authority.
Above the dual-approval threshold, a second approving member counter-signs. Every vote changes the decision's
concurrency token, so when two members cast the deciding vote at once, the second write is refused with 409.

The following members are recused from the vote: the risk's submitter, its owner and its manager. This applies
regardless of the installation's segregation switch. The third line never sits on a committee.

A decision is rejected as soon as the members who have not voted could no longer bring it to *k*. The individual path,
`POST /Risks/{id}/Acceptances`, is unchanged.

## 4. The third line

`Data/98.sql` seeds the permission `third_line_assurance` and the role `ThirdLineAuditor`. The role carries the read
permissions: register, governance, compliance, assessments, reports, vulnerabilities, hosts and incidents.

Every policy of the API refuses a caller holding the permission on any write. That covers every method other than
GET, HEAD, OPTIONS and TRACE, and the seven GETs that write. It applies even to a caller with the `Admin` role.

Bulk grants never include the permission: the administrator created by `netrisk-console user create` and "select all"
in the user editor get every permission except this one. It restricts rather than grants, so it is given deliberately,
through the role.

A short reviewed list of self-service writes stays open:

- signing in and out;
- changing one's own password;
- enrolling one's own hardware factor;
- completing one's own FaceID ceremony, which reading incidents needs when the FaceID plugin is on.

Nobody can name the third line as an authorizing manager, reviewer, business reviewer or committee member. The third
line reads the governance evidence pack (`GET /AuditTrail/Evidence`, and the CSV export). It is refused the PDF,
because generating the PDF stores a report.

`ThirdLineReadOnlyInventoryTest` proves the rule over every action of the API. It checks each action's real policy,
then sends one request per write route through the real routing and authorization pipeline.

## 5. Limits

- The desktop surface is T307. Until then, these features are available through the API and the REST client
  (`IDecisionCycleService`).
- Backtesting compares registration dates. A risk's submission date and an incident's creation date can no longer be
  changed by an update, but a risk whose scenario was rewritten after the incident still counts from the date it was
  registered. The rewrite is visible in the risk's audit trail.
- Changing the permissions of the `ThirdLineAuditor` role is an administrator's act, and the governance trail does not
  record it (S50 R6).
