# KRIs, Mandatory Reassessment Triggers and the Methodology's Metrics

Track 9, Stage 9.8 (M46). MIGR-TI/IA Phase 7 — monitoring, metrics and learning. **Key risk indicators** become a
first-class record with a source, a Phase 0 tolerance and a history; a KRI with no recent reading reads **stale**, never
"within"; the **six mandatory reassessment triggers** of Phase 7 put a risk back in front of its owner — once per cause;
**Gate B** compares the indicators too, not only the score and the tail; and a **metrics panel** gathers the
methodology's ten measures, saying which ones can be computed today and which stage delivers the rest.

Full specification, with every decision and its rejected alternative:
[S49 — docs/roadmap/track9/9.8-kri-reassessment-metrics.md](../roadmap/track9/9.8-kri-reassessment-metrics.md).

Related: [Risk governance (review cadence, appetite)](risk-governance.md) · [Mandatory flags and Gate A](risk-flags-gate-a.md) ·
[Tail statistics and Gate B on the tail](tail-statistics-portfolio.md)

---

## 1. Key risk indicators

`/Monitoring/Kris` holds the register. A KRI has:

| Field | Meaning |
|---|---|
| Name, description | what it measures and how |
| Category | unavailability, data loss, number of data subjects, or other — the indicators Phase 4's Gate B names |
| **Source** | where its readings come from (a system, a report, a person) |
| Unit | `%`, `hours`, `records`… |
| Direction | worse when higher, or worse when lower |
| **Tolerance** | the Phase 0 limit, with the **rationale** that set it (minutes, approval reference) |
| Warning | optional, on the good side of the tolerance — informative only |
| **Maximum reading age** | how many days a reading stays current (1–366, 31 by default — the monthly cadence of Phase 7) |
| Owner, entity | who answers for it; its business entity, or none for an organization-wide indicator |

Readings (`POST /Monitoring/Kris/{id}/Readings`) carry the value, **when it was observed** (never in the future; a few
minutes of client clock skew is recorded as the server's now) and a source note. They are insert-only: a wrong reading
is **voided with a reason** and stays in the history, ignored. A KRI is **retired**, never deleted — its history is the
evidence a gate decided on.

### State

| State | When |
|---|---|
| `NoReading` | no valid reading |
| **`Stale`** | the latest reading is older than the maximum age — **whatever its value** |
| `Breached` | the latest reading is strictly beyond the tolerance |
| `Warning` | past the warning, within the tolerance |
| `WithinTolerance` | otherwise |
| `Retired` | retired: not evaluated, gates nothing |

Staleness is checked before the value. A KRI whose only reading, three months old, was within tolerance is `Stale`:
reading it as "within" is the false comfort an indicator panel must not give.

## 2. The six mandatory reassessment triggers

| # | Phase 7 trigger | How it reaches NetRisk |
|---|---|---|
| 1 | Architecture or technology change | declared |
| 2 | New supplier, acquisition or migration | declared — Stage 9.10 delivers the [third-party register](third-party-register.md) and keeps the trigger declared (S51 D17) |
| 3 | Significant incident or near miss | declared, **referencing the incident** — one event per incident |
| 4 | New regulation | declared |
| 5 | New AI model deployed | declared (the model inventory is Stage 9.12) |
| 6 | New data, or a KRI beyond its tolerance | **detected** for a KRI; declared for new data |

A declared event (`POST /Monitoring/Reassessment/Events`) names the risks it obliges reassessing; more can be added
later (`POST …/Events/{id}/Risks`). Each open risk gets a **trigger**, is flagged for review (keeping the first reason
if it already was) and the 07:30 review-cadence message tells its owner and manager. Closed risks are skipped and
listed. A trigger is **answered** by the next management review of the risk; until then it is pending
(`GET /Monitoring/Reassessment/Triggers?pendingOnly=true`).

### KRI breaches, once per episode

A KRI beyond its tolerance opens a **breach episode** — one reassessment event — and triggers every open risk the KRI
governs (`PUT /Monitoring/Kris/{id}/Risks/{riskId}`). The episode ends when a reading shows the KRI back within
tolerance; staleness does not end it. While it is open, nothing is raised again: **a KRI breached for thirty days opens
one reassessment per risk, not thirty**. The database enforces it too — one event per opening reading, one trigger per
event and risk. A risk linked during an episode gets its trigger at once.

The breach is evaluated when a reading is recorded or voided, when a risk is linked, and by the daily
**`KriEvaluation`** job at **06:45** — after the flags reconciliation (05:40) and the expiry passes (06:00, 06:15),
before the review cadence (07:30), so an overnight breach is in that morning's message. The job is what notices a KRI
going stale.

Notifications: `kri.breached` once per episode; `risk.reassessment_triggered` once per trigger (digest recommended).

## 3. Gate B by indicator

`GET /Risks/{id}/Appetite` gains an **`Indicators`** block: the KRIs linked to the risk against their tolerances.

| State | Meaning |
|---|---|
| `NotConfigured` | no active KRI is linked |
| `NotAssessable` | a linked KRI has no reading or is stale, and none exceeds — **never read as within** |
| `WithinTolerance` | every linked KRI is current and within tolerance |
| `ExceedsTolerance` | a linked KRI is beyond its tolerance — or stale with its last reading beyond it |

An acceptance or renewal whose indicators exceed is refused with **`422 risk_appetite_indicator_tolerance`**, checked
last: Gate A → segregation of duties → authority → ordinal ceiling → tail → indicators. Not assessable does not refuse.
The tolerance is the KRI's own, so a risk with no appetite row is still gated by its indicators. The gate reads a risk's
KRIs whatever the caller's entity scope — it never depends on who asks. A KRI must be organization-wide or of the risk's
own entity to be linked.

## 4. The metrics panel

`GET /Monitoring/Metrics` — computed on request, never stored, in the caller's scope:

| # | Metric | Today |
|---|---|---|
| M1 | Coverage of critical processes and discovered assets | **partial** — critical-process coverage (Stage 9.1); discovered assets are not measured |
| M2 | Risks with an owner and evidence | **available** — evidence = confidence declared confirmed or indicative |
| M3 | Mean time from discovery to decision | **available** — submission to the first Phase 4 decision |
| M4 | Aggregate exposure above appetite (E[L] and P95) | **available** — the residual portfolio of Stage 9.7 with its Gate B |
| M5 | Control effectiveness and residual by domain | **partial** — inherent → residual reduction by entity; control domain is not a dimension |
| M6 | Time to remediate KEV items | **available** (Stage 9.4) |
| M7 | Restoration tested against the declared RTO/RPO | **available** (Stage 9.3) |
| M8 | Concentration in third parties | available — the largest share of the critical processes depending on one supplier, from the [third-party register](third-party-register.md) (Stage 9.10, S51) |
| M9 | Reopened risks, unforeseen incidents, false negatives | available since Stage 9.9 — [archive and backtesting](archive-backtesting-committee.md) |
| M10 | AI: precision, recall, calibration, drift, override | available since Stage 9.12 — the share of the AI models in use evaluated on every metric their tier requires; a model with no evaluation counts as not evaluated ([AI governance](ai-governance.md), S53) |

Plus the mechanism itself: KRIs by state, and triggers pending, answered, and the mean days to the answer. A source
that fails is logged and its metric reads "not available"; the panel never fails for one number.

## 5. Permissions

| Action | Policy |
|---|---|
| Read KRIs, events, triggers, the metrics panel | `RequireRiskmanagement` |
| Define, change or retire a KRI (its tolerance is a Phase 0 limit) | `RequireAdminOnly` |
| Record or void a reading, link or unlink a risk, declare an event | `RequireSubmitRisk` |

No new permission or policy. A KRI outside the caller's entity scope is 404; writing one there is 403. An
organization-wide KRI is readable by every user of the register, and a scoped user may link their own risks to it, but
only an unrestricted user writes its definition or its readings. Every change is in
the governance audit trail with its author: the definition and tolerance, each reading and voiding, each link and
unlink, each event and trigger.

## 6. Known limitations

- The desktop screens — the KRI register, readings, the link editor, Gate B by indicator on the risk, event declaration,
  the reassessment queue and the metrics panel — are **T306**; this release ships the API and the REST client.
- Triggers 1–5 are declared, not detected: nothing in NetRisk changes by itself when the architecture does.
- Creating an incident does not trigger a reassessment; declare it, referencing the incident (backtesting is Stage 9.9).
- KRI readings arrive through the API (a person or an integration with a token); reading a KRI automatically from a
  metric or an external monitor is not built.
- Flag 9 ("emerging risk or rapid growth") stays declared; a linked KRI's series is evidence for it, not a derivation.
- A removed link stays in the audit table but not in the risk's own trail, like a removed correlation.
