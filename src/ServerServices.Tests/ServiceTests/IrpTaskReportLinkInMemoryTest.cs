using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Model;
using Serilog;
using ServerServices.EmailTemplates;
using ServerServices.Interfaces;
using ServerServices.Services;
using SyncContracts;
using Tools.Security;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// NR-2026-037, API half: the key e-mailed to an incident-response task assignee. It used to be the
/// task's sequential id (and the website took it as an execution id), so anyone could enumerate every
/// open incident's tasks and change their status. These tests pin the replacement: an unguessable,
/// hashed-at-rest, single-use key bound to exactly one execution, pushed to the website and re-checked
/// when the website's report comes back.
/// </summary>
[TestSubject(typeof(IncidentResponsePlansService))]
public class IrpTaskReportLinkInMemoryTest : InMemoryServiceTestBase
{
    private static readonly User Admin = new() { Value = 1, Name = "Admin", Type = "local", Admin = true, Lang = "en" };

    /// <summary>Records the template parameters, which the shared EmailMock does not keep.</summary>
    private sealed class RecordingEmailService : IEmailService
    {
        public List<object> Parameters { get; } = new();

        public Task SendEmailAsync(string to, string subject, string template, string localizationCode, object parameters)
        {
            Parameters.Add(parameters);
            return Task.CompletedTask;
        }

        public Task SendNotificationAsync(string to, string subject, string htmlBody, string? plainTextBody = null)
            => Task.CompletedTask;
    }

    private readonly RecordingEmailService _email = new();

    private IIncidentResponsePlansService BuildPlans() => new IncidentResponsePlansService(
        GetService<ILogger>(), GetService<IDalService>(), GetService<IMessagesService>(),
        GetService<ILocalizationService>(), _email, GetService<IEntitiesService>(),
        GetService<IConfiguration>(), GetService<INotificationEventPublisher>(), GetService<ILinksService>());

    private ISyncIngestService BuildIngest() => new SyncIngestService(
        GetService<ILogger>(), GetService<IDalService>(),
        GetService<IFixRequestsService>(), GetService<ICommentsService>(), GetService<IMessagesService>(),
        GetService<IUsersService>(), GetService<ILinksService>(), GetService<IIncidentResponsePlansService>());

    private ISyncPushBuilder BuildPush() => new SyncPushBuilder(
        GetService<ILogger>(), GetService<IDalService>(),
        GetService<IFixRequestsService>(), GetService<ICommentsService>(), GetService<ITeamsService>(),
        GetService<IUsersService>(), GetService<IIncidentResponsePlansService>(), GetService<ILinksService>());

    private Incident SeedPlanWithOneTask()
    {
        var incident = new Incident { Id = 1, Description = "d", Name = "INC" };
        var plan = new IncidentResponsePlan
            { Id = 1, Name = "IRP", Description = "d", CreationDate = DateTime.Now, LastUpdate = DateTime.Now };
        incident.IncidentResponsePlansActivated.Add(plan);
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(plan);
            ctx.IncidentResponsePlanTasks.Add(new IncidentResponsePlanTask { Id = 1, PlanId = 1, Name = "T", AssignedToId = 50 });
            ctx.IncidentResponsePlanExecutions.Add(new IncidentResponsePlanExecution
            {
                Id = 1, PlanId = 1, ExecutionTrigger = "m", ExecutionResult = "-", Duration = TimeSpan.Zero
            });
            var entity = new Entity { Id = 50, DefinitionName = "person", DefinitionVersion = "1", Status = "active",
                Created = DateTime.Now, Updated = DateTime.Now };
            entity.EntitiesProperties.Add(new EntitiesProperty { Id = 1, Entity = 50, Type = "email", Value = "a@b.io", OldValue = "", Name = "email" });
            ctx.Entities.Add(entity);
            ctx.Incidents.Add(incident);
        });
        return incident;
    }

    private static IncidentResponsePlanTaskExecution NewExecution() => new()
    {
        Id = 0, PlanExecutionId = 1, TaskId = 1, ExecutionDate = DateTime.Now, Duration = TimeSpan.Zero
    };

    private static string KeyOf(string reportLink)
    {
        const string marker = "/IRTEReport?key=";
        var at = reportLink.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"not a report link: {reportLink}");
        return reportLink[(at + marker.Length)..];
    }

    private void SeedExecutionAndLink(int executionId, string key, int boundTo, DateTime? expires = null)
    {
        Seed(ctx =>
        {
            ctx.IncidentResponsePlanTaskExecutions.Add(new IncidentResponsePlanTaskExecution
            {
                Id = executionId, PlanExecutionId = 1, TaskId = 1, Status = (int)IntStatus.New,
                CreatedAt = DateTime.Now.AddHours(-1), ExecutionDate = DateTime.Now, Duration = TimeSpan.Zero
            });
            ctx.Links.Add(new Link
            {
                Type = IrpTaskReportLink.Type,
                KeyHash = LinkKeyHash.Primary(key),
                CreationDate = DateTime.Now,
                ExpirationDate = expires ?? DateTime.Now.AddDays(30),
                Data = JsonSerializer.SerializeToUtf8Bytes(new IrpTaskReportLinkData { TaskExecutionId = boundTo })
            });
        });
    }

    private static OutboxActionDto ReportAction(int executionId, int status, string key) => new()
    {
        ClientActionId = Guid.NewGuid(),
        ActionType = SyncActionTypes.IrpTaskStatusChange,
        ActionTimeUtc = DateTime.UtcNow,
        PayloadJson = JsonSerializer.Serialize(new IrpTaskStatusChangeDto
        {
            TaskExecutionId = executionId, NewStatus = status, ActionTimeUtc = DateTime.UtcNow, ReportKey = key
        })
    };

    private int StatusOf(int executionId)
    {
        using var ctx = OpenContext();
        return ctx.IncidentResponsePlanTaskExecutions.Single(e => e.Id == executionId).Status;
    }

    private int ReportLinkCount()
    {
        using var ctx = OpenContext();
        return ctx.Links.Count(l => l.Type == IrpTaskReportLink.Type);
    }

    private static readonly string KeyA = RandomKey();
    private static readonly string KeyB = RandomKey();
    private static string RandomKey() => global::Tools.RandomGenerator.RandomToken(IrpTaskReportLink.KeyBytes);

    // ---------------- Issuing the key ----------------

    [Fact]
    public async Task TestTaskEmailCarriesAnUnguessableKeyNotASequentialId()
    {
        var incident = SeedPlanWithOneTask();

        var created = await BuildPlans().CreateTaskExecutionAsync(NewExecution(), incident, Admin);

        var mail = Assert.IsType<TaskExecution>(Assert.Single(_email.Parameters));
        Assert.StartsWith("http://localhost:5000/IRTEReport?key=", mail.ReportLink);
        var key = KeyOf(mail.ReportLink);
        Assert.True(IrpTaskReportLink.IsWellFormedKey(key), $"key '{key}' is not a {IrpTaskReportLink.KeyLength}-char base64url token");
        Assert.NotEqual(created.Id.ToString(), key);
        Assert.NotEqual(created.TaskId.ToString(), key);
    }

    [Fact]
    public async Task TestKeyIsStoredOnlyAsItsSha256AndBindsTheCreatedExecution()
    {
        var incident = SeedPlanWithOneTask();

        var created = await BuildPlans().CreateTaskExecutionAsync(NewExecution(), incident, Admin);
        var key = KeyOf(Assert.IsType<TaskExecution>(Assert.Single(_email.Parameters)).ReportLink);

        using var ctx = OpenContext();
        var link = Assert.Single(ctx.Links.Where(l => l.Type == IrpTaskReportLink.Type));
        Assert.Equal(HashTool.CreateSha256(key), link.KeyHash);
        Assert.Equal(64, link.KeyHash.Length);
        Assert.DoesNotContain(key, link.KeyHash);
        Assert.DoesNotContain(key, Encoding.UTF8.GetString(link.Data!));
        var data = JsonSerializer.Deserialize<IrpTaskReportLinkData>(link.Data!);
        Assert.Equal(created.Id, data!.TaskExecutionId);
        Assert.True(link.ExpirationDate > DateTime.Now.AddDays(29));
        Assert.True(link.ExpirationDate <= DateTime.Now.Add(IrpTaskReportLink.Lifetime));
    }

    [Fact]
    public async Task TestEveryExecutionGetsItsOwnKey()
    {
        var incident = SeedPlanWithOneTask();
        var plans = BuildPlans();

        await plans.CreateTaskExecutionAsync(NewExecution(), incident, Admin);
        await plans.CreateTaskExecutionAsync(NewExecution(), incident, Admin);

        var keys = _email.Parameters.Cast<TaskExecution>().Select(m => KeyOf(m.ReportLink)).ToList();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.Equal(2, ReportLinkCount());
    }

    [Fact]
    public void TestCreateLinkKeyReturnsA256BitTokenAndStoresOnlyItsDigest()
    {
        var links = GetService<ILinksService>();

        var first = links.CreateLinkKey("someType", DateTime.Now.AddDays(1), null);
        var second = links.CreateLinkKey("someType", DateTime.Now.AddDays(1), null);

        Assert.True(IrpTaskReportLink.IsWellFormedKey(first));
        Assert.NotEqual(first, second);
        Assert.True(links.LinkExists("someType", first));
        Assert.False(links.LinkExists("otherType", first));
        using var ctx = OpenContext();
        Assert.DoesNotContain(ctx.Links.ToList(), l => l.KeyHash == first || l.KeyHash == second);
    }

    [Fact]
    public async Task TestFastPushCarriesReportLinksAlongsidePasswordResets()
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7);
        GetService<ILinksService>().CreateLinkKey(SyncLinkTypes.PasswordReset, DateTime.Now.AddDays(1), null);

        var payload = await BuildPush().BuildFastAsync();

        var report = Assert.Single(payload.Links, l => l.Type == IrpTaskReportLink.Type);
        Assert.Equal(LinkKeyHash.Primary(KeyA), report.KeyHash);
        Assert.Equal(7, JsonSerializer.Deserialize<IrpTaskReportLinkData>(report.DataJson!)!.TaskExecutionId);
        Assert.Single(payload.Links, l => l.Type == SyncLinkTypes.PasswordReset);
    }

    // ---------------- Applying the website's report ----------------

    [Fact]
    public async Task TestReportWithTheBoundKeyIsAppliedAndConsumesTheLink()
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7);
        var action = ReportAction(7, (int)IntStatus.Completed, KeyA);

        var acked = await BuildIngest().ApplyAsync(new[] { action });

        Assert.Contains(action.ClientActionId, acked);
        Assert.Equal((int)IntStatus.Completed, StatusOf(7));
        Assert.Equal(0, ReportLinkCount());
    }

    [Fact]
    public async Task TestReportWithoutAKeyIsRejectedAndChangesNothing()
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7);
        var action = ReportAction(7, (int)IntStatus.Completed, "");

        var acked = await BuildIngest().ApplyAsync(new[] { action });

        // Acked so the website stops resending a report that can never become valid; nothing applied.
        Assert.Contains(action.ClientActionId, acked);
        Assert.Equal((int)IntStatus.New, StatusOf(7));
        Assert.Equal(1, ReportLinkCount());
        using var ctx = OpenContext();
        Assert.Single(ctx.ProcessedSyncActions.Where(p => p.ClientActionId == action.ClientActionId.ToString()));
    }

    [Fact]
    public async Task TestKeyForOneExecutionCannotChangeAnother()
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7);
        Seed(ctx => ctx.IncidentResponsePlanTaskExecutions.Add(new IncidentResponsePlanTaskExecution
        {
            Id = 8, PlanExecutionId = 1, TaskId = 1, Status = (int)IntStatus.New,
            CreatedAt = DateTime.Now, ExecutionDate = DateTime.Now, Duration = TimeSpan.Zero
        }));

        await BuildIngest().ApplyAsync(new[] { ReportAction(8, (int)IntStatus.Completed, KeyA) });

        Assert.Equal((int)IntStatus.New, StatusOf(8));
        Assert.Equal((int)IntStatus.New, StatusOf(7));
        Assert.Equal(1, ReportLinkCount()); // the legitimate holder can still report
    }

    [Fact]
    public async Task TestUnknownOrExpiredKeyIsRejected()
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7, expires: DateTime.Now.AddMinutes(-1));

        await BuildIngest().ApplyAsync(new[]
        {
            ReportAction(7, (int)IntStatus.Completed, KeyA),
            ReportAction(7, (int)IntStatus.Completed, KeyB)
        });

        Assert.Equal((int)IntStatus.New, StatusOf(7));
    }

    [Theory]
    [InlineData((int)IntStatus.New)]
    [InlineData((int)IntStatus.Cancelled)]
    [InlineData((int)IntStatus.Open)]
    public async Task TestOnlyTheStatusesThePageOffersAreAccepted(int status)
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7);

        await BuildIngest().ApplyAsync(new[] { ReportAction(7, status, KeyA) });

        Assert.Equal((int)IntStatus.New, StatusOf(7));
        Assert.Equal(1, ReportLinkCount());
    }

    [Fact]
    public async Task TestAKeyChangesItsExecutionOnlyOnce()
    {
        SeedExecutionAndLink(7, KeyA, boundTo: 7);
        var ingest = BuildIngest();

        await ingest.ApplyAsync(new[] { ReportAction(7, (int)IntStatus.Skipped, KeyA) });
        await ingest.ApplyAsync(new[] { ReportAction(7, (int)IntStatus.Failed, KeyA) });

        Assert.Equal((int)IntStatus.Skipped, StatusOf(7));
    }
}
