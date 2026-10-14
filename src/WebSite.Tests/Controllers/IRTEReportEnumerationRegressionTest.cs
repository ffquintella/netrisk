using System.Reflection;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebSite.Controllers;
using WebSite.Models;
using WebSite.Tests.Sync;
using WebSite.Tools;
using WebSiteData.Entities;
using WebSiteData.Services;
using Xunit;

namespace WebSite.Tests.Controllers;

/// <summary>
/// NR-2026-037 regression tests. The task report page took the task-execution id itself as its key, so
/// anyone could walk the sequential ids to read every open incident's tasks, and post any id to mark
/// that execution completed, skipped or failed — no session, no antiforgery token.
///
/// This file deliberately touches only what the page exposed before the fix (the controller's
/// constructor, <c>Index(key)</c>, <c>DoReport(vm)</c> with a <c>Result</c>) and runs the real
/// <see cref="LocalIrpService"/> over the real SQLite schema, so it compiles against the pre-fix code
/// unchanged and was run there to observe it fail (see the T340 note in ROADMAP.md).
/// </summary>
[TestSubject(typeof(IRTEReportController))]
public class IRTEReportEnumerationRegressionTest : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();
    private readonly IRTEReportController _controller;

    public IRTEReportEnumerationRegressionTest()
    {
        var language = new LanguageService(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance));
        _controller = new IRTEReportController(NullLogger<IRTEReportController>.Instance, language,
            new LocalIrpService(_factory, new OutboxService(_factory)));

        // What the bulk sync leaves on the website: open executions with their task and incident.
        using var db = _factory.CreateDbContext();
        for (var id = 1; id <= 10; id++)
        {
            db.Incidents.Add(new LocalIncident { Id = id, Name = $"INC-{id}", Description = $"secret-{id}" });
            db.IrpTasks.Add(new LocalIrpTask { Id = id, PlanId = 1, IncidentId = id, Name = $"Task-{id}" });
            db.IrpTaskExecutions.Add(new LocalIrpTaskExecution { Id = id, TaskId = id, Status = 1, CreatedAt = DateTime.UtcNow });
        }
        db.SaveChanges();
    }

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("10")]
    public async Task TestASequentialExecutionIdRevealsNoTaskOrIncident(string key)
    {
        var result = await _controller.Index(key);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Error", redirect.ActionName);
    }

    [Fact]
    public async Task TestWalkingTheIdSpaceRevealsNothing()
    {
        for (var id = 0; id <= 20; id++)
        {
            var result = await _controller.Index(id.ToString());
            Assert.IsNotType<ViewResult>(result);
        }
    }

    [Fact]
    public async Task TestAReportNamingAnExecutionIdQueuesNothing()
    {
        // What model binding does with a posted TaskExecutionId=7: set the property if the model has one.
        // After the fix it has none, so the forged field has nowhere to land.
        var vm = new IRTEReportViewModel { Result = "succeed" };
        typeof(IRTEReportViewModel).GetProperty("TaskExecutionId")?.SetValue(vm, 7);

        await _controller.DoReport(vm);

        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Outbox);
    }

    [Theory]
    [InlineData("succeed")]
    [InlineData("skip")]
    [InlineData("fail")]
    public async Task TestAReportWithoutAKeyQueuesNothing(string outcome)
    {
        await _controller.DoReport(new IRTEReportViewModel { Result = outcome });

        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Outbox);
    }

    [Fact]
    public void TestTheReportIsAPostCheckedForAnAntiforgeryToken()
    {
        var doReport = typeof(IRTEReportController).GetMethod(nameof(IRTEReportController.DoReport))!;

        Assert.NotNull(doReport.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(doReport.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
