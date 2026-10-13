# AI Unit Testing Instructions for netrisk

This guide defines how to write unit tests that match the established patterns in this repository. It is intended for an agent that will author tests across the API, ServerServices, ClientServices, and Tools projects.

## When tests are mandatory

Every change ships with tests. Specifically:

- **New feature, endpoint, service method, or command** — add tests in the same change, covering the happy path *and* each error/guard branch the code introduces (not found, invalid input, permission denied, domain exception mapped to a status code). A feature is not done until its tests exist and pass.
- **Bug fix** — write a test that fails on the unfixed code and passes after the fix, then fix it. Commit both together, so the bug cannot come back unnoticed.
- **Bug found but not fixed** — if you discover a defect outside the scope of what you were asked to change, do not silently drop it and do not weaken a test to hide it. Either write a test asserting the *current* behaviour with a comment naming it a known limitation, or leave the test out and report the defect explicitly in your summary. Never delete or loosen an assertion just to get a green run.
- **Test reveals a real bug** — that is a success, not an obstacle. Report it. Fix the code, not the test, unless the user says otherwise.

When a test cannot reasonably be written (needs real hardware, a live third-party endpoint, an ONNX model, a real SMTP server), say so explicitly and name what blocked it rather than leaving a silent gap.

## Frameworks and Tooling

- Test runner: xUnit (`[Fact]`, `[Theory]`, `[InlineData]`).
- Mocking: NSubstitute for interfaces and collaborators.
- DI: Microsoft `ServiceCollection` with project-specific `ServiceRegistration` helpers.
- Logging: Serilog injected via DI; do not assert on log output.
- Target framework: `net9.0` and C# `LangVersion` 13.
- Optional: `JetBrains.Annotations` `[TestSubject(typeof(...))]` attribute used to annotate the class under test.

## Project and File Placement

- API layer tests go in `API.Tests` mirroring controllers (e.g., `APITests/<ControllerName>Test.cs`).
- Server-side domain/service tests go in `ServerServices.Tests` under `ServiceTests`.
- Client REST service tests go in `ClientServices.Tests` under `Services`.
- Tools/library helpers go in `Tools.Tests` under a domain folder (e.g., `Helpers`, `Network`, `Globalization`, `Math`).
- File naming: Use `*Test.cs` or `*Tests.cs` matching existing project conventions.
- Class naming: `<SubjectName>Test` or `<SubjectName>Tests`. Add `[TestSubject(typeof(<SubjectType>))]` when applicable.

## Common Test Structure

- Prefer the Arrange/Act/Assert pattern with clear sections and minimal comments.
- Asynchronous APIs: mark test methods `public async Task <Name>()` and use `await`.
- Use xUnit assertions consistently: `Assert.NotNull`, `Assert.Equal`, `Assert.True`, `Assert.IsType<T>`, `Assert.ThrowsAsync<TException>(...)`.
- Cover both success paths and error paths (e.g., not found, permission invalid).

## Dependency Injection Setup

- Do not spin up real hosts or hit external resources. Always resolve the subject from a project-specific DI container:
  - API controllers: inherit from `API.Tests.APITests.BaseControllerTest` and resolve controllers from `_serviceProvider` created by `API.Tests.DI.ServiceRegistration.GetServiceProvider()`.
  - Server services: inherit from `ServerServices.Tests.ServiceTests.BaseServiceTest` and resolve interfaces from `_serviceProvider` created by `ServerServices.Tests.DI.ServiceRegistration.GetServiceProvider()`.
  - Client REST services: inherit from `ClientServices.Tests.Services.BaseServiceTest` and resolve interfaces from `_serviceProvider` created by `ClientServices.Tests.DI.ServiceRegistration.GetServiceProvider()`.

## Mocking Conventions

- API controller tests:
  - Use the mocks provided in `API.Tests/Mock` (e.g., `MockedIncidentResponsePlansService`, `MockedRisksService`, `MockedUsersService`, etc.). These are registered in `API.Tests.DI.ServiceRegistration` and return deterministic data or throw domain exceptions (e.g., `DataNotFoundException`).
  - When testing controller actions returning `ActionResult<T>`, assert the outer `IActionResult` type (e.g., `OkObjectResult`, `CreatedResult`, `NotFoundResult`), then cast `.Value` to the expected model and assert fields.

- Server services tests:
  - Use `ServerServices.Tests.Mock.MockDalService` (backed by `MockDbContext`) and other mocks (e.g., `EmailMock`, `FilesServiceMock`) wired in `ServerServices.Tests.DI.ServiceRegistration`.
  - Many methods throw domain exceptions on invalid IDs or permissions; assert these using `Assert.ThrowsAsync<...>`.
  - Sieve filtering is enabled; when testing paged/filter queries, assert both the returned list count and the total count tuple values.

- Client REST services tests:
  - Do not perform real HTTP calls. Use `ClientServices.Tests.Mock.MockSetup.GetRestClient()` to get a mocked `IRestClient` with preconfigured route responses (see `MockIncidentResponsePlan`, `MockRisks`, `MockHostsRestService`, `MockComments`).
  - Resolve services via DI and assert that methods deserialize responses correctly and map to the expected models.
  - For error paths, mocks may return error codes or throw; assert thrown exceptions where configured.

## Assertions for Controllers (API.Tests)

- Pattern for `ActionResult<T>`:
  1. `var result = await controller.Method(...);`
  2. `Assert.NotNull(result);`
  3. `Assert.IsType<OkObjectResult | CreatedResult | NotFoundResult | BadRequestObjectResult>(result.Result);`
  4. If applicable: cast and assert payload
     - `var ok = (OkObjectResult)result.Result;`
     - `var model = Assert.IsType<TModel>(ok.Value);`
     - Assert IDs, counts, and important fields.

## Naming and Style

- Method names start with `Test` followed by the method or behavior (e.g., `TestGetByIdAsync`, `TestAssociateRiskToIncidentResponsePlanAsync`).
- Keep tests small and focused. Avoid testing multiple unrelated behaviors in a single test.
- Prefer literal values present in mocks for assertions (e.g., IDs 1/2, names like "Test", counts like 2/15) to keep tests deterministic.
- Use `var` where type is obvious; explicit types are acceptable when it improves clarity.

## Examples (Patterns to Replicate)

- Success result list count:
  - Tools: `Assert.Equal(expected, list.Count);`
  - Server services: also assert total tuple item (e.g., `result.Item2`).

- Error assertions:
  - `await Assert.ThrowsAsync<DataNotFoundException>(async () => await service.GetByIdAsync(999));`
  - `await Assert.ThrowsAsync<PermissionInvalidException>(async () => await service.CreateAsync(entity, userWithoutPermission));`

- Controller action result typing:
  - `Assert.IsType<OkObjectResult>(result.Result);`
  - `Assert.IsType<CreatedResult>(result.Result);`
  - `Assert.IsType<NotFoundResult>(result);` for actions returning `IActionResult` directly.

## What Not to Do

- Do not create real databases or make network calls.
- Do not modify production DI setups; use the test `ServiceRegistration` classes.
- Do not assert on log output or time-sensitive fields unless explicitly mocked.
- Do not share state across tests; resolve fresh subjects per test class constructor and rely on deterministic mocks.

## Running Tests

- From the solution directory, run: `dotnet test netrisk.sln`. This does **not** run
  `GUIClient.UiTests` — see the next section.
- Keep tests deterministic and fast to support frequent local runs and CI.

## Desktop UI tests (`GUIClient.UiTests`) — on demand or at release only (S54)

`src/GUIClient.UiTests` holds headless Avalonia UI tests: real views, the client's own styles,
bindings and simulated mouse and keyboard input. They are too expensive for the development loop,
so the rule of [docs/testing/desktop-ui-tests.md](../docs/testing/desktop-ui-tests.md) §3 applies to
every agent and every brief:

- **Never run them as routine verification** — not in `dotnet test`, not in a `test-runner` brief,
  not to "double-check" a GUI change. Run them only when the user explicitly asks, or as the release
  gate: `./build.sh TestUi` before `git tag vX.Y.Z`, and `.github/workflows/ui-tests.yml` on the tag.
  "Verify the change" or "run the tests" is not an explicit request (E6).
- **When you change a view or view-model a UI test covers**, update the test in the same change so it
  still compiles (`Compile` builds the project), do not run it, and say *"UI tests not run (S54 §3)"*
  in your summary.
- Two locks keep this true; do not remove either. The `.csproj` sets `IsTestingPlatformApplication`
  (and `IsTestProject`) to `false`, so `dotnet test src/netrisk.sln` never selects the project. And
  every test skips unless `NETRISK_UI_TESTS=1`, which only `TestUi` and the workflow set.

Conventions when you write or update one:

- **Declare tests with `[UiFact]` / `[UiTheory]` only** (`Infrastructure/UiFactAttribute.cs`), never a
  bare `[Fact]`, `[Theory]`, `[AvaloniaFact]` or `[AvaloniaTheory]`: those run without the opt-in.
  `Guards/UiTestAttributeGuardTest` fails on one.
- **Doubles come from the project helper, `UiServices`.** Each test gets a fresh container (the
  Application is rebuilt per test). Localization, toasts, navigation and the dialog service are the
  client's real ones; every other interface a view-model resolves is an NSubstitute double that
  answers like an empty server (empty lists, not nulls). Configure what the test needs through
  `UiServices.Current.Double<IHostsService>()` **before** creating the view — view-models read their
  services in their constructors. Open dialogs through `IDialogService`, as the client does, with
  `IMainWindowProvider.GetActiveWindow()` pointed at an owner window.
- **No network, database, user-secret or real server** — and no production data: fixtures are
  synthetic (`@example.invalid` addresses, made-up hosts).
- **Drive the UI with `UiInput`** (clicks, keys, typed text through the headless platform), not by
  setting view-model properties, when the test is about interaction.
- **Message boxes are answered, not avoided.** Every test runs under a classic-desktop lifetime
  (`Infrastructure/UiDesktop.cs`), so the client's MsBox confirmations and error boxes open as windows:
  `await UiMessageBoxes.WaitForAsync(...)`, assert on `UiMessageBoxes.Text(box)`, then
  `await UiMessageBoxes.AnswerAsync(box, "Yes")`. A double whose first answer the view-model must not see
  synchronously (a constructor that starts a load and is subscribed to afterwards) can complete after a
  dispatcher hop, as the REST client does — see `Flows/Risks/RiskScreen.cs` (`RiskServer.Later`).
- **End every test with `UiHost.AssertClean()`.** A standing binding error, an Avalonia error-level log
  (for example a render-loop exception) or an exception escaping a ReactiveCommand fails the test. A
  product defect the suite has found but that is not being fixed is tolerated only by name, through an
  entry in `Infrastructure/KnownDefects.cs` passed to `AssertClean(...)` — and that entry fails the
  test as soon as the defect stops occurring, so it has to be removed with the fix.
- `xunit.v3` is pinned to 3.2.2 in this project (`VersionOverride`): Avalonia.Headless.XUnit is not
  compatible with xunit.v3 4.x yet. Keep the project's other packages on the central versions.

## Minimal Templates

### Controller test (API)

```csharp
[TestSubject(typeof(RisksController))]
public class RisksControllerTest : BaseControllerTest
{
    private readonly RisksController _controller;

    public RisksControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<RisksController>();
    }

    [Fact]
    public async Task TestGetOpenVulnerabilities()
    {
        // Act
        var result = await _controller.GetOpenVulnerabilities(1);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
        var ok = (OkObjectResult)result.Result;
        var list = Assert.IsType<List<Vulnerability>>(ok.Value);
        Assert.Equal(2, list.Count);
    }
}
```

### Server service test

```csharp
[TestSubject(typeof(IncidentResponsePlansService))]
public class IncidentResponsePlansServiceTest : BaseServiceTest
{
    private readonly IIncidentResponsePlansService _svc;

    public IncidentResponsePlansServiceTest()
    {
        _svc = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }

    [Fact]
    public async Task TestGetByIdAsync()
    {
        var plan = await _svc.GetByIdAsync(1);
        Assert.NotNull(plan);
        Assert.Equal(1, plan.Id);
    }
}
```

### Client REST service test

```csharp
public class IncidentResponsePlansRestServiceTests : BaseServiceTest
{
    private readonly IIncidentResponsePlansService _svc;
    public IncidentResponsePlansRestServiceTests()
    {
        _svc = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }

    [Fact]
    public async Task TestGetAllAsync()
    {
        var plans = await _svc.GetAllAsync();
        Assert.NotNull(plans);
        Assert.Equal(2, plans.Count);
    }
}
```

### Tools test with data-driven cases

```csharp
[TestSubject(typeof(FqdnTool))]
public class FqdnToolTest
{
    [Theory]
    [InlineData("example.com", true)]
    [InlineData("invalid_domain", false)]
    public void IsValidTest(string fqdn, bool expected)
    {
        var result = FqdnTool.IsValid(fqdn);
        Assert.Equal(expected, result);
    }
}
```

