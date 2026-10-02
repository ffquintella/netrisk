# Computer / Host Management

Tracks hosts (computers, servers, network devices) as inventory assets. Hosts own a list of network services (port/protocol) and are the join point between vulnerability scans and risks.

## Key Model Classes

- [Host.cs](../../src/DAL/Entities/Host.cs) — host/computer entity
- [HostsService.cs](../../src/DAL/Entities/HostsService.cs) — service running on a host (port, protocol, name)
- [HostsServiceDto.cs](../../src/Model/DTO/HostsServiceDto.cs)
- [HostVulnerabilitySummaryDto.cs](../../src/Model/DTO/HostVulnerabilitySummaryDto.cs) — open-finding counts by severity (S38)
- [Technology.cs](../../src/DAL/Entities/Technology.cs) — software/tech stack

## Server Service

[`IHostsService`](../../src/ServerServices/Interfaces/IHostsService.cs):

- Query: `GetAll`, `GetById`, `GetByIp`/`GetByIpAsync`, `GetFiltredAsync` (Sieve), `HostExistsAsync`
- Lifecycle: `Create`/`CreateAsync`, `Update`/`UpdateAsync`, `Delete`
- Services: `GetHostServices`, `GetHostService`, `HostHasService`/`Async`, `CreateAndAddService`/`Async`, `DeleteService`, `UpdateService`, `FindService`/`Async` (LINQ expression)
- `GetVulnerabilities` — vulnerabilities affecting this host
- `GetEnvironmentsAsync` — distinct non-blank environments, for the Hosts view facet (S38)
- `GetVulnerabilitySummaryAsync` / `GetVulnerabilitySummariesAsync` — open findings by severity for one
  or up to 500 hosts in one grouped query; "open" is `Model.Status.ClosedStatuses`, the same set the
  Master Dashboard counts by (S38)

## API

[`HostsController`](../../src/API/Controllers/HostsController.cs):

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/hosts` | List all |
| GET | `/hosts/Filtered` | Filtered, sorted, paged list; total in `X-Total-Count`. Filterable: `hostname`, `id`, `status`, `fqdn`, `ip`, `os`, `teamId`, `RegistrationDate`, `criticality`, `environment`, `owner`, `source`, `riskScore`, `lastVerificationDate` |
| GET | `/hosts/Environments` | Distinct non-blank environments of the hosts the caller can see (S38) |
| GET | `/hosts/VulnerabilitySummary?ids=1\|2\|3` | Open-finding counts by severity for up to 500 hosts; unknown or out-of-scope hosts are omitted (S38) |
| GET | `/hosts/ByIp/{ip}` | Lookup by IP |
| GET | `/hosts/{id}` | Get one |
| GET | `/hosts/{id}/Services` | Services on host |
| GET | `/hosts/{id}/Vulnerabilities` | Vulnerabilities on host |
| GET | `/hosts/{id}/VulnerabilitySummary` | Open-finding counts by severity for one host; 404 when missing or out of scope (S38) |
| GET | `/hosts/{id}/History?limit=500` | Field-level change history from `audit_logs`, newest first, `limit` 1–5000; 404 when missing or out of scope (S38) |
| POST/PUT/DELETE | `/hosts[/{id}]` | CRUD |
| POST/PUT/DELETE | `/hosts/{id}/Services/{serviceId}` | Manage services |

Every route is under the controller's `[PermissionAuthorize("hosts")]` (`hosts_create` / `hosts_delete`
for writes). `/hosts/{id}/History` looks the host up through the entity-scoped hosts set before it
reads the trail, and returns rows without the `User` navigation; the generic
`/AuditTrail/Host/{id}` reader refuses host types for that reason (`use_host_history`).

## Change history

`GovernanceAuditInterceptor` records `Host` and `HostsService` saves as one `audit_logs` row per changed
field (a summary row for a create or delete), grouped per save by `CorrelationId`. `LastVerificationDate`
and `RiskScoreUpdatedAt` are not recorded: every import pass stamps them. The actor is the caller's login
for an API edit, and `Nessus import` (any finding importer, by name), `Jira Assets import` or
`Trend Micro import` for those imports.

## Client

[`HostsRestService`](../../src/ClientServices/Services/HostsRestService.cs): `GetFilteredAsync(pageSize, page,
filter, sorts)` returns `(Items, Total)`; the three-argument overload returns the list sorted by
`hostName` on the server. `GetEnvironmentsAsync`, `GetVulnerabilitySummaryAsync`, `GetHistoryAsync`.

## Capabilities

- IP-based primary lookup (plus dedup via `HostExistsAsync`)
- Port/service enumeration per host
- Vulnerability rollup per host
- Sieve filtering/pagination
- Expression-based service search (`FindService` takes a LINQ predicate)
- Technology / software inventory

## Tests

- `HostsServiceTest`, `HostsServiceInMemoryTest`, `Filtering/HostFilteringEndToEndTest`,
  `Track8/HostAuditTrailInMemoryTest` (ServerServices.Tests)
- `HostsControllerTest`, `Track8ControllersTest` (API.Tests)
- `HostsRestServiceTest`, `HostsRestServiceStubTest` (ClientServices.Tests)

## Common Exceptions

`DataNotFoundException`, `DataAlreadyExistsException`, `InvalidParameterException`
