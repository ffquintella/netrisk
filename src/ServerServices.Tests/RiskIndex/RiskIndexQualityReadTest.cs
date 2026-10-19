using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Read;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M54, T256 (S40 §5.12, §8.2 <c>GET /RiskIndex/Quality</c>; S39 §8.5–§8.6): the data quality beside the index — the
/// Unassigned and default-criticality fractions by weight, stale sources and counts over the visible objects only, the
/// organization's tree counts at the root alone, unmeasured counts as null, and the <c>chip=</c> lists the items open.
/// </summary>
[TestSubject(typeof(RiskIndexReadService))]
public class RiskIndexQualityReadTest : RiskIndexSeedTestBase
{
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);

    private IRiskIndexReadService Service => GetService<IRiskIndexReadService>();

    public RiskIndexQualityReadTest()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(new RiskIndexProfile
        {
            Id = 1, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
            Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), Justification = "pilot",
            SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
            EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
        }));
        Assert.Equal(RiskIndexSnapshotResult.Written, GetService<IRiskIndexSnapshotService>().RunAsync(Now).GetAwaiter().GetResult().Result);
    }

    private static RiskIndexCaller Committee() =>
        new(1, false, new HashSet<string>(new[] { RiskIndexPermissions.View, RiskIndexPermissions.Approve }
            .Concat(RiskIndexModulePermissions.All)));

    private static RiskIndexCaller Viewer() => new(2, false, new HashSet<string> { RiskIndexPermissions.View });

    private static string Key(int entity) => RiskIndexNodeKey.ForEntity(entity).ToString();

    // --- the read ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheOrganizationsQualityMatchesTheOverviewAndCarriesTheTreeCounts()
    {
        var overview = await Service.GetOverviewAsync("org", null, Committee());

        var quality = await Service.GetQualityAsync("org", null, Committee());

        Assert.Equal((overview.Quality.Coverage, overview.Quality.Freshness, overview.Quality.Quality, overview.Quality.QualityBandKey),
            (quality.Coverage, quality.Freshness, quality.Quality, quality.QualityBandKey));
        // The host-less finding imported with no entity is the Unassigned group: a share of the assets' weight.
        Assert.InRange(quality.UnassignedWeightFraction, 1e-9, 1);
        Assert.Equal(quality.UnassignedWeightFraction > new RiskIndexParameters().UnassignedThreshold, quality.UnassignedWarning);
        Assert.InRange(quality.DefaultCriticalityWeightFraction, 0, 1);
        Assert.NotNull(quality.DivergentLinks);
        Assert.NotNull(quality.OrphanApplications);
        Assert.NotNull(quality.FindingStatusDisagreements);
        Assert.Null(quality.SscPseudoHosts);
        Assert.Null(quality.ExitedWithBadReading);
        Assert.Equal(RiskIndexQualityFilters.EvidenceConfidenceRequiresM40, quality.EvidenceConfidenceStateKey);
    }

    [Fact]
    public async Task TestAnEntityNodeHasNoUnassignedShareAndNoTreeCounts()
    {
        var quality = await Service.GetQualityAsync(Key(UnitX), null, Committee());

        Assert.Equal(0, quality.UnassignedWeightFraction);
        Assert.False(quality.UnassignedWarning);
        Assert.Null(quality.DivergentLinks);
        Assert.Null(quality.OrphanApplications);
        Assert.Null(quality.FindingStatusDisagreements);
    }

    /// <summary>The counts equal what the object list's own <c>chip=</c> filters list, under the same scope (S40 §5.12.1).</summary>
    [Fact]
    public async Task TestTheCountsAreTheListsTheItemsOpen()
    {
        var quality = await Service.GetQualityAsync("org", null, Committee());
        var presumed = await Service.GetObjectsAsync("org", null,
            new RiskIndexObjectQuery(RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.PresumedCritical), PageSize: 500), Committee());
        var residual = await Service.GetObjectsAsync("org", null,
            new RiskIndexObjectQuery(RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.ResidualAboveInherent), PageSize: 500),
            Committee());
        var defaults = await Service.GetObjectsAsync("org", null,
            new RiskIndexObjectQuery(RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.DefaultCriticality), PageSize: 500),
            Committee());

        Assert.Equal(quality.PresumedCritical, presumed.Rows.Select(r => r.Object.Object).Distinct().Count());
        Assert.All(presumed.Rows, r => Assert.True(r.State == RiskIndexObjectState.Presumed && r.Criticality >= 4));
        Assert.Equal(quality.ResidualAboveInherent, residual.Total);
        Assert.All(defaults.Rows, r => Assert.Equal("default", r.CriticalitySource));
        // The Unassigned group has no entity to take a criticality from: the default, so neither side is vacuous.
        Assert.True(defaults.Total > 0);
        Assert.InRange(quality.DefaultCriticalityWeightFraction, 1e-9, 1);
    }

    /// <summary>The strip counts a presumed critical object once; the list its "›" opens lists it once, so the two agree.</summary>
    [Fact]
    public async Task TestThePresumedCriticalListTotalIsTheStripsCount()
    {
        var presumedRows = 0;
        SeedUnscoped(ctx =>
        {
            foreach (var row in ctx.RiskIndexObjectDays.Where(o => o.ObjectKind == RiskIndexObjectKind.Host && o.ObjectRef == Db.ToString()))
            {
                row.State = RiskIndexObjectState.Presumed;
                row.Criticality = 5;
                presumedRows++;
            }
        });

        var quality = await Service.GetQualityAsync("org", null, Committee());
        var listed = await Service.GetObjectsAsync("org", null,
            new RiskIndexObjectQuery(RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.PresumedCritical), PageSize: 500), Committee());

        Assert.True(presumedRows >= 2, "DB-02 has rows in more than one category");
        Assert.True(quality.PresumedCritical > 0);
        Assert.Equal(quality.PresumedCritical, listed.Total);
        Assert.Equal(listed.Total, listed.Rows.Select(r => r.Object.Object).Distinct().Count());
    }

    [Fact]
    public async Task TestAnUnknownChipIsAnInvalidFilter()
    {
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery("chip=everything"), Committee()));

        Assert.Equal((400, RiskIndexErrors.InvalidFilter), (refusal.Status, refusal.Code));
    }

    /// <summary>A partial node counts only the visible objects, and the scope's cache entry is its own (S40 §5.12.1, §11.3).</summary>
    [Fact]
    public async Task TestAPartialNodeCountsOnlyTheVisible()
    {
        var full = await Service.GetQualityAsync(Key(UnitX), null, Committee());
        ScopeTo(UnitX, Lab);
        var partial = await Service.GetQualityAsync(Key(UnitX), null, Committee());
        var visible = await Service.GetObjectsAsync(Key(UnitX), null, new RiskIndexObjectQuery(PageSize: 500), Committee());
        ScopeToEverything();
        var again = await Service.GetQualityAsync(Key(UnitX), null, Committee());

        Assert.Equal(visible.Rows.Where(r => r.State == RiskIndexObjectState.Presumed && r.Criticality >= 4)
            .Select(r => r.Object.Object).Distinct().Count(), partial.PresumedCritical);
        Assert.DoesNotContain(visible.Rows, r => r.Object.Object.Kind == RiskIndexObjectKind.Application);
        Assert.Equal(full, again);
    }

    [Fact]
    public async Task TestTheQualityRefusesLikeEveryRead()
    {
        var shadow = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetQualityAsync("org", null, Viewer()));
        var scopeRoot = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetQualityAsync("scope", null, Committee()));
        var malformed = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetQualityAsync("e:", null, Committee()));
        ScopeTo(UnitX);
        var organization = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetQualityAsync("org", null, Committee()));

        Assert.Equal((403, RiskIndexErrors.InShadow), (shadow.Status, shadow.Code));
        Assert.Equal((400, RiskIndexErrors.InvalidNode), (scopeRoot.Status, scopeRoot.Code));
        Assert.Equal((400, RiskIndexErrors.InvalidNode), (malformed.Status, malformed.Code));
        Assert.Equal((403, RiskIndexErrors.OrgRequiresGlobalScope), (organization.Status, organization.Code));
    }

    // --- the pure half -----------------------------------------------------------------------------------------------

    private static readonly EntityMap Map = EntityMap.Build(
        [new EntityMapRow(Org, "organization", null), new EntityMapRow(UnitX, "organizationUnit", Org)], []);

    private static RiskIndexReadService.NodeValues Values(params StoredChip[] chips) =>
        new(60, 60, 60, RiskIndexDisplayState.Point, 0.8, 0.9, 0.72, null, null, 4, 0, false, Now, [], chips, [], [], null);

    private static RiskIndexObjectRow Row(RiskIndexObjectKind kind, string reference, RiskIndexCategory category, double weight,
        string source = "manual", int criticality = 3, RiskIndexObjectState state = RiskIndexObjectState.Assessed,
        int[]? attribution = null, bool residualAboveInherent = false) => new()
    {
        Key = new IcrObjectKey(kind, reference), Category = category, Population = RiskIndexPopulation.Assets, Weight = weight,
        CriticalitySource = source, Criticality = criticality, State = state, Attribution = attribution ?? [UnitX],
        ScenarioInputs = category == RiskIndexCategory.Reg ? new RiskIndexScenarioInputs { ResidualAboveInherent = residualAboveInherent } : null
    };

    [Fact]
    public void TestTheFractionsAreByWeightAndTheCountsByObject()
    {
        IReadOnlyList<RiskIndexObjectRow> rows =
        [
            Row(RiskIndexObjectKind.Host, "1", RiskIndexCategory.Exp, 3, criticality: 5, state: RiskIndexObjectState.Presumed),
            Row(RiskIndexObjectKind.Host, "1", RiskIndexCategory.Gov, 3, criticality: 5, state: RiskIndexObjectState.Presumed),
            Row(RiskIndexObjectKind.Host, "2", RiskIndexCategory.Exp, 1, source: "default", attribution: []),
            Row(RiskIndexObjectKind.Host, "3", RiskIndexCategory.Exp, 1, criticality: 4, state: RiskIndexObjectState.Stale),
            Row(RiskIndexObjectKind.Scenario, "9", RiskIndexCategory.Reg, 5, source: "default", residualAboveInherent: true)
        ];
        var p = new RiskIndexParameters();

        var quality = RiskIndexReadService.QualityOf(Values(), rows, Map, p, [], organization: false);

        Assert.Equal(1.0 / 5, quality.UnassignedWeightFraction, 9); // host 2 of hosts 1, 2, 3 (weights 3, 1, 1)
        Assert.True(quality.UnassignedWarning);
        Assert.Equal(6.0 / 10, quality.DefaultCriticalityWeightFraction, 9); // host 2 and the scenario of every object's weight
        Assert.Equal(1, quality.PresumedCritical); // host 1, once across its two rows
        Assert.Equal(1, quality.ResidualAboveInherent);
        Assert.Equal((0.8, 0.9, 0.72), (quality.Coverage, quality.Freshness, quality.Quality));
        Assert.Null(quality.OrphanApplications);
    }

    [Fact]
    public void TestAnEmptyNodeHasZeroFractionsNotADivisionByZero()
    {
        var quality = RiskIndexReadService.QualityOf(Values(), [], Map, new RiskIndexParameters(), [], organization: false);

        Assert.Equal((0.0, 0.0, false), (quality.UnassignedWeightFraction, quality.DefaultCriticalityWeightFraction,
            quality.UnassignedWarning));
        Assert.Equal(0, quality.PresumedCritical);
    }

    [Fact]
    public void TestTheTreeCountsComeFromTheOrganizationsStoredChipOnly()
    {
        var chip = new StoredChip(RiskIndexChipKeys.DataQuality, new Dictionary<string, string>
        {
            ["divergent_links"] = "3", ["orphan_applications"] = "18", ["legacy_lifecycle_disagreements"] = "141"
        });

        var root = RiskIndexReadService.QualityOf(Values(chip), [], Map, new RiskIndexParameters(), [], organization: true);
        var entity = RiskIndexReadService.QualityOf(Values(chip), [], Map, new RiskIndexParameters(), [], organization: false);
        var unstored = RiskIndexReadService.QualityOf(Values(), [], Map, new RiskIndexParameters(), [], organization: true);

        Assert.Equal((3, 18, 141), (root.DivergentLinks!.Value, root.OrphanApplications!.Value, root.FindingStatusDisagreements!.Value));
        Assert.Null(entity.DivergentLinks);
        Assert.Null(unstored.OrphanApplications);
    }

    [Fact]
    public void TestAStaleSourceIsAnEnabledOneWithNoCurrentReading()
    {
        var p = new RiskIndexParameters();
        var older = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc);
        StoredFactor[] readings =
        [
            new("EXP.D", RiskIndexFactorType.S, 0.9, 0.4, 1, older, RiskIndexSources.V1Device),
            new("EXP.D", RiskIndexFactorType.S, 0.2, 0, 1, newer, RiskIndexSources.V1Device),
            new("EXP.V", RiskIndexFactorType.V, 0.5, 0.5, 1, older, RiskIndexSources.TenableNessus),
            new("EXP.V", RiskIndexFactorType.V, 0.5, 1, 1, newer, RiskIndexSources.TenableNessus),
            new("EXP.V", RiskIndexFactorType.V, 0.5, 0, 1, older, RiskIndexSources.TenableVm), // not enabled by default
            new("EXP.V", RiskIndexFactorType.V, 0.5, 0, 1, older, null)
        ];

        var stale = RiskIndexReadService.StaleSources(readings, p);

        Assert.Equal((RiskIndexSources.V1Device, (DateTime?)newer), (Assert.Single(stale).SourceKey, stale[0].LastReadingUtc));
    }
}
