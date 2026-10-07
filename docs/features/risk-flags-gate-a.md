# Mandatory Flags, Gate A and Top Risks

Track 9, Stage 9.5 (M43). The eleven mandatory flags of the MIGR-TI/IA methodology's Phase 2 as queryable
data on every risk, Gate A — the non-discretionary gate of Phase 4 — enforced ahead of the risk appetite on
every way a risk can be discarded, an "act immediately" decision that is recorded and notified rather than
inferred from a severity band, and the executive Top Risks list with trend, confidence and next decision.

Full specification, with every decision and its rejected alternative:
[S46 — docs/roadmap/track9/9.5-flags-gate-a.md](../roadmap/track9/9.5-flags-gate-a.md).

Related: [Risk governance](risk-governance.md) · [Business continuity](business-continuity.md) ·
[Exploitation signals](exploitation-signals.md) · [Notification channels](notification-channels.md)

---

## 1. The eleven flags and where each comes from

| # | Flag | Origin | Gate A |
|---|---|---|---|
| 1 | Life, health or human safety | declared | **yes** |
| 2 | Legal/regulatory obligation or LGPD | declared (derivable from the LGPD catalogue in Stage 9.11) | **yes** |
| 3 | Known exploitation (CISA KEV) or active attack | **derived** from KEV — an open finding linked to the risk has a CVE listed in [`kev_entries`](exploitation-signals.md) — and declarable for an active attack | **yes** |
| 4 | Critical process with RTO/RPO threatened | **derived** from the [BIA](business-continuity.md) — the risk is chain-linked to an active critical process, or to something it depends on, whose continuity-threat weight is above 0 — and declarable. The weight (1.0 confirmed, the configured weight when only unverified) is shown beside it | no, at any weight |
| 5 | Sensitive personal data, large volume or strategic research | **derived** from data classification — the risk is chain-linked to `organizationData` classified at a `securityClassificationLevel` marked **sensitive** — and declarable | no |
| 6 | Systemic risk or single point of failure | declared | no |
| 7 | Concentration in a third party, cloud or identity | declared (third-party register, Stage 9.10) | no |
| 8 | Low probability, catastrophic impact | declared (tail statistics, Stage 9.7) | no |
| 9 | Emerging risk or rapid growth | declared (KRIs, Stage 9.8) | no |
| 10 | High uncertainty or weak evidence | declared; evidence confidence sits beside it | no |
| 11 | AI risk | declarable only, until Stage 9.12 | no |
| — | Risk without legitimate acceptance | declared — a Phase 4 Gate A condition, not one of the eleven | **yes** |

A flag is **set** when it is declared **or** derived. A declaration never suppresses a derivation, so a KEV
finding keeps flag 3 — and Gate A — whatever is declared.

**Marking a classification level sensitive.** The entity form of a security classification level has a
**sensitive (flag 5)** checkbox (entity schema 2.6). Every risk linked to data classified at that level
carries flag 5 from the next reconciliation.

## 2. Derivation and the audit trail

The derived half is persisted in `risk_flags` and reconciled:

- nightly by the `RiskFlagsDerivation` job at **05:40** (after the KEV and EPSS syncs, before the expiry
  passes);
- on `POST /RiskFlags/Risks/{id}/Refresh`;
- **before every Gate A evaluation**, so a decision is never taken on stale flags;
- after every declaration or withdrawal.

It always reads the whole organisation and writes as the **system**: what a risk's flags are cannot depend
on who asked. A derived flag whose basis disappears — the CVE leaves KEV, the finding is closed, the BIA is
deleted, the link is removed — **reverts to false with a trail**: `derived_note` says what was lost, and
`audit_logs` gets the field-level `Update` rows (actor `system`). The row is never deleted.

Declaring or withdrawing needs a written reason. Withdrawing a Gate A condition (1, 2, 3 or "no legitimate
acceptance") is refused to the risk's submitter, owner and manager, with no break-glass. Flag rows and
decisions are part of the risk's audit trail and of the governance evidence pack, so the reason a Gate A
declaration was withdrawn is exported with it.

## 3. Gate A

Gate A holds when flag 1, 2 or 3, or "no legitimate acceptance", is set. While it holds, NetRisk answers
**`422 gate_a_non_discretionary`** to:

| Path | Where |
|---|---|
| Accepting the risk | `POST /Risks/{id}/Acceptances`, and through it the review campaign / portal "Accepted" decision |
| Renewing an acceptance | `POST /Risks/{id}/Acceptances/{acceptanceId}/Renew` |
| Closing | `POST /Risks/{id}/Closure` and `PUT /Risks/{id}` with status `Closed` — even with the workflow state machine switched off |
| Deleting | `DELETE /Risks/{id}` |
| Recording any decision but "act immediately" | `POST /RiskFlags/Risks/{id}/Decisions` |

Gate A is checked **first** — before segregation of duties, band authority and the risk appetite (Gate B) —
so when both gates would refuse, the refusal says Gate A. There is no override: the way out is to correct
the fact behind the condition. Live acceptances are not revoked, but cannot be renewed.

**Escalation.** When Gate A starts holding on a risk, NetRisk records an automatic "act immediately"
decision, marks the risk for review and raises the **`risk.gate_a`** notification event — once per onset.
Subscribe a channel to it under Administration → Notifications.

## 4. Decisions

`POST /RiskFlags/Risks/{id}/Decisions` records one of the four Phase 4 decisions — act immediately, treat
in cycle, monitor/accept, archive — with a reason. The log is insert-only; the latest is the one in force.
"Act immediately" is escalated and notified whatever the score; nothing sets it from a severity band. The
decision is a classification: "monitor/accept" creates no acceptance and "archive" closes nothing.

## 5. Top Risks

`GET /RiskFlags/TopRisks?limit=10` (1–50) lists open risks in the caller's scope, ordered by Gate A, then
"act immediately", then the business owner's rank, then expected annual loss; the ordinal score only breaks
ties. Each row carries flags, Gate A conditions, the decision in force, inherent/residual score and E[L],
owner, evidence confidence, the **trend** (90-day change of the score history, ±0.5) and the **next
decision** (Gate A escalation, acceptance expiry, next management review or the earliest open task).

## 6. API and permissions

| Route | Policy |
|---|---|
| `GET /RiskFlags/Catalogue`, `GET /RiskFlags/Risks/{id}`, `POST …/Refresh`, `PUT …/Flags/{code}`, `GET …/Decisions`, `GET /RiskFlags/Flagged`, `GET /RiskFlags/TopRisks` | `RequireRiskmanagement` |
| `POST …/Flags/{code}/Withdraw`, `POST …/Decisions` | `RequireMgmtReviewAccess` |

No new permission. Desktop screens arrive with T303; the REST client is `ClientServices.IRiskFlagsService`.

## 7. What establishes each claim

| Claim | Test |
|---|---|
| Gate A refuses even within appetite, and its refusal wins over the appetite's | `GateAInMemoryTest` G1–G3 |
| Gate A comes before segregation of duties; the segregation break-glass does not open it | G4, G9 |
| Renewal, campaign acceptance, closure (state machine off) and deletion are refused | G5–G8; `RisksControllerExtendedTest` (422 and closure removed) |
| A derived flag that loses its basis reverts with an audit entry, as the system | `RiskFlagsServiceInMemoryTest` R1, R2 |
| A scoped refresh cannot revert a basis outside the caller's scope | R3 |
| A Gate A onset escalates once | E1–E3 |
| Withdrawing a Gate A condition is refused to the risk's own people; the reason is exported | F4, EV1 |
| Every action carries exactly the policy above | `RiskFlagsAuthorizationTest`, `ControllerAuthorizationInventoryTest` |
