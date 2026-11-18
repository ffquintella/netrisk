using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Integrations;
using Model.Secrets;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ServerServices.Integrations;
using ServerServices.Integrations.Tenable;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4.Tenable;

/// <summary>
/// M56, T265–T266 (S40 §8.7, §9.4, §11.6, §12.2 <c>TenableServiceTest</c>): the connection keeps its keys write-only and
/// vault-capable, a manual sync is only queued and the job host claims and runs it in place, findings flow through the
/// ingestion pipeline with an incremental cursor that never advances on failure, FIXED closes through the lifecycle — and
/// with 401, 403, 429 and a transport error no log event, sync-log row or connection column carries a key.
/// </summary>
[TestSubject(typeof(TenableService))]
public class TenableServiceTest : InMemoryServiceTestBase
{
    private const int Law = 8;
    private static readonly DateTime Now = new(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc);

    private readonly CapturingSink _sink = new();
    private readonly ITenableService _svc;

    public TenableServiceTest()
    {
        Seed(ctx => ctx.Entities.Add(new Entity { Id = Law, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" }));
        _svc = Build(GetService<ISecretResolver>());
    }

    /// <summary>The service with a logger whose every event is captured, and a client that never sleeps.</summary>
    private TenableService Build(ISecretResolver resolver)
    {
        var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
        var dal = GetService<IDalService>();
        var client = new TenableClient(logger, FakeOutboundHttpClient) { DelayAsync = (_, _) => Task.CompletedTask };
        var tracker = new IntegrationSyncTracker(logger, dal, GetService<IIntegrationSyncNotifier>());
        return new TenableService(logger, dal, GetService<ISecretProtector>(), resolver, client,
            GetService<IFindingIngestionService>(), GetService<IFindingLifecycleService>(), tracker,
            GetService<IExploitationSignalsSyncService>());
    }

    private static TenableConnectionUpsert Upsert(string name = "Tenable FGV", string? access = TenableFixtures.AccessKey,
        string? secret = TenableFixtures.SecretKey, bool findings = true, bool assets = true, string? baseUrl = null,
        TenableProduct product = TenableProduct.VulnerabilityManagement, bool enabled = true, int interval = 24,
        string? accessRef = null, string? secretRef = null, bool clearAccess = false, bool clearSecret = false) =>
        new(name, product, baseUrl, Law, enabled, interval, findings, assets, false, true, 1, 90, false, false,
            access, secret, accessRef, secretRef, clearAccess, clearSecret);

    /// <summary>Answers the asset and vulnerability exports with one chunk each.</summary>
    private void StubExports(string assetChunk, string vulnChunk)
    {
        FakeOutboundHttpClient.Reset();
        FakeOutboundHttpClient.RuleFor("/assets/v2/export", TenableFixtures.Started("assets-1"));
        FakeOutboundHttpClient.RuleFor("/assets/export/assets-1/status", TenableFixtures.Status("FINISHED", 1));
        FakeOutboundHttpClient.RuleFor("/assets/export/assets-1/chunks/1", assetChunk);
        FakeOutboundHttpClient.RuleFor("/vulns/export/vulns-1/status", TenableFixtures.Status("FINISHED", 1));
        FakeOutboundHttpClient.RuleFor("/vulns/export/vulns-1/chunks/1", vulnChunk);
        FakeOutboundHttpClient.RuleFor("/vulns/export", TenableFixtures.Started("vulns-1"));
    }

    private async Task<TenableSyncResult> QueueAndRunAsync(int connectionId)
    {
        await _svc.QueueSyncAsync(connectionId);
        return await _svc.RunPendingAsync(DateTime.UtcNow);
    }

    // --- T265: the connection --------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAViewNeverCarriesAKeyAndTheColumnsHoldCiphertext()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());

        Assert.Equal((true, true, (string?)null, (string?)null), (view.HasAccessKey, view.HasSecretKey,
            view.AccessKeyVaultReference, view.SecretKeyVaultReference));
        Assert.Equal(TenableClient.VulnerabilityManagementUrl, view.BaseUrl);
        Assert.False(TenableFixtures.Leaks(JsonSerializer.Serialize(view)));

        await using var db = OpenContext();
        var row = db.TenableConnections.Single();
        Assert.False(TenableFixtures.Leaks(row.EncryptedAccessKey + row.EncryptedSecretKey));
        Assert.Equal(TenableFixtures.AccessKey, GetService<ISecretProtector>().Unprotect(row.EncryptedAccessKey));
    }

    [Fact]
    public async Task TestAVaultReferenceIsStoredAsGivenAndShownAsAReference()
    {
        var access = SecretReference.Create(3, "tenable/netrisk", "access").ToString();
        var secret = SecretReference.Create(3, "tenable/netrisk", "secret").ToString();

        var view = await _svc.CreateConnectionAsync(Upsert(access: null, secret: null, accessRef: access, secretRef: secret));

        Assert.Equal((access, secret), (view.AccessKeyVaultReference, view.SecretKeyVaultReference));
        await using var db = OpenContext();
        Assert.Equal((access, secret), (db.TenableConnections.Single().EncryptedAccessKey, db.TenableConnections.Single().EncryptedSecretKey));
    }

    [Fact]
    public async Task TestAnOmittedKeyIsKeptAndAClearedOneIsErased()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        string? Stored(Func<TenableConnection, string?> column)
        {
            using var db = OpenContext();
            return column(db.TenableConnections.Single());
        }

        var before = Stored(c => c.EncryptedAccessKey);
        var kept = await _svc.UpdateConnectionAsync(view.Id, Upsert(name: "Renamed", access: null, secret: null));
        Assert.Equal(("Renamed", true, true), (kept.Name, kept.HasAccessKey, kept.HasSecretKey));
        Assert.Equal(before, Stored(c => c.EncryptedAccessKey));

        var cleared = await _svc.UpdateConnectionAsync(view.Id, Upsert(access: null, secret: null, clearSecret: true));
        Assert.Equal((true, false), (cleared.HasAccessKey, cleared.HasSecretKey));
        Assert.Null(Stored(c => c.EncryptedSecretKey));
    }

    [Theory]
    [InlineData("https://evil.example.com", TenableProduct.VulnerabilityManagement, TenableErrors.BaseUrlInvalid)]
    [InlineData(null, TenableProduct.SecurityCenter, TenableErrors.ProductNotSupported)]
    public async Task TestOnlyVulnerabilityManagementAtItsFixedUrlIsAccepted(string? url, TenableProduct product, string code)
    {
        var refused = await Assert.ThrowsAsync<TenableException>(() =>
            _svc.CreateConnectionAsync(Upsert(baseUrl: url, product: product)));

        Assert.Equal((400, code), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task TestTheGuardsOfAWriteAreRefusedWithTheirCodes()
    {
        async Task<string> Code(TenableConnectionUpsert body) =>
            (await Assert.ThrowsAsync<TenableException>(() => _svc.CreateConnectionAsync(body))).Code;

        Assert.Equal(TenableErrors.CredentialsRequired, await Code(Upsert(secret: null)));
        Assert.Equal(TenableErrors.VaultReferenceInvalid, await Code(Upsert(accessRef: "vault:nonsense")));
        Assert.Equal(TenableErrors.InvalidParameter, await Code(Upsert(interval: 169)));
        Assert.Equal(TenableErrors.InvalidParameter, await Code(Upsert(name: " ")));

        await _svc.CreateConnectionAsync(Upsert());
        Assert.Equal(TenableErrors.NameTaken, await Code(Upsert()));
        Assert.Equal(404, (await Assert.ThrowsAsync<TenableException>(() => _svc.GetConnectionAsync(999))).Status);
    }

    [Fact]
    public async Task TestTheAuditRecordsTheConnectionButNeverItsKeys()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        await _svc.UpdateConnectionAsync(view.Id, Upsert(access: "AK-rotated-0000000000000000", secret: null) with { EntityId = null });

        await using var db = OpenContext();
        var rows = db.AuditLogs.Where(a => a.EntityType == nameof(TenableConnection)).ToList();
        Assert.Contains(rows, a => a.Field == nameof(TenableConnection.EntityId) && a.OldValue == Law.ToString());
        Assert.DoesNotContain(rows, a => a.Field != null && a.Field.StartsWith("Encrypted"));
        Assert.False(rows.Any(a => TenableFixtures.Leaks(a.OldValue) || TenableFixtures.Leaks(a.NewValue) ||
                                   (a.NewValue ?? "").Contains("AK-rotated")));
    }

    [Fact]
    public async Task TestDeletingAConnectionTakesItsRulesAndLeavesItsHosts()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        Seed(ctx =>
        {
            ctx.Hosts.Add(new Host { Id = 1, HostName = "web-01", Source = TenableService.ProviderName, RegistrationDate = Now, Status = 1 });
            ctx.VendorEntityMappings.Add(new VendorEntityMapping
            {
                Provider = IntegrationKind.Tenable, ConnectionId = view.Id, MatchKind = VendorMatchKind.TenableTag,
                MatchValue = "BU: Law", EntityId = Law, Priority = 1, RestampPolicy = RestampPolicy.OnlyUnassigned, Enabled = true,
                CreatedAt = Now
            });
        });

        await _svc.DeleteConnectionAsync(view.Id);

        await using var db = OpenContext();
        Assert.Empty(db.TenableConnections);
        Assert.Empty(db.VendorEntityMappings);
        Assert.Single(db.Hosts);
    }

    [Fact]
    public async Task TestTheProbeResolvesTheKeysAndReportsItsSteps()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        FakeOutboundHttpClient.EnqueueJson("{}").EnqueueJson("""{"exports":[]}""").EnqueueJson("""{"exports":[]}""");

        var result = await _svc.TestConnectionAsync(view.Id);

        Assert.True(result.Success);
        Assert.Equal($"accessKey={TenableFixtures.AccessKey}; secretKey={TenableFixtures.SecretKey};",
            FakeOutboundHttpClient.Requests[0].Headers["X-ApiKeys"]);
    }

    [Fact]
    public async Task TestEveryCallReadsTheKeysThroughTheSecretResolver()
    {
        var access = SecretReference.Create(3, "tenable", "access").ToString();
        var secret = SecretReference.Create(3, "tenable", "secret").ToString();
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(call => (string?)call[0] == access ? "resolved-access" : "resolved-secret");
        var svc = Build(resolver);
        var view = await svc.CreateConnectionAsync(Upsert(access: null, secret: null, accessRef: access, secretRef: secret));

        await svc.TestConnectionAsync(view.Id);

        await resolver.Received().ResolveAsync(access, Arg.Any<System.Threading.CancellationToken>());
        await resolver.Received().ResolveAsync(secret, Arg.Any<System.Threading.CancellationToken>());
        Assert.Equal("accessKey=resolved-access; secretKey=resolved-secret;", FakeOutboundHttpClient.Requests[0].Headers["X-ApiKeys"]);
    }

    // --- T265: the queued manual sync and the job host ---------------------------------------------------------------------

    [Fact]
    public async Task TestAManualSyncIsOnlyQueuedAndASecondIsRefused()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());

        var queued = await _svc.QueueSyncAsync(view.Id);

        await using (var db = OpenContext())
        {
            var row = db.IntegrationSyncLogs.Single();
            Assert.Equal((queued.LogId, IntegrationSyncStatus.Queued, IntegrationKind.Tenable), (row.Id, row.Status, row.Integration));
        }

        Assert.Empty(FakeOutboundHttpClient.Requests);
        Assert.Equal(TenableErrors.SyncRunning,
            (await Assert.ThrowsAsync<TenableException>(() => _svc.QueueSyncAsync(view.Id))).Code);
    }

    [Fact]
    public async Task TestADisabledConnectionCannotBeQueued()
    {
        var view = await _svc.CreateConnectionAsync(Upsert(enabled: false));

        var refused = await Assert.ThrowsAsync<TenableException>(() => _svc.QueueSyncAsync(view.Id));

        Assert.Equal((409, TenableErrors.ConnectionDisabled), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task TestTheJobRunsTheQueuedRequestInItsOwnRowAndThenIsIdle()
    {
        var view = await _svc.CreateConnectionAsync(Upsert(assets: false));
        StubExports("[]", TenableFixtures.Chunk(TenableFixtures.Vuln("f-1")));
        var queued = await _svc.QueueSyncAsync(view.Id);

        var result = await _svc.RunPendingAsync(DateTime.UtcNow);

        Assert.Equal((1, 1), (result.Runs, result.FindingsCreated));
        await using var db = OpenContext();
        var row = Assert.Single(db.IntegrationSyncLogs.ToList());
        Assert.Equal((queued.LogId, IntegrationSyncStatus.Succeeded), (row.Id, row.Status));
        Assert.NotNull(row.HeartbeatAt);
        Assert.Contains("requested by hand", row.ProgressLog);

        // The connection was anchored at this tick and has just run: the next tick inside the interval is idle.
        Assert.Equal(0, (await _svc.RunPendingAsync(DateTime.UtcNow.AddMinutes(15))).Runs);
    }

    [Fact]
    public async Task TestAQueuedRequestThatLostItsClaimIsNotRunTwice()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        var queued = await _svc.QueueSyncAsync(view.Id);
        var svc = (TenableService)_svc;

        Assert.NotNull(await svc.ClaimQueuedAsync(queued.LogId, Now, default));
        Assert.Null(await svc.ClaimQueuedAsync(queued.LogId, Now, default));
    }

    [Theory]
    [InlineData("2026-11-18T02:10:00Z", "2026-11-19T02:00:00Z")]
    [InlineData("2026-11-21T05:00:00Z", "2026-11-22T02:00:00Z")]
    [InlineData("2026-11-18T02:00:00Z", "2026-11-19T02:00:00Z")]
    public void TestTheCadenceIsAnchoredOnItsOwnDueTime(string now, string next) =>
        Assert.Equal(DateTime.Parse(next).ToUniversalTime(),
            TenableService.NextDue(Now, 24, DateTime.Parse(now).ToUniversalTime()));

    // --- T266: findings ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestFindingsFlowIntoThePipelineAndTheCursorAdvancesToTheRequest()
    {
        var view = await _svc.CreateConnectionAsync(Upsert(assets: false));
        StubExports("[]", TenableFixtures.Chunk(TenableFixtures.Vuln("f-1"), TenableFixtures.Vuln("f-2", cves: "\"CVE-2024-0002\"")));
        var before = DateTime.UtcNow;

        var result = await QueueAndRunAsync(view.Id);

        Assert.Equal((2, 2, 0), (result.FindingsReceived, result.FindingsCreated, result.Errors));
        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single(v => v.ToolUniqueId == "f-1");
        Assert.Equal(("tenable-vm", "19506", (float?)8.9f, (int?)Law),
            (finding.ImportSource, finding.RuleId, finding.VprScore, finding.EntityId));
        Assert.Equal("FUNCTIONAL", finding.ExploitCodeMaturity);
        Assert.Equal(EpssSource.Tenable, db.EpssScores.Single(e => e.CveId == "CVE-2024-0001").Source);
        Assert.Equal(0.42, db.Vulnerabilities.Single(v => v.ToolUniqueId == "f-1").EpssScore);
        Assert.InRange(db.TenableConnections.Single().FindingsCursor!.Value, before.AddSeconds(-1), DateTime.UtcNow);

        var start = FakeOutboundHttpClient.Requests.First(r => r.Method == "POST");
        var since = new DateTimeOffset(before.AddDays(-90)).ToUnixTimeSeconds();
        Assert.InRange(JsonDocument.Parse(start.Body!).RootElement.GetProperty("filters").GetProperty("since").GetInt64(),
            since - 5, since + 5);
    }

    [Fact]
    public async Task TestTheCursorDoesNotAdvanceWhenTheStepFails()
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        StubExports(TenableFixtures.Chunk(TenableFixtures.Asset("a-1")), "[]");
        FakeOutboundHttpClient.Rules.Insert(0, (r => r.Url.EndsWith("/vulns/export"),
            new OutboundHttpResponse { StatusCode = 403, Body = """{"error":"Forbidden"}""" }));

        var result = await QueueAndRunAsync(view.Id);

        Assert.Equal(1, result.Errors);
        await using var db = OpenContext();
        var connection = db.TenableConnections.Single();
        Assert.Null(connection.FindingsCursor);
        Assert.NotNull(connection.AssetsCursor);
        Assert.Equal(IntegrationSyncStatus.Failed, connection.LastSyncStatus);
        Assert.Contains("403", db.IntegrationSyncLogs.Single().ErrorMessage);
    }

    [Fact]
    public async Task TestAFixedRecordClosesTheFindingThroughTheLifecycle()
    {
        var view = await _svc.CreateConnectionAsync(Upsert(assets: false));
        StubExports("[]", TenableFixtures.Chunk(TenableFixtures.Vuln("f-1")));
        await QueueAndRunAsync(view.Id);

        StubExports("[]", TenableFixtures.Chunk(TenableFixtures.Vuln("f-1", state: "FIXED"), TenableFixtures.Vuln("never-seen", state: "FIXED")));
        var result = await QueueAndRunAsync(view.Id);

        Assert.Equal((1, 0), (result.FindingsFixed, result.FindingsCreated));
        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single();
        Assert.Equal(FindingStatus.Mitigated, finding.LifecycleStatus);
        Assert.Contains(db.FindingStatusHistories, h => h.VulnerabilityId == finding.Id && h.ToStatus == FindingStatus.Mitigated &&
                                                        h.Source == FindingStatusChangeSource.Job);
    }

    // --- S40 §11.6: non-disclosure ---------------------------------------------------------------------------------------

    public static TheoryData<int> Failures() => new() { 401, 403, 429, 0 };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task TestNoKeyReachesALogEventASyncLogRowOrTheConnection(int status)
    {
        var view = await _svc.CreateConnectionAsync(Upsert());
        // The remote echoes the header back, the worst case a careless error path would copy.
        var echo = $$"""{"error":"bad header accessKey={{TenableFixtures.AccessKey}}; secretKey={{TenableFixtures.SecretKey}};"}""";
        FakeOutboundHttpClient.DefaultResponse = status == 0
            ? new OutboundHttpResponse { StatusCode = 0, TransportError = $"refused X-ApiKeys: accessKey={TenableFixtures.AccessKey}" }
            : new OutboundHttpResponse { StatusCode = status, Body = echo };

        var result = await QueueAndRunAsync(view.Id);
        var test = await _svc.TestConnectionAsync(view.Id);

        Assert.True(result.Errors > 0);
        Assert.False(test.Success);
        Assert.False(test.Steps.Any(s => TenableFixtures.Leaks(s.Detail)), "probe detail");
        Assert.False(_sink.Events.Any(TenableFixtures.Leaks), "log event");
        Assert.False(result.Messages.Any(TenableFixtures.Leaks), "result message");

        await using var db = OpenContext();
        var log = db.IntegrationSyncLogs.Single();
        Assert.False(TenableFixtures.Leaks(log.Summary) || TenableFixtures.Leaks(log.ErrorMessage) ||
                     TenableFixtures.Leaks(log.ProgressLog), "sync log");
        var connection = db.TenableConnections.Single();
        Assert.False(TenableFixtures.Leaks(connection.LastSyncError) || TenableFixtures.Leaks(connection.Name), "connection");
        Assert.Contains("[redacted]", log.ErrorMessage ?? log.ProgressLog);
    }

    /// <summary>Every event as rendered, with its properties and exception, so a key anywhere in it is found.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        public List<string> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
                Events.Add(logEvent.RenderMessage() + " " + string.Join(" ", logEvent.Properties.Values) + " " + logEvent.Exception);
        }
    }
}
