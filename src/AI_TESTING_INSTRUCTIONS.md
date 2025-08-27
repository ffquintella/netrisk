# AI Unit Testing Instructions for netrisk

This guide defines how to write unit tests that match the established patterns in this repository. It is intended for an agent that will author tests across the API, ServerServices, ClientServices, and Tools projects.

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

- From the solution directory, run: `dotnet test netrisk.sln`.
- Keep tests deterministic and fast to support frequent local runs and CI.

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

