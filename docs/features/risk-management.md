# Risk Management

Central feature of NetRisk. Tracks organizational risks through their full lifecycle — identification, scoring, mitigation, management review, and closure — and links them to vulnerabilities, entities, incident response plans, and file attachments.

> The governance layer on top of this — formal expiring acceptance, inherent versus residual scoring,
> the enforced state machine and segregation of duties, risk appetite, the field-level audit trail,
> the auditor evidence pack and the business review portal — is documented separately in
> [Risk governance](risk-governance.md).

## Key Model Classes

- [Risk.cs](../../src/DAL/Entities/Risk.cs) — core risk entity
- [RiskScoring.cs](../../src/DAL/Entities/RiskScoring.cs) — impact/likelihood scoring
- [RiskCatalog.cs](../../src/DAL/Entities/RiskCatalog.cs) — predefined catalog entries
- [Closure.cs](../../src/DAL/Entities/Closure.cs) — closure records
- [Review.cs](../../src/DAL/Entities/Review.cs) — management review records
- [RiskStatus.cs](../../src/Model/Risks/RiskStatus.cs) — `New`, `MitigationPlanned`, `ManagementReview`, `Closed`

## Server Service

[`IRisksService`](../../src/ServerServices/Interfaces/IRisksService.cs) exposes:

- Query: `GetUserRisks`, `GetToReview`, `GetRisk`, `GetRisksNeedingReview`
- Scoring: `GetRiskScoring`, `GetRiskScore`, `GetRiskImpacts`, `GetRiskSources`
- Lifecycle: `CreateRisk`, `SaveRisk`, `DeleteRisk`
- Entity associations: `AssociateRiskWithEntity`, `CleanRiskEntityAssociations`, `DeleteEntityAssociation`
- Closure: `GetRiskCloseReasons`, `GetRiskClosureByRiskId`, `CreateRiskClosure`, `DeleteRiskClosure`
- Links: `GetVulnerabilitiesAsync`, `GetFilteredVulnerabilitiesAsync`, `GetIncidentResponsePlanAsync`, `AssociateRiskToIncidentResponsePlanAsync`

## API

[`RisksController`](../../src/API/Controllers/RisksController.cs) — requires `RequireValidUser` and `RequireRiskmanagement`.

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/risks` | List (filter by status, includeClosed) |
| GET | `/risks/ToReview` | Risks due for review |
| GET | `/risks/{id}` | Get one |
| GET | `/risks/{id}/Mitigation` | Mitigations |
| GET | `/risks/{id}/Scoring` | Scoring details |
| GET/PUT/DELETE | `/risks/{id}/Entity` | Entity association |
| GET | `/risks/{id}/Files` | Attachments |
| GET | `/risks/{id}/MgmtReviews` | Management reviews |
| GET | `/risks/{id}/Vulnerabilities` | Linked vulnerabilities |
| GET | `/risks/{id}/IncidentResponsePlan` | Linked IRP |
| POST/PUT/DELETE | `/risks/{riskId}/Closure` | Close / reopen |
| POST | `/risks` | Create |

## Client

[`RisksRestService`](../../src/ClientServices/Services/RisksRestService.cs) — consumed by `GUIClient` and `ConsoleClient`.

## Capabilities

- Status lifecycle with management-review approval step before closure
- Impact × likelihood scoring matrix, multiple risk sources
- File attachments
- Many-to-many links to vulnerabilities and incident response plans
- Entity (business unit) association
- "Needs review" tracking based on review dates, pushed as notifications by the Track 8 cadence job
- Inherent and residual scores with the delta, on the register list and the risk detail panel

## Tests

- [RisksControllerTest](../../src/API.Tests/APITests) (API)
- [RisksServiceTest](../../src/ServerServices.Tests/ServiceTests) (server)
- [RisksRestServiceTest](../../src/ClientServices.Tests/Services) (client)

## A note on the composite score

`RiskCalculationTool.CalculateTotalRiskScore` computes `(calculated + 2 × contributing) / 3` — a
weighted average of the ordinal matrix score and a CVSS-derived contribution from linked
vulnerabilities. **Treat it as a triage heuristic and not as a measurement.** The two inputs are
non-commensurate: the matrix score is a rank on a five-point scale and the contribution is a measured
quantity, so their average has no dimension. It is a reasonable way to sort a queue. It is not a
number to put in a threshold, report to a board, or difference between two periods — use the FAIR-lite
quantitative result for that, which is described in [Risk governance § 8](risk-governance.md#8-quantitative-scoring).

## Common Exceptions

`DataNotFoundException`, `PermissionInvalidException`, `UserNotAuthorizedException`
