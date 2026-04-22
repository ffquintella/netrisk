# Computer / Host Management

Tracks hosts (computers, servers, network devices) as inventory assets. Hosts own a list of network services (port/protocol) and are the join point between vulnerability scans and risks.

## Key Model Classes

- [Host.cs](../../src/DAL/Entities/Host.cs) — host/computer entity
- [HostsService.cs](../../src/DAL/Entities/HostsService.cs) — service running on a host (port, protocol, name)
- [HostsServiceDto.cs](../../src/Model/DTO/HostsServiceDto.cs)
- [Technology.cs](../../src/DAL/Entities/Technology.cs) — software/tech stack

## Server Service

[`IHostsService`](../../src/ServerServices/Interfaces/IHostsService.cs):

- Query: `GetAll`, `GetById`, `GetByIp`/`GetByIpAsync`, `GetFiltredAsync` (Sieve), `HostExistsAsync`
- Lifecycle: `Create`/`CreateAsync`, `Update`/`UpdateAsync`, `Delete`
- Services: `GetHostServices`, `GetHostService`, `HostHasService`/`Async`, `CreateAndAddService`/`Async`, `DeleteService`, `UpdateService`, `FindService`/`Async` (LINQ expression)
- `GetVulnerabilities` — vulnerabilities affecting this host

## API

[`HostsController`](../../src/API/Controllers/HostsController.cs):

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/hosts` | List all |
| GET | `/hosts/Filtered` | Sieve-filtered list |
| GET | `/hosts/ByIp/{ip}` | Lookup by IP |
| GET | `/hosts/{id}` | Get one |
| GET | `/hosts/{id}/Services` | Services on host |
| GET | `/hosts/{id}/Vulnerabilities` | Vulnerabilities on host |
| POST/PUT/DELETE | `/hosts[/{id}]` | CRUD |
| POST/PUT/DELETE | `/hosts/{id}/Services/{serviceId}` | Manage services |

## Client

[`HostsRestService`](../../src/ClientServices/Services/HostsRestService.cs).

## Capabilities

- IP-based primary lookup (plus dedup via `HostExistsAsync`)
- Port/service enumeration per host
- Vulnerability rollup per host
- Sieve filtering/pagination
- Expression-based service search (`FindService` takes a LINQ predicate)
- Technology / software inventory

## Tests

- `HostsServiceTest` (ServerServices.Tests)
- `HostsRestServiceTest` (ClientServices.Tests)

## Common Exceptions

`DataNotFoundException`, `DataAlreadyExistsException`, `InvalidParameterException`
