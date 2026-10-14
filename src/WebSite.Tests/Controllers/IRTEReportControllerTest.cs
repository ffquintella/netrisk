using System.Reflection;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Model;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyncContracts;
using Tools.Security;
using WebSite.Controllers;
using WebSite.Models;
using WebSite.Tests.Sync;
using WebSite.Tools;
using WebSiteData.Entities;
using WebSiteData.Services;
using WebSiteData.Sync;
using Xunit;

namespace WebSite.Tests.Controllers;

/// <summary>
/// The task report page after NR-2026-037: an execution is reachable only through the key e-mailed for
/// it, every failure looks the same, and the report changes only the execution the key binds. Runs the
/// real <see cref="LocalIrpService"/> and <see cref="OutboxService"/> over the real SQLite schema; the
/// substitute-based tests at the end pin that malformed input never reaches the data layer at all.
/// </summary>
[TestSubject(typeof(IRTEReportController))]
public class IRTEReportControllerTest : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();
    private readonly LocalIrpService _irp;

    private static readonly string KeyA = NewKey();
    private static readonly string KeyB = NewKey();

    public IRTEReportControllerTest()
    {
        _irp = new LocalIrpService(_factory, new OutboxService(_factory));

        using var db = _factory.CreateDbContext();
        db.Incidents.Add(new LocalIncident { Id = 100, Name = "INC-A", Description = "incident A" });
        db.Incidents.Add(new LocalIncident { Id = 200, Name = "INC-B", Description = "incident B" });
        db.IrpTasks.Add(new LocalIrpTask { Id = 10, PlanId = 1, IncidentId = 100, Name = "Task A", SuccessCriteria = "ok A" });
        db.IrpTasks.Add(new LocalIrpTask { Id = 20, PlanId = 1, IncidentId = 200, Name = "Task B" });
        db.IrpTaskExecutions.Add(new LocalIrpTaskExecution { Id = 7, TaskId = 10, Status = 1, CreatedAt = DateTime.UtcNow });
        db.IrpTaskExecutions.Add(new LocalIrpTaskExecution { Id = 8, TaskId = 20, Status = 1, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        AddLink(1, KeyA, IrpTaskReportLink.Type, boundTo: 7);
    }

    public void Dispose() => _factory.Dispose();

    private static string NewKey() => global::Tools.RandomGenerator.RandomToken(IrpTaskReportLink.KeyBytes);

    private void AddLink(int id, string key, string type, int boundTo, DateTime? expires = null, bool consumed = false,
        string? dataJson = null)
    {
        using var db = _factory.CreateDbContext();
        db.Links.Add(new LocalLink
        {
            Id = id, Type = type, KeyHash = LinkKeyHash.Primary(key),
            ExpirationDate = expires ?? DateTime.UtcNow.AddDays(30), Consumed = consumed,
            DataJson = dataJson ?? JsonSerializer.Serialize(new IrpTaskReportLinkData { TaskExecutionId = boundTo })
        });
        db.SaveChanges();
    }

    private static LanguageService Language() => new(new ResourceManagerStringLocalizerFactory(
        Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance));

    private IRTEReportController Build(ILocalIrpService? irp = null)
        => new(NullLogger<IRTEReportController>.Instance, Language(), irp ?? _irp);

    private List<IrpTaskStatusChangeDto> Queued()
    {
        using var db = _factory.CreateDbContext();
        return db.Outbox.ToList()
            .Where(o => o.ActionType == SyncActionTypes.IrpTaskStatusChange)
            .Select(o => JsonSerializer.Deserialize<IrpTaskStatusChangeDto>(o.PayloadJson)!)
            .ToList();
    }

    private bool IsConsumed(string key)
    {
        using var db = _factory.CreateDbContext();
        var hash = LinkKeyHash.Primary(key);
        return db.Links.Single(l => l.KeyHash == hash).Consumed;
    }

    private static void AssertIsError(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Error", redirect.ActionName);
    }

    // ---------------- Index ----------------

    [Fact]
    public async Task TestIndexWithTheKeyShowsOnlyItsExecutionsTaskAndIncident()
    {
        var result = await Build().Index(KeyA);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<IRTEReportViewModel>(view.Model);
        Assert.Equal(KeyA, model.Key);
        Assert.Equal("Task A", model.TaskName);
        Assert.Equal("ok A", model.TaskCriteriaToSucceed);
        Assert.Equal("INC-A", model.IncidentName);
        Assert.Equal("incident A", model.IncidentDescription);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("0000000000000000000000000000000000000000007")] // right length, but an id, not a key
    public async Task TestIndexRefusesAnythingButAnIssuedKey(string? key)
    {
        AssertIsError(await Build().Index(key));
    }

    [Fact]
    public async Task TestIndexRefusesAWellFormedKeyThatWasNeverIssued()
    {
        AssertIsError(await Build().Index(KeyB));
    }

    [Fact]
    public async Task TestIndexRefusesAKeyThatDiffersByOneCharacter()
    {
        var tampered = (KeyA[0] == 'A' ? 'B' : 'A') + KeyA[1..];

        AssertIsError(await Build().Index(tampered));
    }

    [Fact]
    public async Task TestIndexRefusesAPasswordResetKeyEvenWithTheSameData()
    {
        AddLink(2, KeyB, SyncLinkTypes.PasswordReset, boundTo: 8);

        AssertIsError(await Build().Index(KeyB));
    }

    [Fact]
    public async Task TestIndexRefusesAnExpiredKey()
    {
        AddLink(2, KeyB, IrpTaskReportLink.Type, boundTo: 8, expires: DateTime.UtcNow.AddMinutes(-1));

        AssertIsError(await Build().Index(KeyB));
    }

    [Fact]
    public async Task TestIndexRefusesAConsumedKey()
    {
        AddLink(2, KeyB, IrpTaskReportLink.Type, boundTo: 8, consumed: true);

        AssertIsError(await Build().Index(KeyB));
    }

    [Fact]
    public async Task TestIndexRefusesAKeyWhoseExecutionIsNoLongerOpen()
    {
        // The bulk sync drops executions in a final state, so their links resolve to nothing.
        AddLink(2, KeyB, IrpTaskReportLink.Type, boundTo: 999);

        AssertIsError(await Build().Index(KeyB));
    }

    [Fact]
    public async Task TestIndexRefusesALinkWithUnreadableData()
    {
        AddLink(2, KeyB, IrpTaskReportLink.Type, boundTo: 0, dataJson: "not json");

        AssertIsError(await Build().Index(KeyB));
    }

    // ---------------- DoReport ----------------

    [Theory]
    [InlineData("succeed", (int)IntStatus.Completed)]
    [InlineData("skip", (int)IntStatus.Skipped)]
    [InlineData("fail", (int)IntStatus.Failed)]
    public async Task TestReportQueuesTheOutcomeForTheKeysExecutionAndConsumesTheKey(string outcome, int status)
    {
        var result = await Build().DoReport(new IRTEReportViewModel { Key = KeyA, Result = outcome });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(outcome, Assert.IsType<IRTEReportViewModel>(view.Model).Result);
        var queued = Assert.Single(Queued());
        Assert.Equal(7, queued.TaskExecutionId);
        Assert.Equal(status, queued.NewStatus);
        Assert.Equal(KeyA, queued.ReportKey);
        Assert.True(IsConsumed(KeyA));
    }

    [Fact]
    public async Task TestAKeyReportsOnlyOnce()
    {
        var controller = Build();

        await controller.DoReport(new IRTEReportViewModel { Key = KeyA, Result = "succeed" });
        var second = await controller.DoReport(new IRTEReportViewModel { Key = KeyA, Result = "fail" });

        AssertIsError(second);
        Assert.Single(Queued());
        AssertIsError(await controller.Index(KeyA));
    }

    [Fact]
    public async Task TestReportWithAnUnissuedKeyQueuesNothing()
    {
        AssertIsError(await Build().DoReport(new IRTEReportViewModel { Key = KeyB, Result = "succeed" }));

        Assert.Empty(Queued());
        Assert.False(IsConsumed(KeyA));
    }

    [Fact]
    public async Task TestReportWithAnUnknownOutcomeQueuesNothingAndKeepsTheKey()
    {
        var result = await Build().DoReport(new IRTEReportViewModel { Key = KeyA, Result = "delete-everything" });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("", Assert.IsType<IRTEReportViewModel>(view.Model).Result);
        Assert.Empty(Queued());
        Assert.False(IsConsumed(KeyA));
    }

    [Fact]
    public async Task TestReportViewEchoesNothingElseThePostCarried()
    {
        var result = await Build().DoReport(new IRTEReportViewModel
            { Key = KeyA, Result = "succeed", IncidentName = "<script>forged</script>" });

        var model = Assert.IsType<IRTEReportViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("", model.IncidentName);
        Assert.Equal("", model.Key);
    }

    [Fact]
    public async Task TestReportWithNoModelIsABadRequest()
    {
        Assert.IsType<BadRequestResult>(await Build().DoReport(null));
    }

    [Fact]
    public void TestIndexIsGetOnlyAndTheModelCarriesNoExecutionId()
    {
        Assert.NotNull(typeof(IRTEReportController).GetMethod(nameof(IRTEReportController.Index))!
            .GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(typeof(IRTEReportViewModel).GetProperty("TaskExecutionId"));
        Assert.Null(typeof(IRTEReportViewModel).GetProperty("TaskId"));
    }

    // ---------------- End to end through the sync ----------------

    [Fact]
    public async Task TestAKeyPushedOnTheFastLaneOpensTheExecutionPushedOnTheBulkLane()
    {
        var key = NewKey();
        var apply = new SyncApplyService(_factory, new OutboxService(_factory));
        await apply.ApplyPushAsync(new PushPayload
        {
            Cursor = 1,
            Incidents = { new IncidentDto { Id = 300, Name = "INC-C" } },
            IrpTasks = { new IrpTaskDto { Id = 30, PlanId = 1, IncidentId = 300, Name = "Task C" } },
            IrpExecutions = { new IrpTaskExecutionDto { Id = 9, TaskId = 30, Status = 1, CreatedAt = DateTime.UtcNow } }
        });
        await apply.ApplyFastAsync(new FastPushPayload
        {
            Links =
            {
                new LinkDto
                {
                    Id = 50, Type = IrpTaskReportLink.Type, KeyHash = LinkKeyHash.Primary(key),
                    ExpirationDate = DateTime.UtcNow.AddDays(30),
                    DataJson = JsonSerializer.Serialize(new IrpTaskReportLinkData { TaskExecutionId = 9 })
                }
            }
        });

        var view = Assert.IsType<ViewResult>(await Build().Index(key));
        Assert.Equal("INC-C", Assert.IsType<IRTEReportViewModel>(view.Model).IncidentName);
        AssertIsError(await Build().Index("9"));
    }

    // ---------------- Malformed input never reaches the data layer ----------------

    [Theory]
    [InlineData("7")]
    [InlineData("42")]
    [InlineData("not a key")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 42 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // 43 chars, not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // 43 chars, padding
    public async Task TestMalformedKeysAreRefusedBeforeAnyLookup(string key)
    {
        var irp = Substitute.For<ILocalIrpService>();
        var controller = Build(irp);

        AssertIsError(await controller.Index(key));
        AssertIsError(await controller.DoReport(new IRTEReportViewModel { Key = key, Result = "succeed" }));

        Assert.Empty(irp.ReceivedCalls());
    }

    [Fact]
    public async Task TestAnOverlongKeyIsRefusedBeforeAnyLookup()
    {
        var irp = Substitute.For<ILocalIrpService>();
        var key = new string('A', 100_000);

        AssertIsError(await Build(irp).Index(key));

        Assert.Empty(irp.ReceivedCalls());
    }

    [Fact]
    public async Task TestAFailingDataLayerEndsOnTheErrorPage()
    {
        var irp = Substitute.For<ILocalIrpService>();
        irp.GetTaskExecutionByReportKeyAsync(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("db down"));
        irp.QueueTaskReportAsync(Arg.Any<string>(), Arg.Any<int>()).ThrowsAsync(new InvalidOperationException("db down"));
        var controller = Build(irp);

        AssertIsError(await controller.Index(KeyA));
        AssertIsError(await controller.DoReport(new IRTEReportViewModel { Key = KeyA, Result = "succeed" }));
    }

    [Fact]
    public async Task TestIndexWithAKeyWhoseTaskIsMissingEndsOnTheErrorPage()
    {
        var irp = Substitute.For<ILocalIrpService>();
        irp.GetTaskExecutionByReportKeyAsync(KeyA).Returns(new LocalIrpTaskExecution { Id = 7, TaskId = 10 });
        irp.GetTaskAsync(10).Returns((LocalIrpTask?)null);

        AssertIsError(await Build(irp).Index(KeyA));
        await irp.DidNotReceive().GetIncidentForTaskAsync(Arg.Any<int>());
    }
}
