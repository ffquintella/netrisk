# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

NetRisk is a cross-platform risk/vulnerability/incident management application built on .NET 10 (see `src/global.json`). The codebase is split across a REST API, a desktop GUI (Avalonia), a website, background jobs, a console client, and a plugin system.

## Build & Packaging (Nuke)

Builds are driven by [Nuke](https://nuke.build/). The root bootstrappers (`build.sh` / `build.cmd` / `build.ps1`) will install the SDK if needed and forward arguments to the Nuke project at `build/build.csproj`. The Nuke target class is `build/Build.cs`.

- Default build: `./build.sh` (runs the default target)
- Named target: `./build.sh <Target>` — e.g. `Clean`, `Restore`, `Compile`, `CompileApi`, `CompileLinuxGUI`, `CompileWindowsGUI`, `CompileMacGUI`, `CompileWebsite`, `CompileBackgroundJobs`, `CompileConsoleClient`, and matching `Package*` targets.
- Nuke can also be installed globally (`dotnet tool install Nuke.GlobalTool --global`) and invoked as `nuke <Target>`.

Direct `dotnet build src/netrisk.sln` also works for a plain compile, but packaging/artifacts expect Nuke.

## Database Migrations (EF Core)

All migrations live in the `DAL` project but EF must be invoked with `ConsoleClient` as the startup project and the `NRDbContext` context. The wrapper scripts at the repo root encode this:

- Add migration: `./migrationAdd.sh <Name>` (Windows: `migrationAdd.bat`)
- Apply to DB: `./databaseUpdate.sh`
- Generate SQL script: `./migrationScript.sh <MigrationName> <OutputDir>`
- Delete last migration: `./migrationDelete.sh`
- List: `./migrationsList.sh`

Underlying command pattern:
```
dotnet ef <op> --project src/DAL/DAL.csproj \
               --startup-project src/ConsoleClient/ConsoleClient.csproj \
               --context NRDbContext
```

**How migrations actually reach production.** EF `Database.Migrate()` is **not** called at runtime. The runtime upgrade path is **numbered SQL files**: `src/ConsoleClient/DB/Structure/{n}.sql` (DDL) + `DB/Data/{n}.sql` (the `__EFMigrationsHistory` insert and the `update settings set value='{n}' where name='db_version'` bump), applied in order by `DatabaseService.Update()` and tracked by the `db_version` row in `settings`. So adding schema is a two-step ritual: (1) author the EF migration (keeps the model + `NRDbContextModelSnapshot` in sync and generates the SQL via `migrationScript.sh`), then (2) split that SQL into the next numbered `Structure`/`Data` files and bump `targetVersion` in `DB/DatabaseInformation.yaml`. EF migrations sit **on top of** the legacy numbered-SQL base schema, so `Database.Migrate()` cannot build the schema from scratch.

## Database Conventions (Track 6)

New entities must be **born compliant** with the target schema convention (the Track 6 uniformization plan, [docs/plano-uniformizacao-banco.md](docs/plano-uniformizacao-banco.md), converges legacy schema onto it — don't add new drift):

| Item | Standard |
|---|---|
| Tables | `snake_case`, plural (`incidents`, `incident_response_plans`) |
| Columns | `snake_case` (`created_at`, `assigned_to_id`) — set via `HasColumnName` (C# stays PascalCase) |
| FKs | column `<entity>_id` + constraint `fk_<table>_<column>` + a configured EF relationship/navigation |
| Indexes | `idx_<table>_<columns>`; unique `uq_…`; fulltext `ftx_…` |
| Temporal | `created_at` DATETIME NOT NULL, `updated_at` DATETIME NULL — always UTC, no auto-update TIMESTAMP |
| Booleans | `tinyint(1)` |
| Status/enums | `int` + C# enum with explicit `HasConversion` |
| Text | `varchar(n)` when bounded, `TEXT`/`LONGTEXT` when free — **never** BLOB for text |

**Schema-upgrade tooling.** Track 6 phases are applied through a dedicated, auditable command rather than `dotnet ef database update`:

```
netrisk-console database baseline [--env homolog|prod] [--output <file>]          # Phase 0: version, migration/model divergence, removal-candidate census
netrisk-console database upgrade-schema --phase <n> [--env homolog|prod] [--check] [--dry-run] [--yes] [--output <file>]
```

`--check` (read-only pre-flight) and `--dry-run` (emit the exact phase SQL) mutate nothing; a real apply runs backup → census → apply numbered SQL → post-apply validation → write a `schema_upgrade_log` row. Phases are **data-driven** by `src/ConsoleClient/DB/SchemaUpgradePhases.yaml` (target `db_version`, census queries, validations, destructive-gate metadata, removal candidates). To add a phase: add its manifest entry + its numbered SQL files — no command code changes. Destructive phases (`6b`) require `--yes` and an elapsed observation window recorded in `schema_upgrade_log`. The orchestration lives in `ServerServices/SchemaUpgrade` and is covered by `ServerServices.Tests` (unit) and `DAL.IntegrationTests` (Testcontainers MariaDB, `Category=Integration`, needs Docker).

## Required User Secrets

Projects that talk to the DB or server need .NET user-secrets (the `DAL` project cannot hold the connection string — it must be on the startup/consumer projects):

```
dotnet user-secrets init
dotnet user-secrets set "Database:ConnectionString" "server=...;uid=...;pwd=...;Port=3306;database=netrisk;ConvertZeroDateTime=True"
dotnet user-secrets set "Server:Url" "https://127.0.0.1:5443"   # GUIClient
```

## Testing

Frameworks: **xUnit** (`[Fact]`/`[Theory]`) + **NSubstitute** for mocks. Unit test projects: `API.Tests`, `ServerServices.Tests`, `ClientServices.Tests`, `Tools.Tests`. Integration: `DAL.IntegrationTests` (Testcontainers MariaDB — see below).

- Run all tests: `dotnet test src/netrisk.sln`
- Run one project: `dotnet test src/API.Tests/API.Tests.csproj`
- Run one test: `dotnet test --filter "FullyQualifiedName~<ClassName>.<MethodName>"`
- **Skip integration tests** (no Docker): `dotnet test src/netrisk.sln --filter "Category!=Integration"`
- `DAL.IntegrationTests` boots a real MariaDB container via Testcontainers and **requires a running Docker daemon**; its tests are tagged `[Trait("Category", "Integration")]`.

Test authoring conventions are documented in detail in [src/AI_TESTING_INSTRUCTIONS.md](src/AI_TESTING_INSTRUCTIONS.md) — **read it before adding tests**. Key points:

- Never hit real hosts, DBs, or HTTP. Resolve the subject under test from the per-project DI container (`<Project>.Tests.DI.ServiceRegistration.GetServiceProvider()`), and inherit from that project's `BaseControllerTest` / `BaseServiceTest`.
- API controller tests use shared mocks under `API.Tests/Mock` (e.g. `MockedRisksService`) that return deterministic fixtures and throw domain exceptions like `DataNotFoundException`, `PermissionInvalidException`.
- Server service tests use `MockDalService` backed by `MockDbContext`; Sieve filtering is live, so assert both list and total-count tuple values for paged queries.
- Client REST tests use `ClientServices.Tests.Mock.MockSetup.GetRestClient()` — no real HTTP.
- For `ActionResult<T>`, assert on `result.Result`'s concrete `IActionResult` subtype (`OkObjectResult`, `CreatedResult`, `NotFoundResult`, …), then cast `.Value`.

## High-Level Architecture

The solution is `src/netrisk.sln`. Logical layering, bottom-up:

- **`Model`** — POCO entities, DTOs, and domain exceptions shared across tiers. Subdivided by feature area (`Risks`, `Vulnerability`, `Incidents`, `IncidentResponsePlan`, `Assessments`, `Entities`, `Users`, `Authentication`, `Reports`, `Plugins`, `FaceID`, …). Any tier may reference `Model`.
- **`DAL`** — EF Core data access. Owns `NRDbContext` and all migrations. Consumers of the DAL provide the connection string via user-secrets (see above); the DAL itself cannot.
- **`SharedServices`** — code shared between server and client tiers.
- **`ServerServices`** — server-side domain/service layer sitting on top of `DAL`. Uses Mapster (`MapsterConfiguration`) for entity↔DTO mapping and Sieve for filter/page/sort on queries. Consumed by `API`, `BackgroundJobs`, and `ConsoleClient`.
- **`API`** — ASP.NET Core REST API. Controllers are thin and delegate to `ServerServices` interfaces resolved via DI.
- **`BackgroundJobs`** — Hangfire-based job host.
- **`WebSite`** — public-facing site (release downloads, etc.).
- **`ConsoleClient`** — CLI; also used as the EF startup project for migrations.
- **`ClientServices`** — REST client layer consumed by desktop/console clients. Talks to the API via `IRestClient` abstractions (mockable in tests).
- **`GUIClient`** — Avalonia + ReactiveUI desktop app. Depends on `ClientServices` + `SharedServices` + `Model`. Uses `AvaloniaExtraControls` for custom controls. Reads `Server:Url` from user-secrets.
- **`Tools`** — cross-cutting helpers (networking, globalization, math, etc.).
- **`Plugins`** — extension points built on the external `netrisk-plugin-sdk` submodule (in `libs/`).

External submodules live under `libs/` (e.g. `NessusParser`, `Aura.UI`, `netrisk-plugin-sdk`, `reliable-rest-client-wrapper`).

### Request flow (typical)

GUIClient view → ReactiveUI view-model → `ClientServices` REST service → HTTP → `API` controller → `ServerServices` interface → `DAL` (`NRDbContext`) → MariaDB.

## Docs & Roadmap

- [ROADMAP.md](ROADMAP.md) — planned direction (short/medium/long term).
- [CHANGELOG.md](CHANGELOG.md) — Keep-a-Changelog format, SemVer. Record user-visible changes under `[NEXT] - Unreleased` as you work.
- [docs/](docs/) — fundamentals, product guides, and per-feature stubs under [docs/features/](docs/features/).
