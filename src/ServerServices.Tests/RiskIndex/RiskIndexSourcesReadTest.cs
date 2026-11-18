using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Read;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M55, T263 (S40 §6.5, §8.2 <c>GET /RiskIndex/Sources</c> and <c>/Runs</c>, §11.3 "Sources e Runs não revelam totais fora do
/// escopo"): the connections and last sync of each source for every reader, the organization's object count and stale share
/// for global scope only — withheld, never zero, for anyone else — and the runs refused to a scoped reader.
/// </summary>
[TestSubject(typeof(RiskIndexReadService))]
public class RiskIndexSourcesReadTest : RiskIndexSeedTestBase
{
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);

    private IRiskIndexReadService Service => GetService<IRiskIndexReadService>();

    private static RiskIndexCaller Viewer() => new(2, false, new HashSet<string> { RiskIndexPermissions.View });

    public RiskIndexSourcesReadTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.RiskIndexProfiles.Add(new RiskIndexProfile
            {
                Id = 1, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
                Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), Justification = "pilot",
                SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
                EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
            });
            ctx.TrendMicroConnections.AddRange(
                new TrendMicroConnection
                {
                    Id = 1, Name = "Tenant A", Region = "us", BaseUrl = "https://a", Enabled = true, SyncRiskScores = true,
                    SyncVulnerabilities = true, LastSyncAt = Now.AddHours(-3), LastSyncStatus = IntegrationSyncStatus.Succeeded,
                    CreatedAt = Pilot
                },
                new TrendMicroConnection
                {
                    Id = 2, Name = "Tenant B", Region = "eu", BaseUrl = "https://b", Enabled = true, SyncRiskScores = false,
                    SyncVulnerabilities = true, LastSyncAt = Now.AddHours(-1), LastSyncStatus = IntegrationSyncStatus.Failed,
                    CreatedAt = Pilot
                },
                new TrendMicroConnection
                {
                    Id = 3, Name = "Disabled", Region = "us", BaseUrl = "https://c", Enabled = false, SyncRiskScores = true,
                    SyncVulnerabilities = true, LastSyncAt = Now, LastSyncStatus = IntegrationSyncStatus.Succeeded,
                    CreatedAt = Pilot
                });
            ctx.ScanImports.Add(new ScanImport
            {
                Id = 90, Importer = "Nessus", StartedAt = Now.AddDays(-1), FinishedAt = Now.AddDays(-1).AddMinutes(5),
                Status = (int)ScanImportStatus.Succeeded
            });
            ctx.RiskIndexPreviewRequests.Add(new RiskIndexPreviewRequest
            {
                Id = 5, ProfileId = 1, ParametersHash = "h", Status = RiskIndexPreviewStatus.Completed, CreatedAt = Now,
                StartedAt = Now.AddMinutes(1), FinishedAt = Now.AddMinutes(2), Report = "{\"secret\":true}"
            });
        });
        Assert.Equal(RiskIndexSnapshotResult.Written, GetService<IRiskIndexSnapshotService>().RunAsync(Now).GetAwaiter().GetResult().Result);
    }

    private async Task<IReadOnlyDictionary<string, RiskIndexSourceDto>> SourcesAsync() =>
        (await Service.GetSourcesAsync(Viewer())).ToDictionary(s => s.SourceId);

    // --- sources -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestEveryCatalogueSourceIsListedWithItsConnectionsAndLastSync()
    {
        var sources = await SourcesAsync();

        Assert.Equal(RiskIndexSources.All.Order(), sources.Keys.Order());
        // V1-DEV: only tenant A syncs risk scores; V1-CVE: both enabled tenants, the latest sync (B) failed.
        Assert.Equal((1, Now.AddHours(-3), RiskIndexSourceStatusKeys.Ok, RiskIndexSourceStatusKeys.KindSync),
            (sources[RiskIndexSources.V1Device].Connections, sources[RiskIndexSources.V1Device].LastSyncUtc,
                sources[RiskIndexSources.V1Device].LastSyncStatusKey, sources[RiskIndexSources.V1Device].LastSyncKindKey));
        Assert.Equal((2, Now.AddHours(-1), RiskIndexSourceStatusKeys.Failed),
            (sources[RiskIndexSources.V1Cve].Connections, sources[RiskIndexSources.V1Cve].LastSyncUtc,
                sources[RiskIndexSources.V1Cve].LastSyncStatusKey));
        // The seed's one SecurityScorecard connection has never synced.
        Assert.Equal((1, null, null), (sources[RiskIndexSources.SscTotal].Connections,
            sources[RiskIndexSources.SscTotal].LastSyncUtc, sources[RiskIndexSources.SscTotal].LastSyncStatusKey));
        // Nessus files are their own source, compared case-insensitively; the other importers' latest is the seed's sarif.
        Assert.Equal((Now.AddDays(-1).AddMinutes(5), RiskIndexSourceStatusKeys.KindImport),
            (sources[RiskIndexSources.TenableNessus].LastSyncUtc, sources[RiskIndexSources.TenableNessus].LastSyncKindKey));
        Assert.Equal(Now.AddDays(-1), sources[RiskIndexSources.Importers].LastSyncUtc);
        Assert.Null(sources[RiskIndexSources.Incidents].Connections);
    }

    [Fact]
    public async Task TestGlobalScopeReadsTheOrganizationsCountsOfTheLatestSnapshot()
    {
        ScopeToEverything();

        var sources = await SourcesAsync();

        var cve = sources[RiskIndexSources.V1Cve];
        Assert.False(cve.CountsWithheld);
        Assert.NotNull(cve.ObjectCount);
        Assert.InRange(cve.StaleFraction!.Value, 0, 1);
        Assert.True(sources.Values.Where(s => s.ObjectCount is not null).Sum(s => s.ObjectCount) > 0);
        // A source with no per-object bit (an entity property, a register weight) has no count, and nothing is withheld.
        Assert.Equal((null, null, false), (sources[RiskIndexSources.RegImpact].ObjectCount,
            sources[RiskIndexSources.RegImpact].StaleFraction, sources[RiskIndexSources.RegImpact].CountsWithheld));
    }

    [Fact]
    public async Task TestAScopedReaderGetsTheCountsWithheldNotZero()
    {
        ScopeTo(UnitX);

        var sources = await SourcesAsync();

        Assert.All(sources.Values, s => Assert.Null(s.ObjectCount));
        Assert.All(sources.Values, s => Assert.Null(s.StaleFraction));
        Assert.True(sources[RiskIndexSources.V1Cve].CountsWithheld);
        Assert.False(sources[RiskIndexSources.RegImpact].CountsWithheld);
        // The configuration columns stay: they reveal no inventory.
        Assert.Equal(2, sources[RiskIndexSources.V1Cve].Connections);
    }

    [Fact]
    public void TestAnObjectIsStaleForASourceOnlyWhenNoneOfItsReadingsFromItIsCurrent()
    {
        static string Factors(params (string Source, double V)[] readings) => RiskIndexStoredJson.Serialize(readings
            .Select(r => new StoredFactor("f", RiskIndexFactorType.V, 1, r.V, 0, Now, r.Source)).ToList());

        var cve = RiskIndexSourceBits.Bit(RiskIndexSources.V1Cve);
        var device = RiskIndexSourceBits.Bit(RiskIndexSources.V1Device);
        var counts = RiskIndexReadService.CountSources(
        [
            // Host 1: stale on its EXP row, fresh on another category row — fresh.
            new(RiskIndexObjectKind.Host, "1", cve, Factors((RiskIndexSources.V1Cve, 0.5))),
            new(RiskIndexObjectKind.Host, "1", cve, Factors((RiskIndexSources.V1Cve, 1))),
            // Host 2: every V1-CVE reading stale; its device reading fresh.
            new(RiskIndexObjectKind.Host, "2", cve | device,
                Factors((RiskIndexSources.V1Cve, 0.2), (RiskIndexSources.V1Device, 1))),
            // Host 3: carries the bit but no reading from the source — counted, never stale.
            new(RiskIndexObjectKind.Host, "3", cve, null)
        ]);

        Assert.Equal(3, counts.Objects[RiskIndexSources.V1Cve]);
        Assert.Equal(1, counts.Stale[RiskIndexSources.V1Cve]);
        Assert.Equal(1, counts.Objects[RiskIndexSources.V1Device]);
        Assert.False(counts.Stale.ContainsKey(RiskIndexSources.V1Device));
    }

    // --- runs ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheSnapshotRunsAreListedNewestFirstWithoutTheirTrail()
    {
        ScopeToEverything();

        var runs = await Service.GetRunsAsync(RiskIndexRunKind.Snapshot, 10, Viewer());

        var run = Assert.Single(runs);
        Assert.Equal(IntegrationKind.RiskIndex, run.Integration);
        Assert.Equal(IntegrationSyncStatus.Succeeded, run.Status);
        Assert.Null(run.ProgressLog);
    }

    [Fact]
    public async Task TestThePreviewRunsCarryNoReportAndNeedGlobalScope()
    {
        ScopeToEverything();
        var preview = Assert.Single(await Service.GetRunsAsync(RiskIndexRunKind.Preview, 10, Viewer()));
        Assert.Equal((5, 1, "Preview of profile v1", IntegrationSyncStatus.Succeeded, Now.AddMinutes(1)),
            (preview.Id, preview.ConnectionId, preview.ConnectionName, preview.Status, preview.StartedAt));
        Assert.Null(preview.Summary);
        Assert.Null(preview.ProgressLog);

        ScopeTo(UnitX);
        var refused = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetRunsAsync(RiskIndexRunKind.Preview, 10, Viewer()));
        Assert.Equal((403, RiskIndexErrors.RunsRequireGlobalScope), (refused.Status, refused.Code));
    }

    [Theory]
    [InlineData(RiskIndexRunKind.Retention, 10, RiskIndexErrors.RunKindNotRecorded)]
    [InlineData((RiskIndexRunKind)9, 10, RiskIndexErrors.InvalidRunKind)]
    [InlineData(RiskIndexRunKind.Snapshot, 0, RiskIndexErrors.InvalidLimit)]
    [InlineData(RiskIndexRunKind.Snapshot, RiskIndexReadService.MaxRuns + 1, RiskIndexErrors.InvalidLimit)]
    public async Task TestAnInvalidRunsRequestIsRefused(RiskIndexRunKind kind, int limit, string code)
    {
        ScopeToEverything();

        var refused = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetRunsAsync(kind, limit, Viewer()));

        Assert.Equal((400, code), (refused.Status, refused.Code));
    }

    [Theory]
    [InlineData(IntegrationSyncStatus.Succeeded, RiskIndexSourceStatusKeys.Ok)]
    [InlineData(IntegrationSyncStatus.PartiallySucceeded, RiskIndexSourceStatusKeys.Partial)]
    [InlineData(IntegrationSyncStatus.Failed, RiskIndexSourceStatusKeys.Failed)]
    [InlineData(IntegrationSyncStatus.Running, RiskIndexSourceStatusKeys.Running)]
    public void TestASyncStatusReadsAsItsKey(IntegrationSyncStatus status, string key) =>
        Assert.Equal(key, RiskIndexReadService.SyncStatusKey(status));

    [Theory]
    [InlineData(ScanImportStatus.Succeeded, RiskIndexSourceStatusKeys.Ok)]
    [InlineData(ScanImportStatus.Failed, RiskIndexSourceStatusKeys.Failed)]
    [InlineData(ScanImportStatus.Queued, RiskIndexSourceStatusKeys.Queued)]
    [InlineData(ScanImportStatus.Cancelled, RiskIndexSourceStatusKeys.Cancelled)]
    [InlineData(ScanImportStatus.Running, RiskIndexSourceStatusKeys.Running)]
    public void TestAnImportStatusReadsAsItsKey(ScanImportStatus status, string key) =>
        Assert.Equal(key, RiskIndexReadService.ImportStatusKey((int)status));
}
