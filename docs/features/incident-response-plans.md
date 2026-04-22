# Incident Response Plans

Template-based response plans composed of tasks, with approval workflow and per-execution tracking. Plans can be activated from an incident and run independently with their own task status trail.

## Key Model Classes

- [IncidentResponsePlan.cs](../../src/DAL/Entities/IncidentResponsePlan.cs) — plan template
- [IncidentResponsePlanTask.cs](../../src/DAL/Entities/IncidentResponsePlanTask.cs) — task definition
- [IncidentResponsePlanExecution.cs](../../src/DAL/Entities/IncidentResponsePlanExecution.cs) — plan run
- [IncidentResponsePlanTaskExecution.cs](../../src/DAL/Entities/IncidentResponsePlanTaskExecution.cs) — task run
- [IncidentResponsePlanTaskToEntity.cs](../../src/DAL/Entities/IncidentResponsePlanTaskToEntity.cs) — task assignment
- [TaskType.cs](../../src/Model/IncidentResponsePlan/TaskType.cs)

## Server Service

[`IIncidentResponsePlansService`](../../src/ServerServices/Interfaces/IIncidentResponsePlansService.cs):

- Plans: `GetAllAsync`, `GetAllApprovedAsync`, `GetByIdAsync` (with `includeTasks`, `includeActivatedBy`), `CreateAsync`, `UpdateAsync`, `DeleteAsync`
- Tasks: `CreateTaskAsync`, `UpdateTaskAsync`, `GetTaskByIdAsync`, `DeleteTaskAsync`, `GetTasksByPlanIdAsync`
- Executions: `CreateExecutionAsync`, `UpdateExecutionAsync`, `DeleteExecutionAsync`, `GetExecutionsByPlanIdAsync`, `GetExecutionByIdAsync`
- Task executions: `CreateTaskExecutionAsync`, `UpdateTaskExecutionAsync`, `GetTaskExecutionsByIdAsync`, `DeleteTaskExecutionAsync`, `ChangeExecutionTaskStatusByIdAsync`
- `GetIncidentByTaskIdAsync` — reverse lookup to triggering incident

## API

[`IncidentResponsePlansController`](../../src/API/Controllers/IncidentResponsePlansController.cs) — requires `RequireValidUser`.

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/incidentresponseplans` | List all |
| GET | `/incidentresponseplans/Approved` | Approved plans only |
| GET | `/incidentresponseplans/{id}` | Get one |
| GET | `/incidentresponseplans/{id}/Attachments` | Attachments |
| GET/POST | `/incidentresponseplans/{id}/Tasks` | List / create tasks |
| GET/PUT/DELETE | `/incidentresponseplans/{id}/Tasks/{taskId}` | Manage task |
| GET/POST/PUT/DELETE | `/incidentresponseplans/{id}/Tasks/{taskId}/Executions[/{executionId}]` | Task executions |
| POST/PUT/DELETE | `/incidentresponseplans[/{id}]` | Plan CRUD |

## Client

[`IncidentResponsePlansRestService`](../../src/ClientServices/Services/IncidentResponsePlansRestService.cs).

## Capabilities

- Approval workflow gate (only approved plans can be used)
- Task assignment to entities (responders)
- Multi-level nesting: Plan → Tasks → Task Executions
- Attachments at plan and task-execution level
- Activation audit (`includeActivatedBy`)

## Tests

- `IncidentResponsePlansControllerTest` (API.Tests)
- `IncidentResponsePlansServiceTest` (ServerServices.Tests) — 40+ test methods
- `IncidentResponsePlansRestServiceTests` (ClientServices.Tests)

## Common Exceptions

`DataNotFoundException`, `PermissionInvalidException`, `ErrorSavingException`
