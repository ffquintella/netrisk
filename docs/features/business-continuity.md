# Business Impact Analysis and Continuity

Track 9, Stage 9.3 (M41). The continuity data the MIGR-TI/IA methodology's Phase 3 BIA, flag 4 and the
Phase 7 metric "restoration tested vs declared RTO/RPO" depend on: MTPD/MAO, RTO and RPO on business
processes and IT services, the dependencies between them with their cascade, and restoration-test
records comparable against the declared objectives.

Full specification, with every decision and its rejected alternative:
[S43 — docs/roadmap/track9/9.3-bia-continuity.md](../roadmap/track9/9.3-bia-continuity.md).

Related: [Entity Management](entity-management.md) · [Risk governance](risk-governance.md) ·
[Reports](reports.md)

---

## 1. Concepts

| Concept | Where | Notes |
|---|---|---|
| Business impact analysis | [`BusinessImpactAnalysis`](../../src/DAL/Entities/BusinessImpactAnalysis.cs), `business_impact_analyses` | One per `businessProcess` or `itService`. Whole minutes; `NULL` is "not declared", `0` is a value. At least one objective; RTO ≤ MTPD/MAO on the same BIA |
| Dependency | [`BiaDependency`](../../src/DAL/Entities/BiaDependency.cs), `bia_dependencies` | *A* depends on *B*. Cycles are allowed and reported; a self-dependency is refused |
| Restoration test | [`RestorationTest`](../../src/DAL/Entities/RestorationTest.cs), `restoration_tests` | Insert-only evidence; a mistaken record is voided with a reason, never edited or deleted |
| Effective criticality | [`ProcessCriticality`](../../src/Tools/Continuity/ProcessCriticality.cs) | From the MTPD (≤ 4 h → 5, ≤ 24 h → 4, ≤ 72 h → 3, ≤ 7 d → 2, else 1), which wins over the declared `criticality` property; the critical-process coverage report uses it |
| Verification | [`RestorationVerification`](../../src/Tools/Continuity/RestorationVerification.cs) | Absent · Unverified · Met · NotMet. A declared RTO with no valid test is **unverified, never met**. The latest non-voided test that measured the objective (or failed) decides, valid for the configured number of days |
| Cascade | [`ContinuityGraph`](../../src/Tools/Continuity/ContinuityGraph.cs) | Iterative walks with a visited set: dependents, providers, cycle members, the requirement the dependents place on a node, conflicts and requirements without an objective |
| Weighted threat | [`ContinuityThreatAssessor`](../../src/Tools/Continuity/ContinuityThreatAssessor.cs) | The flag 4 basis: confirmed items weigh 1.0, unverified ones the configured weight (default 0.5); the node's weight is the highest item weight |

A process without a BIA is **absent** — never RTO 0 and never infinite: it imposes no requirement on
what it depends on, inherits no threat, and counts outside every denominator.

## 2. API

`ContinuityController` (`/Continuity`). Writes need global scope, checked before any lookup.

| Operation | Route | Authorization |
|---|---|---|
| List subjects, profile, tests, metric, parameters | `GET /Continuity/Subjects`, `…/Subjects/{id}`, `…/Subjects/{id}/RestorationTests`, `…/Metrics/RestorationVerification`, `…/Settings` | `RequireContinuityRead` (risk register audience or either writer permission) |
| Declare / delete a BIA; add / remove a dependency | `PUT`/`DELETE …/Subjects/{id}/Bia`; `POST …/Subjects/{id}/Dependencies`, `DELETE …/Dependencies/{depId}` | `bia_manage` |
| Record / void a restoration test | `POST …/Subjects/{id}/RestorationTests`, `POST …/RestorationTests/{testId}/Void` | `restoration_test_record` |
| Change the parameters | `PUT /Continuity/Settings` | `RequireAdminOnly` |

## 3. Parameters

Two rows of the `settings` table, seeded by `Data/90.sql` and audited by key:
`continuity_restoration_test_validity_days` (default 365, 1–1 095) and
`continuity_unverified_threat_weight` (default 0.5, 0.01–1.00). A missing or invalid row reads as the
default and is reported in `FallbackApplied`.

## 4. Desktop

- **Entities** → a business process or IT service shows a *Continuity (BIA)* block under its form, with
  the BIA editor, the dependency picker and the restoration-test list.
- **Reports** → report 8, *Restoration tested vs declared RTO/RPO*; report 7 shows where each critical
  process's criticality comes from.
- **Administration → Governance** → *Continuity parameters* tab (administrators).

## 5. Known limitations

- The flag 4, Gate A and "act now" are Stage 9.5; this stage exposes the weighted threat they consume.
- No BIA on activities, applications, data or hosts. Third parties are not BIA subjects either: Stage 9.10 links a
  supplier to the services and processes it supplies and reads this cascade from there, comparing its contracted RTO/RPO
  with what the cascade requires ([third-party register](third-party-register.md)).
- Recovery times are not added along the chain (a DRP concern), and the immutability of backups is not
  modelled.
- Writes need global scope; delegating them to unit managers is a later stage.
