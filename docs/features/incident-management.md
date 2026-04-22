# Incident Management

Records and tracks security incidents from detection to resolution, with auto-numbered sequences, file attachments, and linkage to incident response plans.

## Key Model Classes

- [Incident.cs](../../src/DAL/Entities/Incident.cs)
- [IncidentToIncidentResponsePlan.cs](../../src/DAL/Entities/IncidentToIncidentResponsePlan.cs) — join to IRPs
- [IncidentCategory.cs](../../src/Model/Incidents/IncidentCategory.cs)
- [IncidentStatus.cs](../../src/Model/Status/IncidentStatus.cs)
- [File.cs](../../src/DAL/Entities/File.cs) — attachment storage

## Server Service

[`IIncidentsService`](../../src/ServerServices/Interfaces/IIncidentsService.cs):

- `GetAllAsync`, `GetByIdAsync`
- `GetNextSequenceAsync` — yearly auto-numbering
- `GetAttachmentsByIdAsync`
- `GetIncidentResponsPlanIdsByIdAsync`, `AssociateIncidentResponsPlanIdsByIdAsync`
- `CreateAsync`, `UpdateAsync`, `DeleteByIdAsync`

## API

[`IncidentsController`](../../src/API/Controllers/IncidentsController.cs):

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/incidents` | List all |
| GET | `/incidents/{id}` | Get one |
| GET | `/incidents/{id}/Attachments` | Attachments |
| GET | `/incidents/{id}/ResponsePlans` | Linked IRPs |
| POST | `/incidents` | Create |
| PUT | `/incidents/{id}` | Update |
| DELETE | `/incidents/{id}` | Delete |
| POST/DELETE | `/incidents/{id}/ResponsePlans/{planId}` | Associate / dissociate plan |

## Client

[`IncidentsRestService`](../../src/ClientServices/Services/IncidentsRestService.cs).

## Capabilities

- Yearly sequence numbering generated server-side
- Multi-category classification
- File attachments per incident
- Many-to-many link to incident response plans (plans can be activated from an incident)
- Creation / audit metadata

## Tests

- `IncidentsServiceTest` in `ServerServices.Tests`

## Common Exceptions

`DataNotFoundException`, `PermissionInvalidException`
