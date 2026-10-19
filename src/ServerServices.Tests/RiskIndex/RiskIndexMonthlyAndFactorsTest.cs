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
/// M54, T253 (S40 §5.5.1, §8.1–§8.3, §10.9): the monthly category summary and the risk factors of an asset-class tab over
/// real snapshots of the Unit X organization — stored days for pre-computed nodes (never the object rows, never the
/// budget), recomposition under the budget otherwise, the scope rule, the shadow pilot, and every refusal.
/// </summary>
[TestSubject(typeof(RiskIndexReadService))]
public class RiskIndexMonthlyAndFactorsTest : RiskIndexSeedTestBase
{
    private static readonly DateOnly D = DateOnly.FromDateTime(Now);
    private static readonly DateOnly September = new(2026, 9, 15);
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);
    private static readonly int[] UnitXMembers = [UnitX, Lab, Enrolment, Portal];

    private IRiskIndexReadService Service => GetService<IRiskIndexReadService>();

    public RiskIndexMonthlyAndFactorsTest()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(new RiskIndexProfile
        {
            Id = 1, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
            Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), Justification = "pilot",
            SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
            EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
        }));
        var snapshots = GetService<IRiskIndexSnapshotService>();
        Assert.Equal(RiskIndexSnapshotResult.Written, snapshots.RunAsync(September.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc))
            .GetAwaiter().GetResult().Result);
        Assert.Equal(RiskIndexSnapshotResult.Written, snapshots.RunAsync(Now).GetAwaiter().GetResult().Result);
    }

    private static RiskIndexCaller Committee() =>
        new(1, false, new HashSet<string>(new[] { RiskIndexPermissions.View, RiskIndexPermissions.Approve }
            .Concat(RiskIndexModulePermissions.All)));

    private static string Key(int entity) => RiskIndexNodeKey.ForEntity(entity).ToString();

    private RiskIndexReadService WithoutBudget() => new(GetService<ServerServices.Services.IDalService>(), new RiskIndexReadCache(),
        new RiskIndexDerivedBudget(perUser: 0, perProcess: 0, TimeSpan.FromSeconds(5)), GetService<Serilog.ILogger>(),
        GetService<ServerServices.Interfaces.IPermissionsService>());

    // --- the monthly summary -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAnOfficialNodesSummaryAveragesItsCategoryRowsByMonth()
    {
        var summary = await Service.GetMonthlySummaryAsync("org", null, 6, null, Committee());

        Assert.Equal(RiskIndexProvenance.Official, summary.Provenance);
        Assert.Null(summary.Tab);
        Assert.Equal(new[] { "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10" }, summary.Months.Select(m => m.YearMonth));
        Assert.Equal(new[] { false, false, false, false, false, true }, summary.Months.Select(m => m.IsPartial));
        Assert.All(summary.Months, m => Assert.False(m.HasProfileChange));
        Assert.Equal(RiskIndexReadService.ChartOrder.Where(summary.SeriesOrder.Contains), summary.SeriesOrder);

        var october = await Service.GetOverviewAsync("org", D, Committee());
        var september = await Service.GetOverviewAsync("org", September, Committee());
        foreach (var category in october.Categories.Where(c => c.State == RiskIndexCategoryState.Available))
        {
            Assert.Contains(category.Category, summary.SeriesOrder);
            Assert.Equal(category.Value!.Value, summary.Months[5].MeanByCategory[category.Category]!.Value, 9);
            Assert.Equal(september.Categories.Single(c => c.Category == category.Category).Value!.Value,
                summary.Months[4].MeanByCategory[category.Category]!.Value, 9);
        }

        Assert.All(summary.SeriesOrder, c => Assert.Null(summary.Months[0].MeanByCategory[c]));
    }

    /// <summary>
    /// S40 §8.1, §10.9: a pre-computed node's summary — of the node, or of the node ∩ a tab — reads the stored days and
    /// never the object rows nor the derived budget. Proved by reading it with a zero budget and no object rows at all.
    /// </summary>
    [Fact]
    public async Task TestAPrecomputedNodeReadsStoredDaysWithoutTheObjectRowsOrTheBudget()
    {
        var before = await Service.GetMonthlySummaryAsync("org", RiskIndexTabGroup.Devices, 6, null, Committee());
        SeedUnscoped(ctx => ctx.RiskIndexObjectDays.RemoveRange(ctx.RiskIndexObjectDays));
        var service = WithoutBudget();

        var node = await service.GetMonthlySummaryAsync("org", null, 12, null, Committee());
        var tab = await service.GetMonthlySummaryAsync("org", RiskIndexTabGroup.Devices, 6, null, Committee());

        Assert.Equal(12, node.Months.Count);
        Assert.NotNull(node.Months[^1].MeanByCategory[RiskIndexCategory.Exp]);
        Assert.Equal(RiskIndexProvenance.OfficialDerived, tab.Provenance);
        Assert.Equal(before.Months.Select(m => m.MeanByCategory[RiskIndexCategory.Exp]), tab.Months.Select(m => m.MeanByCategory[RiskIndexCategory.Exp]));
        var busy = await Assert.ThrowsAsync<RiskIndexException>(() =>
            service.GetMonthlySummaryAsync(Key(UnitX) + ";k:5", null, 6, null, Committee()));
        Assert.Equal(RiskIndexErrors.DerivedBusy, busy.Code);
    }

    /// <summary>The job's node ∩ tab value and the same intersection recomposed on read are one formula (S40 §5.5.1).</summary>
    [Fact]
    public async Task TestATabSummaryCarriesTheTabsCategoriesInTheFixedOrder()
    {
        var summary = await Service.GetMonthlySummaryAsync("org", RiskIndexTabGroup.Devices, 6, null, Committee());
        var derived = await Service.GetOverviewAsync("c:devices", D, Committee());

        Assert.Equal((RiskIndexTabGroup?)RiskIndexTabGroup.Devices, summary.Tab);
        Assert.Equal(new[] { RiskIndexCategory.Exp, RiskIndexCategory.Ctl, RiskIndexCategory.Gov }, summary.SeriesOrder);
        var exposure = derived.Categories.Single(c => c.Category == RiskIndexCategory.Exp).Value!.Value;
        Assert.Equal(exposure, summary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]!.Value, 1);
        Assert.Equal(new[] { RiskIndexCategory.Exp }, (await Service.GetMonthlySummaryAsync("org", RiskIndexTabGroup.Identities, 6,
            null, Committee())).SeriesOrder);
    }

    [Fact]
    public async Task TestADerivedNodeIsRecomposedDayByDay()
    {
        var summary = await Service.GetMonthlySummaryAsync(Key(UnitX) + ";k:5", null, 6, null, Committee());
        var october = await Service.GetOverviewAsync(Key(UnitX) + ";k:5", D, Committee());

        Assert.Equal(RiskIndexProvenance.OfficialDerived, summary.Provenance);
        var exposure = october.Categories.Single(c => c.Category == RiskIndexCategory.Exp);
        Assert.Equal(exposure.Value!.Value, summary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]!.Value, 9);
        Assert.NotNull(summary.Months[^2].MeanByCategory[RiskIndexCategory.Exp]);
        Assert.All(summary.SeriesOrder, c => Assert.NotEqual(RiskIndexCategoryState.NotApplicable,
            october.Categories.Single(x => x.Category == c).State));
    }

    /// <summary>A partial view recomposes only what the claims see, and says so in its provenance (S40 §11.3).</summary>
    [Fact]
    public async Task TestAPartialNodeSummaryIsComputedOverTheVisibleObjectsOnly()
    {
        ScopeTo(UnitX, Lab);

        var summary = await Service.GetMonthlySummaryAsync(Key(UnitX), RiskIndexTabGroup.Devices, 6, null, Committee());

        Assert.Equal(RiskIndexProvenance.PartialRestricted, summary.Provenance);
        Assert.NotNull(summary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]);
    }

    [Fact]
    public async Task TestAClassDayTheClaimsDidNotCoverIsLeftOut()
    {
        SeedUnscoped(ctx => ctx.RiskIndexNodeMembers.Add(new RiskIndexNodeMember
        {
            SnapshotDate = September, ProfileId = 1, NodeEntityId = UnitX, MemberKind = RiskIndexNodeMemberKind.Closure,
            MemberEntityId = Invalid, CreatedAt = Now
        }));
        ScopeTo(UnitXMembers);

        var summary = await Service.GetMonthlySummaryAsync(Key(UnitX), RiskIndexTabGroup.Devices, 6, null, Committee());

        Assert.Equal(RiskIndexProvenance.OfficialDerived, summary.Provenance);
        Assert.Null(summary.Months[^2].MeanByCategory[RiskIndexCategory.Exp]);
        Assert.NotNull(summary.Months[^1].MeanByCategory[RiskIndexCategory.Exp]);
    }

    /// <summary>
    /// A profile activated mid-day writes a second row set for the same date (the job is idempotent per date and profile):
    /// the official summary keeps the rows of the profile that day's members name, once per category — never a 500.
    /// </summary>
    [Fact]
    public async Task TestTwoProfilesOnOneDateKeepOnlyTheDaysProfile()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(new RiskIndexProfile
        {
            Id = 2, Version = 2, Name = "Pilot v2", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
            Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters { MA = 0.1 }), Justification = "v2",
            SubmittedAt = Pilot, ApprovedAt = Pilot, CommitteeDecisionRef = "CRTI-2026-10", EffectiveFrom = Pilot.AddDays(1),
            CreatedAt = Pilot
        }));
        Assert.Equal(RiskIndexSnapshotResult.Written, (await GetService<IRiskIndexSnapshotService>().RunAsync(Now)).Result);
        var stored = new Dictionary<int, Dictionary<RiskIndexCategory, double>>();
        SeedUnscoped(ctx =>
        {
            foreach (var profile in new[] { 1, 2 })
                stored[profile] = ctx.RiskIndexSnapshots.Where(s => s.SnapshotDate == D && s.ProfileId == profile &&
                                                                     s.ScopeKind == RiskIndexScopeKind.Organization &&
                                                                     s.Category != RiskIndexCategory.Icr &&
                                                                     s.CategoryState == RiskIndexCategoryState.Available)
                    .ToDictionary(s => s.Category, s => s.ValueUnfloored);
        });

        var summary = await Service.GetMonthlySummaryAsync("org", null, 6, null, Committee());

        Assert.Contains(stored[2], c => Math.Abs(c.Value - stored[1][c.Key]) > 1e-9); // the two row sets do differ
        foreach (var (category, value) in stored[2])
            Assert.Equal(value, summary.Months[^1].MeanByCategory[category]!.Value, 9);
        Assert.False(summary.Months[^1].HasProfileChange);
    }

    /// <summary>
    /// A node already cut by a tab is its own intersection, so a month the cache holds for the tab's categories must not
    /// answer the read of the whole chart: the two reads keep their months apart, and the read without the tab is what a
    /// fresh one gives.
    /// </summary>
    [Fact]
    public async Task TestTheMonthCacheKeepsATabCutNodesCategorySetsApart()
    {
        var fresh = await WithBudget().GetMonthlySummaryAsync("c:devices", null, 6, null, Committee());
        var cache = new RecordingCache();
        var service = WithBudget(cache);

        await service.GetMonthlySummaryAsync("c:devices", RiskIndexTabGroup.Devices, 6, null, Committee());
        var withTab = cache.Taken("monthly-days|");
        var after = await service.GetMonthlySummaryAsync("c:devices", null, 6, null, Committee());
        var withoutTab = cache.Taken("monthly-days|").Except(withTab).ToList();

        Assert.NotEmpty(withTab); // September is complete, so its days are cached
        Assert.Equal(withTab.Count, withoutTab.Count);
        Assert.Equal(fresh.SeriesOrder, after.SeriesOrder);
    }

    /// <summary>The real cache, recording every key it is asked for.</summary>
    private sealed class RecordingCache : IRiskIndexReadCache
    {
        private readonly RiskIndexReadCache _inner = new();
        private readonly List<string> _keys = [];

        public IReadOnlyList<string> Taken(string prefix)
        {
            lock (_keys) return _keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Distinct().ToList();
        }

        public string ScopeKey(DAL.Context.EntityScope scope) => _inner.ScopeKey(scope);

        public Task<T> GetOrCreateAsync<T>(string key, TimeSpan timeToLive, Func<Task<T>> factory) where T : class
        {
            lock (_keys) _keys.Add(key);
            return _inner.GetOrCreateAsync(key, timeToLive, factory);
        }

        public void Clear() => _inner.Clear();
    }

    private RiskIndexReadService WithBudget(IRiskIndexReadCache? cache = null) => new(GetService<ServerServices.Services.IDalService>(),
        cache ?? new RiskIndexReadCache(), new RiskIndexDerivedBudget(), GetService<Serilog.ILogger>(),
        GetService<ServerServices.Interfaces.IPermissionsService>());

    [Theory]
    [InlineData("org", null, 3, RiskIndexErrors.InvalidMonths)]
    [InlineData("org", 9, 6, RiskIndexErrors.InvalidTab)]
    [InlineData("c:server", (int)RiskIndexTabGroup.InternetFacing, 6, RiskIndexErrors.InvalidTab)]
    [InlineData("e:x", null, 6, RiskIndexErrors.InvalidNode)]
    public async Task TestTheSummaryRefusesItsArguments(string node, int? tab, int months, string code)
    {
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetMonthlySummaryAsync(node, (RiskIndexTabGroup?)tab, months, null, Committee()));

        Assert.Equal((400, code), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task TestTheSummaryFollowsTheShadowAndScopeRules()
    {
        var viewer = new RiskIndexCaller(2, false, new HashSet<string> { RiskIndexPermissions.View });
        var shadow = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetMonthlySummaryAsync("org", null, 6, null, viewer));
        var factors = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetRiskFactorsAsync("org", RiskIndexTabGroup.Devices, null, viewer));
        ScopeTo(UnitXMembers);
        var org = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetMonthlySummaryAsync("org", null, 6, null, Committee()));

        Assert.Equal((403, RiskIndexErrors.InShadow), (shadow.Status, shadow.Code));
        Assert.Equal((403, RiskIndexErrors.InShadow), (factors.Status, factors.Code));
        Assert.Equal((403, RiskIndexErrors.OrgRequiresGlobalScope), (org.Status, org.Code));
    }

    /// <summary>The pure half: a month that mixes profiles is marked, a month with no value is null, never zero.</summary>
    [Fact]
    public void TestTheMonthBucketsMarkProfileChangesAndKeepNoValueAsNull()
    {
        var days = new List<RiskIndexReadService.MonthlyDay>
        {
            new(new DateOnly(2026, 9, 2), 1, new Dictionary<RiskIndexCategory, double> { [RiskIndexCategory.Exp] = 70 }, [RiskIndexCategory.Exp]),
            new(new DateOnly(2026, 9, 20), 2, new Dictionary<RiskIndexCategory, double> { [RiskIndexCategory.Exp] = 80 }, [RiskIndexCategory.Exp]),
            new(new DateOnly(2026, 10, 30), 2, new Dictionary<RiskIndexCategory, double> { [RiskIndexCategory.Exp] = 10 }, [RiskIndexCategory.Exp])
        };

        var summary = RiskIndexReadService.BuildMonthlySummary(RiskIndexTabGroup.Devices, [RiskIndexCategory.Exp, RiskIndexCategory.Gov],
            days, new DateOnly(2026, 9, 1), 2, new DateOnly(2026, 10, 31), RiskIndexProvenance.OfficialDerived);

        Assert.True(summary.Months[0].HasProfileChange);
        Assert.Equal(75, summary.Months[0].MeanByCategory[RiskIndexCategory.Exp]);
        Assert.Null(summary.Months[0].MeanByCategory[RiskIndexCategory.Gov]);
        Assert.False(summary.Months[1].IsPartial); // the last day of the month is a complete month
        Assert.False(summary.Months[1].HasProfileChange);
    }

    // --- the risk factors ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The factor lines are the tab's Euler points regrouped (S39 §4.8): their points add up to the objects' contributions,
    /// and each line's count is exactly the objects its <c>ObjectFilter</c> lists on the same intersection.
    /// </summary>
    [Fact]
    public async Task TestTheFactorLinesRegroupTheTabsContributions()
    {
        var lines = await Service.GetRiskFactorsAsync("org", RiskIndexTabGroup.Devices, null, Committee());
        var objects = await Service.GetObjectsAsync("c:devices", null, new RiskIndexObjectQuery(PageSize: 500), Committee());

        Assert.NotEmpty(lines);
        Assert.Contains(lines, l => l.FactorKey == RiskIndexFactorAttribution.Presumed);
        Assert.Equal(lines.OrderByDescending(l => l.Points).ThenByDescending(l => l.Count).Select(l => l.FactorKey), lines.Select(l => l.FactorKey));
        Assert.InRange(lines.Sum(l => l.Points) - objects.Rows.Sum(r => r.ContributionPoints), -lines.Count, lines.Count);
        foreach (var line in lines)
        {
            Assert.Equal("RiskFactor_" + line.FactorKey.Replace('.', '_'), line.LabelKey);
            var listed = await Service.GetObjectsAsync("c:devices", null, new RiskIndexObjectQuery(line.ObjectFilter, PageSize: 500),
                Committee());
            Assert.Equal(line.Count, listed.Rows.Select(r => r.Object.Object).Distinct().Count());
        }
    }

    /// <summary>
    /// A line counts each object once over its tab's categories, so the list it opens holds each object once and only rows of
    /// those categories: its total is the line's count, even for an object presumed in several categories.
    /// </summary>
    [Fact]
    public async Task TestAFactorLinesListTotalIsItsCount()
    {
        var devices = RiskIndexTabMap.Categories(RiskIndexTabGroup.Devices);
        var presumedRows = 0;
        SeedUnscoped(ctx =>
        {
            foreach (var row in ctx.RiskIndexObjectDays.Where(o => o.SnapshotDate == D && o.ObjectKind == RiskIndexObjectKind.Host &&
                                                                   o.ObjectRef == Db.ToString()))
            {
                row.State = RiskIndexObjectState.Presumed;
                row.RValue = Math.Max(row.RValue, 0.5);
                if (devices.Contains(row.Category)) presumedRows++;
            }
        });

        var lines = await Service.GetRiskFactorsAsync("org", RiskIndexTabGroup.Devices, null, Committee());

        Assert.True(presumedRows >= 2, "DB-02 is presumed in more than one of the tab's categories");
        Assert.Contains(lines, l => l.FactorKey == RiskIndexFactorAttribution.Presumed);
        foreach (var line in lines)
        {
            var listed = await Service.GetObjectsAsync("c:devices", null, new RiskIndexObjectQuery(line.ObjectFilter, PageSize: 500),
                Committee());
            Assert.Equal((line.FactorKey, line.Count), (line.FactorKey, listed.Total));
            Assert.Equal(listed.Total, listed.Rows.Select(r => r.Object.Object).Distinct().Count());
            Assert.All(listed.Rows, r => Assert.Contains(r.Category, devices));
        }
    }

    [Fact]
    public async Task TestAPartialViewsFactorsCountOnlyTheVisible()
    {
        var full = await Service.GetRiskFactorsAsync(Key(UnitX), RiskIndexTabGroup.Devices, null, Committee());
        ScopeTo(UnitX, Lab);

        var partial = await Service.GetRiskFactorsAsync(Key(UnitX), RiskIndexTabGroup.Devices, null, Committee());

        Assert.True(partial.Sum(l => l.Count) <= full.Sum(l => l.Count));
        Assert.NotEmpty(partial);
    }

    [Fact]
    public async Task TestTheFactorsRefuseAMissingOrConflictingTabAndGoThroughTheBudget()
    {
        var missing = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetRiskFactorsAsync("org", null, null, Committee()));
        var conflict = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetRiskFactorsAsync("c:application", RiskIndexTabGroup.Devices, null, Committee()));
        var busy = await Assert.ThrowsAsync<RiskIndexException>(() =>
            WithoutBudget().GetRiskFactorsAsync("org", RiskIndexTabGroup.Devices, null, Committee()));

        Assert.Equal((400, RiskIndexErrors.InvalidTab), (missing.Status, missing.Code));
        Assert.Equal((400, RiskIndexErrors.InvalidTab), (conflict.Status, conflict.Code));
        Assert.Equal((503, RiskIndexErrors.DerivedBusy), (busy.Status, busy.Code));
    }

    [Theory]
    [InlineData("factor=EXP V")]
    [InlineData("factor=EXP.V,factor=GOV.T")]
    public async Task TestTheObjectListRefusesAMalformedFactorFilter(string filters)
    {
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(filters), Committee()));

        Assert.Equal((400, RiskIndexErrors.InvalidFilter), (refusal.Status, refusal.Code));
    }

    // --- the attribution rule (pure) -------------------------------------------------------------------------------------

    private static StoredFactor Factor(string key, double x, RiskIndexFactorType type = RiskIndexFactorType.V, double v = 1,
        double u = 1) => new(key, type, x, v, u, null, null);

    [Fact]
    public void TestInExposureTheDominantFactorTakesTheWholeObject()
    {
        var split = RiskIndexFactorAttribution.Split(RiskIndexCategory.Exp, RiskIndexObjectState.Assessed, 0.8, 5,
            [Factor(RiskIndexFactors.ExpDevice, 0.9), Factor(RiskIndexFactors.ExpVulnerabilities, 0.5)], [], new RiskIndexParameters());

        Assert.Equal(new[] { (RiskIndexFactors.ExpDevice, 1.0) }, split);
    }

    [Fact]
    public void TestAKnownExploitationSignalNamesTheSubFactor()
    {
        var split = RiskIndexFactorAttribution.Split(RiskIndexCategory.Exp, RiskIndexObjectState.Assessed, 0.8, 3,
            [Factor(RiskIndexFactors.ExpVulnerabilities, 0.9)],
            [new StoredSignal(RiskIndexFactors.ExpVulnerabilities, "finding:1", 0.9, ["nessus", "exploited"])], new RiskIndexParameters());

        Assert.Equal("EXP.V.exploited", Assert.Single(split).Key);
    }

    [Fact]
    public void TestOutsideExposureThePointsSplitByTheMraShares()
    {
        var split = RiskIndexFactorAttribution.Split(RiskIndexCategory.Gov, RiskIndexObjectState.Assessed, 0.7, 5,
            [Factor(RiskIndexFactors.GovAppetite, 0.6, RiskIndexFactorType.S), Factor(RiskIndexFactors.GovReview, 0.3, RiskIndexFactorType.S)],
            [], new RiskIndexParameters());

        Assert.Equal(2, split.Count);
        Assert.Equal(1, split.Sum(s => s.Share), 9);
        Assert.True(split.Single(s => s.Key == RiskIndexFactors.GovAppetite).Share > split.Single(s => s.Key == RiskIndexFactors.GovReview).Share);
    }

    [Theory]
    [InlineData(RiskIndexObjectState.Presumed, 0.4, "presumed")]
    [InlineData(RiskIndexObjectState.AssessedClean, 0.0, null)]
    [InlineData(RiskIndexObjectState.NotAssessed, 0.0, null)]
    [InlineData(RiskIndexObjectState.Assessed, 0.0, null)]
    public void TestPresumptionAndNoRisk(RiskIndexObjectState state, double r, string? expected)
    {
        var split = RiskIndexFactorAttribution.Split(RiskIndexCategory.Exp, state, r, 3, [Factor(RiskIndexFactors.ExpDevice, 0.4)], [],
            new RiskIndexParameters());

        Assert.Equal(expected, split.SingleOrDefault().Key);
    }

    [Fact]
    public void TestTheLinesCountObjectsOnceAndRoundTheirPoints()
    {
        var a = new ServerServices.RiskIndex.Engine.IcrObjectKey(RiskIndexObjectKind.Host, "1");
        var b = new ServerServices.RiskIndex.Engine.IcrObjectKey(RiskIndexObjectKind.Host, "2");

        var lines = RiskIndexFactorAttribution.Aggregate(
        [
            new(a, 10.4, [("EXP.D", 1.0)]), new(a, 4, [("GOV.T", 0.5), ("GOV.R", 0.5)]), new(b, 2.2, [("EXP.D", 1.0)]),
            new(b, 0, [])
        ]);

        Assert.Equal(new[] { ("EXP.D", 2, 13), ("GOV.R", 1, 2), ("GOV.T", 1, 2) }, lines.Select(l => (l.FactorKey, l.Count, l.Points)));
        Assert.Equal("factor=EXP.D", lines[0].ObjectFilter);
        Assert.False(RiskIndexFactorAttribution.IsFactorKey("EXP V"));
        Assert.True(RiskIndexFactorAttribution.IsFactorKey("EXP.V.exploited"));
    }
}
