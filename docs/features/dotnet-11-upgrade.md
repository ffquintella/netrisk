# Upgrade to .NET 11

Status: **specified, not started** (Track 12, M66, tasks T380–T386). This page is the design; nothing in it is
delivered until its task is ticked in the roadmap.

NetRisk builds on .NET 10 (`global.json` SDK `10.0.302`, `net10.0` in every project). This milestone moves the whole
solution, the build, the installers and CI to .NET 11 **with no behaviour change**: no feature, no schema change, no
refactor rides along. A platform upgrade that also changes behaviour cannot be bisected when it breaks.

## Principles

1. **One change in kind.** The diff of the upgrade is target frameworks, package versions and the places that name a
   framework. Anything else found on the way becomes its own task.
2. **Evidence is the suite, not the build.** A green compile proves nothing about runtime behaviour changes in EF Core,
   ASP.NET Core or the Avalonia stack. The milestone is done when the same tests that pass on .NET 10 pass on .NET 11,
   unchanged (an edited assertion is a finding, not a fix).
3. **Blocked is a result.** If a dependency has no .NET 11 build, the upgrade stops there and the blocker is recorded with
   the dependency, the version tried and the failure. It is not worked around by pinning a package to a .NET 10 build
   without saying so.
4. **No partial solution.** `GUIClient`, `ClientServices`, `SharedServices`, `Model` and `Tools` are referenced across
   tiers, so every project moves in the same change. The exception is the plugin SDK (below), which is a submodule with
   its own cadence.

## Inventory (what names the framework today)

| Where | What | Task |
|---|---|---|
| `global.json` | SDK `10.0.302`, `rollForward: latestMajor`, `allowPrerelease: true` | T380 |
| 32 `*.csproj` under `src/` and `build/` | `<TargetFramework>net10.0</TargetFramework>`, repeated per project | T380 |
| `src/Directory.Packages.props` | central package versions: `Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.Authentication.JwtBearer` at `10.0.13`; `Microting.EntityFrameworkCore.MySql` `10.0.12`; Avalonia `12.1.4`, ReactiveUI `26.0.1`, Hangfire `1.8.25`, Mapster `10.0.13`, Testcontainers.MariaDb `4.16.0`, xunit.v3 `4.0.2` | T381 |
| `src/Directory.Build.props` | holds only the product version; it does **not** set the framework | T380 |
| `build/installers/windows/msi/NetRisk.wxs`, `msix/AppxManifest.xml.template` | comments and the OS floor derived from the bundled .NET 10 runtime | T383 |
| `build/installers/linux/flatpak/*.yml.template`, `snap/snapcraft.yaml.template` | `org.freedesktop.Platform` `24.08`; Snap base and the self-contained runtime's ICU/OpenSSL linkage | T383 |
| `src/McpBridge/McpBridge.csproj` | the bridge runs on the machine's .NET, so a client that starts it with a minimal environment needs `DOTNET_ROOT` (see T373) | T383 |
| `build/Build.UiTests.cs`, `scripts/agents/observe-mcp.sh`, `src/GUIClient.Tests/Agents/AgentObservedRunTest.cs` | hard-coded `bin/<config>/net10.0/` output paths | T384 |
| `CLAUDE.md`, `build/README.md`, `docs/packaging/release-engineering.md`, `docs/features/*`, `docs/roadmap/track9/*` | prose and test recipes naming `.NET 10` / `net10.0` | T384 |
| `.github/workflows/{security,ui-compliance,ui-tests}.yml` | `actions/setup-dotnet` steps; they read the SDK from `global.json`, so check that rather than assume | T384 |
| `libs/netrisk-plugin-sdk` and in-tree plugins (`src/Plugins/*`) | the plugin contract assembly and its consumers | T385 |

## Decisions to take before starting

These change the work and are the owner's call; the defaults are recommendations.

1. **Which release.** .NET 11 is a standard-term (STS) release, .NET 10 is long-term support. Moving off LTS is a
   deliberate trade of support window for newer features and fixes. Confirm the support window against Microsoft's
   published policy at the time of the upgrade, and decide whether NetRisk follows STS releases or only LTS. If only
   LTS, this milestone should wait for the next LTS and this page stays as the template. *Default: proceed only if the
   product wants .NET 11 specifically; otherwise defer.*
2. **Centralise the framework.** Declare `TargetFramework` once in `src/Directory.Build.props` and delete it from the
   projects, so the next upgrade is a one-line change. `build/` has its own `Directory.Build.props` that deliberately
   imports nothing, so it is edited separately. *Default: yes, as part of T380, because the alternative is repeating the
   32-file edit.*
3. **Plugin compatibility.** Whether a plugin built for .NET 10 must still load in a .NET 11 host. Assemblies targeting
   an older framework normally load in a newer runtime, but the contract is the plugin SDK's, not the host's. *Default:
   keep the SDK on its current target until a plugin needs otherwise, and prove the load with a test (T385).*
4. **Self-contained or framework-dependent.** The desktop installers bundle the runtime (the MSI and MSIX comments say so);
   the server tiers and the bridge do not. The upgrade keeps each as it is. Changing that is a separate decision.

## Work, in order

Each step ends with the solution restoring and building; steps 3 onward also run the tests named.

1. **SDK and frameworks (T380).** Install the .NET 11 SDK. Set `global.json` to it; drop `allowPrerelease` only once the
   SDK is a stable release, and keep `rollForward` as it is unless there is a reason. Change the framework (once, if
   decision 2 is taken). Build with warnings visible and record every new warning — a new obsoletion warning on a path the
   product uses is a finding even when the build passes.
2. **Packages (T381).** Raise the runtime-tied packages together: `Microsoft.EntityFrameworkCore*`,
   `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*`, `System.Security.Cryptography.*`. Then the ecosystem packages that
   must support the new runtime: the MariaDB provider (`Microting.EntityFrameworkCore.MySql`), Avalonia and its theme and
   control packages, ReactiveUI, Hangfire, Mapster, Testcontainers. For each, record the version used or the blocker.
   The MariaDB provider gates everything below it: EF Core 11 needs a provider built against it, so check that first,
   before touching anything else, and stop here if it does not exist. Run `./scripts/security/scan-dependencies.sh`
   afterwards, since a package bump changes the vulnerable-package result and the suppression file's expiry list.
3. **Schema path (T382).** The upgrade must not alter the database. After the bump:
   - `./migrationAdd.sh DotNet11Probe` must produce an **empty** migration. A non-empty one means the new EF Core changed
     the model's convention-derived mapping; that is a finding to resolve before continuing, and the probe migration is
     deleted either way (`./migrationDelete.sh`), never shipped.
   - `DAL.IntegrationTests/StringColumnTypeGuardTest` must still pass. The `char(n)` string trap exists because EF Core 10
     treats a string as a primitive collection of `char`; a newer EF Core can move that convention, so verify the guard
     fails the way it did when the rule was written, not only that it passes.
   - `ConsoleClient.Tests/DB/SchemaUpgradeIdempotenceTest`, `SchemaUpgradeTableReferencesTest` and
     `DAL.IntegrationTests/SchemaUpgradeRetryTests` (all numbered scripts applied, each Structure script twice, schema
     identical to a clean pass) pass unchanged. These need Docker for the integration ones.
4. **Packaging (T383).** Update what derives from the runtime: the Windows floor in the MSI and MSIX comments (the .NET 11
   runtime's minimum Windows may differ from 1809 — read it, do not carry the number over), the Flatpak runtime version
   and the Snap base, and the Snap's ICU/OpenSSL note. Update `src/Packaging.Tests` so the manifests it renders and
   asserts still hold. The Windows and Linux formats cannot be built on macOS, so on this host the evidence is the
   rendered-manifest tests plus a successful `PackageMacA64GUI`; the other formats are verified on their runners and the
   milestone records which were and were not. `netrisk-mcp-bridge` must still start from an agent client's minimal
   environment; if it needs `DOTNET_ROOT`, that is T373's problem and is noted, not silently fixed here.
5. **Paths and prose (T384).** Replace the `net10.0` output paths in `build/Build.UiTests.cs`,
   `scripts/agents/observe-mcp.sh` and `AgentObservedRunTest.cs`, ideally by deriving the folder from the framework
   property instead of repeating the string. Update `CLAUDE.md`, `build/README.md`, `docs/packaging/release-engineering.md`
   and the live recipes in `docs/features/`. The `docs/roadmap/track9/*` acceptance lists quote `net10.0` paths as
   historical commands; update them only if they are still executed, otherwise leave the record alone and say so.
   Check that each workflow's `setup-dotnet` resolves the .NET 11 SDK from `global.json`.
6. **Plugin contract (T385).** Rebuild `libs/netrisk-plugin-sdk` and `src/Plugins/FixtureVaultPlugin` against the new
   framework. Show with a test that the host loads a plugin built for the previous framework (decision 3). The SDK is a
   submodule, so its change goes through its own commit and the pin bump passes `check-submodule-bump.sh`; the bump's PR
   body states what changed in the submodule.
7. **Release gate (T386).** Run, on .NET 11 and unchanged:
   - every unit project (`API.Tests`, `ServerServices.Tests`, `ClientServices.Tests`, `Tools.Tests`, `GUIClient.Tests`,
     `SharedServices.Tests`, `BackgroundJobs.Tests`, `ConsoleClient.Tests`, `WebSite.Tests`, `Packaging.Tests`,
     `RiskPortal.Tests`);
   - `DAL.IntegrationTests` against a real MariaDB container;
   - `./build.sh LintUi`;
   - `./build.sh TestUi` **once**, as the S54 release gate (never as routine verification);
   - a comparison with the .NET 10 build of start-up time of the API and the GUI, and of one representative report, to
     catch a performance regression. The numbers are recorded in this page; the threshold for "regression" is set before
     measuring (suggested: more than 20% slower on any of the three).

## What can go wrong

| Risk | Why it is likely | How it shows | Response |
|---|---|---|---|
| The MariaDB provider has no EF Core 11 build | it is a third-party fork that trails EF Core | restore or model-build failure in step 2 | stop; record the blocker; do not pin around it |
| EF Core 11 changes a convention and the model snapshot drifts | the `char(n)` trap is one such change already | non-empty probe migration in step 3 | fix the mapping explicitly in the model, never by editing the snapshot |
| Avalonia or a control package lacks .NET 11 support | the desktop stack has the most third-party packages | restore or run failure in `GUIClient` | same rule as the provider; the `libs/TreeDataGrid.Avalonia` submodule is the first suspect |
| A behaviour change in ASP.NET Core 11 breaks authorization or model binding | the controller authorization inventory and the Track 7 tests assert these | `ControllerAuthorizationInventoryTest` or an API test fails | each is a bug to fix with a regression test, not a reason to relax the test |
| The vulnerable-package scan changes | new package versions bring new advisories and drop old ones | `scan-dependencies.sh` output differs | triage per `docs/security/`; expired suppressions are removed, not renewed |
| Installers cannot be verified on this host | WiX, `makeappx`, `flatpak-builder`, `snapcraft` are platform-bound | no artifact for those formats | state which formats were verified only statically; do not claim them built |

## Out of scope

Adopting new .NET 11 language or library features; changing `LangVersion`; moving server tiers to self-contained
publishing; Native AOT or trimming; replacing any dependency that merely has a newer major version; and the Backlog
items T367–T379.

## Definition of done

- Every project, `global.json`, the workflows and the installer templates name .NET 11, and no `net10.0` remains outside
  historical records (`grep -rn net10` is clean or each hit is justified in this page).
- The step 7 suite is green with no assertion edited, and the probe migration of step 3 was empty.
- The dependency scan is clean or its findings are triaged.
- The blockers and unverified installer formats, if any, are listed in the roadmap notes of T381 and T383.
- `CHANGELOG.md` has an entry under `Changed` citing T380–T386 and S56, and the tasks are ticked.
