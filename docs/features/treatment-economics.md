# Treatment Economics: Monetary Cost, Gates C and D, Target Level

Track 9, Stage 9.6 (M44). The economics of MIGR-TI/IA Phases 4 and 5 as data and as calculation: the four treatment
options, a monetary cost beside the existing ordinal cost scale, **Gate C** (marginal economics) on every mitigation,
**Gate D** (portfolio selection under budget, people, dependencies and deadline), a **target** risk level on the
register, and completion evidence with an acceptance criterion on every treatment task.

Full specification, with every decision and its rejected alternative:
[S47 — docs/roadmap/track9/9.6-treatment-economics.md](../roadmap/track9/9.6-treatment-economics.md).

Related: [Mandatory flags, Gate A and Top Risks](risk-flags-gate-a.md) · [Risk governance](risk-governance.md) ·
[Risk management](risk-management.md)

---

## 1. The four treatment options and the monetary cost

Each mitigation can carry a **treatment economics** record (`mitigation_economics`), declared through
`PUT /TreatmentEconomics/Mitigations/{id}`:

| Option | Meaning | How it enters E[L] after |
|---|---|---|
| Avoid | Stop the activity that produces the risk | effectiveness 100 % |
| Reduce | Controls that lower frequency or magnitude | the mitigation's declared effectiveness |
| Transfer/share | Insurance, contract, SLA — **counterparty required** | the effectiveness declares the share transferred |
| Accept | Retain the risk | Gate C does not apply; it creates no formal acceptance |

The editable `planning_strategy` labels stay as they are and are not mapped onto the typed option — an administrator
can rename them, so no code can rely on what a value means.

The **monetary cost** is declared as a block, beside the ordinal `mitigation_cost` scale (which is unchanged):

| Field | |
|---|---|
| One-time | implementation |
| Annual | licences, operation, insurance premium |
| Annual side effects | productivity, friction, revenue forgone when avoiding |
| Horizon (years, 1–30) | amortization of the one-time cost; required when it is above zero |

From it: **annualized total** = annual + side effects + one-time ÷ horizon (what Gate C compares), and **first year** =
one-time + annual (what Gate D draws from the budget — side effects are a cost, not an outlay). A cost that is not
declared is **not** a zero cost. Effort (person-days), duration (days) and prerequisites (other mitigations, across
risks) feed Gate D; a prerequisite that closes a cycle is refused with `422 dependency_cycle`.

The record is a table of its own, not columns on `mitigations`, because `PUT /Mitigations/{id}` copies the whole
payload and would erase fields a client does not know.

## 2. Gate C — marginal economics

On every mitigation (`GET /TreatmentEconomics/Mitigations/{id}` and the risk view):

```
benefit = E[L before] − E[L after]        (both annual MEANS from the FAIR-lite Monte Carlo)
passes  ⇔ benefit > annualized total cost
```

- **Not assessable** — never a pass or a fail — when there is no monetary cost, no quantitative analysis, no residual
  run (effectiveness 0 or undeclared), no recorded residual **mean** (an analysis from before schema 95: recompute it),
  or when the residual run belongs to a more recent mitigation of the same risk. Every reason is listed.
- **The mean, never the median.** The residual mean (`risk_scoring.quant_residual_ale_mean`) is recorded by every
  quantitative computation from schema 95 on; the residual median is never used in its place.
- **Gordon–Loeb** (1/e ≈ 36.8 % of E[L] before) is shown as a reference, as the methodology says, and never decides:
  a cost above it that is still below the benefit passes.
- **Gate A comes first.** On a Gate A risk the result is informational: the treatment is mandatory whatever Gate C
  says, and "accept" cannot be declared as its option (`422 gate_a_non_discretionary`, the same refusal as accepting).
  Gate C never relaxes Gate B: a failing control does not make a risk acceptable.

## 3. Gate D — portfolio selection

`POST /TreatmentEconomics/Portfolio` with a budget (required), and optionally a people capacity, a deadline, a start
date and a list of mitigations (by default every mitigation of an open risk in your scope that is not completed). It is
a computation — nothing is stored.

Selection is in three tiers, each filled before the next:

1. **Mandatory** — Gate A risks, whatever Gate C says.
2. **Protected** — tail (flag 8) and systemic (flag 6) risks, whatever their expected loss and even when Gate C fails:
   the expected loss understates exactly these risks, which is why the methodology says to preserve them.
3. **Economic** — treatments that pass Gate C, by benefit/cost ratio.

A treatment takes its prerequisites with it (they inherit its tier); the critical path of durations is checked against
the deadline; effort is checked against the people capacity. Every item says why it was or was not selected. The result
flags **GateAShortfall** (a Gate A treatment did not fit — never silent), **ProtectedShortfall**, and
**RequiresEscalation** on each unfunded treatment of a risk above the appetite (Gate B: treat or escalate).

## 4. Target risk level

`PUT /TreatmentEconomics/Risks/{id}/Target` sets a target score (0–10, the residual's scale) and/or a target annual
expected loss, an optional date and a written rationale; `DELETE` removes it. The risk view
(`GET /TreatmentEconomics/Risks/{id}`) compares it with the current residual (or inherent) and the appetite: met or
not, the gap, **within appetite** (a target above the appetite ceiling is recorded and flagged, not refused) and overdue.

## 5. Completion evidence and acceptance criterion

`POST`/`PUT /MitigationTasks` carry an **acceptance criterion** and **completion evidence**:

- moving a task to **Completed** requires evidence (`400` naming `CompletionEvidence` otherwise); who recorded it and
  when are stamped by the server;
- on update, an omitted (null) field is left as it is and a blank one is cleared — a client that does not know the
  fields cannot erase them; the evidence of a completed task cannot be cleared;
- the risk view lists, per task, what Phase 5 still needs: owner, due date, acceptance criterion and — when completed —
  evidence. Tasks completed before schema 95 read as "completed without evidence".

## 6. Permissions

| Action | Policy |
|---|---|
| Read a mitigation's economics | `RequireMitigation` |
| Declare option/cost/prerequisites, set or remove the target | `RequirePlanMitigations` |
| Risk view, portfolio selection | `RequireRiskmanagement` |

Everything is entity-scoped: another unit's mitigations and risks are not found.

## 7. Known limitations

- The desktop screens (cost and option editors, Gate C on the mitigation, the target on the risk, task evidence and the
  portfolio screen) are **T304**; this release ships the API and the REST client.
- Tail is the declared flag 8 until Stage 9.7 derives it from P95/CVaR; there is no monetary appetite until M45/M46.
- Evidence is text (a description, a reference, a link); attaching a file per task is not part of this stage.
