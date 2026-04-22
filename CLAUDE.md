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

## Required User Secrets

Projects that talk to the DB or server need .NET user-secrets (the `DAL` project cannot hold the connection string — it must be on the startup/consumer projects):

```
dotnet user-secrets init
dotnet user-secrets set "Database:ConnectionString" "server=...;uid=...;pwd=...;Port=3306;database=netrisk;ConvertZeroDateTime=True"
dotnet user-secrets set "Server:Url" "https://127.0.0.1:5443"   # GUIClient
```

## Testing

Frameworks: **xUnit** (`[Fact]`/`[Theory]`) + **NSubstitute** for mocks. Test projects: `API.Tests`, `ServerServices.Tests`, `ClientServices.Tests`, `Tools.Tests`.

- Run all tests: `dotnet test src/netrisk.sln`
- Run one project: `dotnet test src/API.Tests/API.Tests.csproj`
- Run one test: `dotnet test --filter "FullyQualifiedName~<ClassName>.<MethodName>"`

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

GUIClient view → ReactiveUI view-model → `ClientServices` REST service → HTTP → `API` controller → `ServerServices` interface → `DAL` (`NRDbContext`) → MySQL.

## Docs & Roadmap

- [ROADMAP.md](ROADMAP.md) — planned direction (short/medium/long term).
- [CHANGELOG.md](CHANGELOG.md) — Keep-a-Changelog format, SemVer. Record user-visible changes under `[NEXT] - Unreleased` as you work.
- [docs/](docs/) — fundamentals, product guides, and per-feature stubs under [docs/features/](docs/features/).
