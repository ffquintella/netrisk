# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

NetRisk is a cross-platform risk/vulnerability/incident management application built on .NET 10 (see [global.json](global.json)). The codebase is split across a REST API, a desktop GUI (Avalonia), a website, background jobs, a console client, and a plugin system.

## Build & Packaging (Nuke)

Builds are driven by [Nuke](https://nuke.build/). The root bootstrappers (`build.sh` / `build.cmd` / `build.ps1`) will install the SDK if needed and forward arguments to the Nuke project at `build/build.csproj`. The Nuke target class is `build/Build.cs`.

- Default build: `./build.sh` (runs the default target)
- Named target: `./build.sh <Target>` — e.g. `Clean`, `Restore`, `Compile`, `CompileApi`, `CompileLinuxGUI`, `CompileWindowsGUI`, `CompileMacGUI`, `CompileWebsite`, `CompileBackgroundJobs`, `CompileConsoleClient`, and matching `Package*` targets.
- Nuke can also be installed globally (`dotnet tool install Nuke.GlobalTool --global`) and invoked as `nuke <Target>`.

Direct `dotnet build src/netrisk.sln` also works for a plain compile, but packaging/artifacts expect Nuke.

### Desktop installers & signing

The desktop packaging targets (`PackageWindowsGUI`, `PackageWindowsMSI`, `PackageWindowsMSIX`, `PackageLinuxGUI`, `PackageLinuxFlatpak`, `PackageLinuxSnap`, `PackageMacGUI`, `PackageMacA64GUI`, plus the `PackageWindowsInstallers` / `PackageLinuxInstallers` / `PackageAllInstallers` aggregates and `VerifySignatures`) live in the `build/Build.Signing.cs` and `build/Build.Installers.cs` partials. Installer manifests are reviewed templates under `build/installers/`; the pure logic behind them is `build/NetRisk.Packaging` (tested by `src/Packaging.Tests`), and every installer-visible identifier is declared once in `PackageIdentity` — those GUIDs and identities are **append-only**, since changing one turns an upgrade into a parallel install.

Two rules hold for signing: **no credential lives in the repository** (parameters or `NETRISK_*` environment variables only, `[Secret]`-marked, redacted from logs), and **a missing certificate is not a build failure** — the target warns once and emits an unsigned artifact unless `--require-signing` / `--require-notarization` is passed. Cross-building the signed formats is impossible: WiX/`makeappx`/`signtool` need Windows, `codesign`/`notarytool` need macOS, `flatpak-builder`/`snapcraft` need Linux; the targets skip with one warning off-platform. WiX is pinned to **v5** (`dotnet tool install --global wix --version 5.0.2`) because v6/v7 refuse to build without accepting the OSMF EULA; the target reports the install command rather than installing anything itself. Full operational guide: [docs/packaging/release-engineering.md](docs/packaging/release-engineering.md).

## Database Migrations (EF Core)

All migrations live in the `DAL` project but EF must be invoked with `ConsoleClient` as the startup project and the `NRDbContext` context. The wrapper scripts at the repo root encode this:

- Add migration: `./migrationAdd.sh <Name>` (Windows: `migrationAdd.bat`)
- Apply to DB: `./databaseUpdate.sh`
- Generate SQL script: `./migrationScript.sh <MigrationName> <OutputDir>` — note this takes the migration to script **from**, so pass the previous migration's name; `dotnet ef migrations script <from> <to>` is the explicit form.
- Delete last migration: `./migrationDelete.sh`
- List: `./migrationsList.sh`

Underlying command pattern:
```
dotnet ef <op> --project src/DAL/DAL.csproj \
               --startup-project src/ConsoleClient/ConsoleClient.csproj \
               --context NRDbContext
```

**How migrations actually reach production.** EF `Database.Migrate()` is **not** called at runtime. The runtime upgrade path is **numbered SQL files**: `src/ConsoleClient/DB/Structure/{n}.sql` (DDL) + `DB/Data/{n}.sql` (the `__EFMigrationsHistory` insert and the `update settings set value='{n}' where name='db_version'` bump), applied in order by `DatabaseService.Update()` and tracked by the `db_version` row in `settings`. So adding schema is a two-step ritual: (1) author the EF migration (keeps the model + `NRDbContextModelSnapshot` in sync and generates the SQL via `migrationScript.sh`), then (2) split that SQL into the next numbered `Structure`/`Data` files and bump `targetVersion` in `DB/DatabaseInformation.yaml`. EF migrations sit **on top of** the legacy numbered-SQL base schema, so `Database.Migrate()` cannot build the schema from scratch.

**Upgrade scripts must be safe to apply twice.** MariaDB implicitly commits every DDL statement, so a `START TRANSACTION` around a `Structure/{n}.sql` rolls nothing back — a script that dies half-way leaves the database between versions with `db_version` still naming the previous one, and no way forward but hand-written SQL. The contract that replaces it has two halves:

- **`Structure/{n}.sql` carries no transaction, and every statement is guarded.** Use MariaDB's native clauses wherever they exist: `CREATE TABLE IF NOT EXISTS`, `DROP TABLE IF EXISTS`, `CREATE INDEX IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, `ADD CONSTRAINT <name> FOREIGN KEY IF NOT EXISTS (…)`, `DROP {COLUMN,INDEX,FOREIGN KEY,CONSTRAINT} IF EXISTS`. Renames have no such clause, so they go through a probe instead:

  ```sql
  SET @nr_ddl = IF((SELECT COUNT(*) FROM information_schema.TABLES
                     WHERE TABLE_SCHEMA = DATABASE() AND BINARY TABLE_NAME = 'incidents') > 0,
                   'DO 0', 'ALTER TABLE `Incidents` RENAME `incidents`');
  PREPARE nr_ddl FROM @nr_ddl; EXECUTE nr_ddl; DEALLOCATE PREPARE nr_ddl;
  ```

  The `BINARY` is not optional: `information_schema` compares identifiers case-insensitively, so a case-only rename (`Incidents` → `incidents`, `OS` → `os`) looks already-done and gets skipped, leaving the old spelling in place. Two more traps: an `ADD` whose name a **sibling** action in the same `ALTER` drops must stay **unguarded** (MariaDB evaluates `IF NOT EXISTS` against the table as it was *before* the statement, so guarding it skips the re-add and loses the object); and an `INSERT` into `__EFMigrationsHistory` needs `ON DUPLICATE KEY UPDATE`, or the retry dies on the primary key.
- **`Data/{n}.sql` is pure DML inside a real transaction.** No DDL — a single `CREATE`/`ALTER` there commits the transaction out from under the rest of the script. The `db_version` bump goes inside the transaction and is the genuine commit point, so a failed Data script rolls back whole and the retry starts from nothing applied.

Because those guards use a user variable, both appliers route their connection string through `NumberedSqlConnectionString.Normalize` (MySqlConnector otherwise reads `@nr_ddl` as a parameter placeholder and refuses the script). `ConsoleClient.Tests/DB/SchemaUpgradeIdempotenceTest` enforces the convention statement by statement without needing a database, `SchemaUpgradeTableReferencesTest` replays every script in apply order and rejects one that touches a table that does not exist yet, and `DAL.IntegrationTests/SchemaUpgradeRetryTests` applies all 78 versions with each Structure script run twice and requires the schema to match a single clean pass exactly.

**Never give a `string` column a `char(n)` store type.** Use `varchar(n)`. A string is an `IEnumerable<char>`, so EF Core 10's `ElementMappingConvention` treats a `char(n)` string as a primitive collection of `char`; the MySQL provider has no char element mapping, and the model build dies with a `NullReferenceException` that names no property and takes `dotnet ef migrations script`, `HasPendingModelChanges` and `database update` down with it. Writing `HasMaxLength(n).IsFixedLength()` instead only hides it — the generated snapshot re-resolves the store type and writes `HasColumnType("char(n)")` back, so the failure appears one `migrationAdd.sh` later in a file nobody edited. `Guid` columns are unaffected (Pomelo maps them to `char(36)`, but a Guid is not a collection). `DAL.IntegrationTests/StringColumnTypeGuardTest` fails immediately if this is reintroduced, in the model or in the snapshot.

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

**Tests are part of the change, not a follow-up.** Any new feature, endpoint, service method or command must land with tests covering its happy path and each error/guard branch it introduces. Any bug fix must land with a regression test that fails before the fix and passes after. If you find a defect you are not fixing, report it explicitly — never weaken or delete an assertion to get a green run. Full rules in [src/AI_TESTING_INSTRUCTIONS.md](src/AI_TESTING_INSTRUCTIONS.md).

Frameworks: **xUnit v3** (`[Fact]`/`[Theory]`) + **NSubstitute** for mocks. Unit test projects: `API.Tests`, `ServerServices.Tests`, `ClientServices.Tests`, `Tools.Tests`, `GUIClient.Tests`, `SharedServices.Tests`, `BackgroundJobs.Tests`, `ConsoleClient.Tests`, `WebSite.Tests`, `Packaging.Tests`. Integration: `DAL.IntegrationTests` (Testcontainers MariaDB — see below).

`API.Tests` registration is convention-based: `API.Tests/DI/ServiceRegistration.cs` auto-registers every static `Create()` factory in namespace `API.Tests.Mock` against the interface it returns, and every concrete controller in the API assembly. Covering a new controller therefore needs no edit to any shared file — write `APITests/<Name>ControllerTest.cs`, inherit `BaseControllerTest`, and pass per-test doubles through `ResolveController<T>(configure)`, whose registrations are applied last and so win. Controllers that read the database directly get `API.Tests/Mock/InMemoryDalService`; give each test class its own database name. Note that EF `Include` on a **required** navigation inner-joins, so seed the principal rows (`User`, `Entity`, `Role`, …) or your seeded rows read back as an empty list.

`Packaging.Tests` covers `build/NetRisk.Packaging` — the pure packaging logic the Nuke build uses (installer identifiers, per-format version normalisation, signing-material resolution, template rendering, secret redaction). It also **renders the real installer manifests from `build/installers/` and asserts on them**, because MSI/MSIX need Windows tooling and Flatpak/Snap need Linux tooling, so those artifacts cannot be built on a Mac. A template edit that breaks a manifest fails here rather than on a packaging runner.

`GUIClient.Tests` deliberately does **not** reference `GUIClient` — that would pull Avalonia into a headless run. It compiles the specific source files it covers directly (`<Compile Include="..\GUIClient\Validation\*.cs" />`). Anything added there that touches Avalonia types needs a different approach.

**Test platform.** xUnit v3 runs on **Microsoft.Testing.Platform (MTP)**, not VSTest. Three consequences:
1. The root [global.json](global.json) sets `test.runner` to `Microsoft.Testing.Platform`. This is what makes `dotnet test` work — without it the .NET 10 SDK tries VSTest and fails outright. `global.json` is resolved by walking **up from the current directory**, so run `dotnet test` from the repo root (or anywhere below it).
2. Test projects are **self-executing** (`<OutputType>Exe</OutputType>`) and reference `xunit.v3`. They do *not* use `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, or `coverlet.collector` — those are VSTest-only. Coverage comes from `Microsoft.Testing.Extensions.CodeCoverage`.
3. Don't mix VSTest and MTP projects in one solution; it's unsupported. Any new test project must follow the same shape.

- Run all tests: `dotnet test src/netrisk.sln`
- Run one project: `dotnet test --project src/API.Tests/API.Tests.csproj`
- **Coverage**: `dotnet test src/netrisk.sln --coverage --coverage-output-format cobertura` → writes per-project Cobertura XML to `TestResults/`.
- **Filtering**: xUnit's filter flags are **not** forwarded through `dotnet test` (they silently match zero tests). Filter by invoking the built test executable directly:
  - one class: `src/ServerServices.Tests/bin/Debug/net10.0/ServerServices.Tests -class "*RisksServiceInMemoryTest*"`
  - one method: `src/Tools.Tests/bin/Debug/net10.0/Tools.Tests -method "*AsyncHelper*"`
  - **skip integration tests** (no Docker): `src/DAL.IntegrationTests/bin/Debug/net10.0/DAL.IntegrationTests -trait- "Category=Integration"` (the other five projects have no integration-tagged tests)
  - Note: `dotnet test --project <proj>` currently reports "Zero tests ran" (exit 5) for these projects; running the built executable directly works and is the reliable recipe.
- `DAL.IntegrationTests` boots a real MariaDB container via Testcontainers and **requires a running Docker daemon**; its tests are tagged `[Trait("Category", "Integration")]`.
- xUnit v3 changed `IAsyncLifetime` to return `ValueTask` (it was `Task` in v2) — see `MariaDbContainerFixture`.

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

## Security Conventions (Track 7)

The security posture, the findings register and the rotation procedures live in
[docs/security/](docs/security/) — start at its [README](docs/security/README.md). The parts that
constrain day-to-day code:

**Every security claim names the code or the test that establishes it.** Never "handled", never "by
design", never a comment. This repository has three times shipped a control that was documented as
working and was not — `ApplyEntityScope` (filtered nothing), the Master Dashboard backend (did not
exist), and `WebAuthnController`'s "the registration endpoints are authenticated" (no `[Authorize]`
attribute anywhere on the class). A review that cannot name the test must downgrade the claim.

| Need | Use | Never |
|---|---|---|
| A token, key, password or id | `Tools.RandomGenerator` (CSPRNG) or `RandomNumberGenerator` | `System.Random` — it is recoverable from a few observed outputs |
| A path from caller input | `Tools.Security.SafePathTool` | `Path.Combine` alone; it is not a containment primitive |
| Encrypt a stored secret | `ISecretProtector` → `Tools.Criptography.AesGcm256` | `Tools.Criptography.AES` (CBC, constant IV, unauthenticated — read path only) |
| Hash a high-entropy token | `HashTool.CreateSha256` | `CreateMD5` / `CreateSha1` — compatibility reads only |
| Hash a password | bcrypt work factor 15 (`UsersService`) | anything else |
| An outbound HTTP call | `IOutboundHttpClient` (SSRF policy applied) | a bare `HttpClient` |
| Open a URL from domain data | `Tools.Security.ExternalUrlPolicy` then `ArgumentList` | `Process.Start(file, "…" + url)` |
| Compare a secret | `CryptographicOperations.FixedTimeEquals` | `==` / `!=` |

**Every API action needs `[Authorize]` or `[PermissionAuthorize]`.** An unannotated action falls
through to a fallback deny policy, so it is not open — but
`API.Tests/Security/ControllerAuthorizationInventoryTest` fails on it anyway, and on any new
`[AllowAnonymous]` that is not on its justified allowlist. Add the endpoint to the allowlist *with a
reason* only when it genuinely must run before a session exists.

**Configuration precedence is file → user-secrets (Debug) → environment.** Any secret can be supplied
as `Section__Key` in the environment; nothing secret belongs in `appsettings.json`. A Release build
refuses to start with the certificates committed under `src/*/Certificates`.

**Security fixes land with a regression test that fails on the pre-fix code**, like every other fix
(see [src/AI_TESTING_INSTRUCTIONS.md](src/AI_TESTING_INSTRUCTIONS.md)) — and, where the behaviour is
observable at runtime, they are *observed*. Two of this track's own fixes were wrong in a way no unit
test could see: a header the middleware removed and Kestrel re-added below it, and an SSO request id
whose entropy was irrelevant because the attacker chose it.

Local gates, which are the same ones CI runs:

```bash
./scripts/security/scan-dependencies.sh
BASE_REF=<sha> HEAD_REF=<sha> PR_BODY="$(cat msg.txt)" ./scripts/security/check-submodule-bump.sh
```

## Docs & Roadmap

- [ROADMAP.md](ROADMAP.md) — planned direction (short/medium/long term).
- [CHANGELOG.md](CHANGELOG.md) — Keep-a-Changelog format, SemVer. Record user-visible changes under `[NEXT] - Unreleased` as you work.
- [docs/](docs/) — fundamentals, product guides, and per-feature stubs under [docs/features/](docs/features/).
