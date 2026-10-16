using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// M53, T249 (S40 §8.1–§8.3, §11.3, §12.2): the read API over a real snapshot of the Unit X organization — the official
/// read rule, partial views without hidden counts, the shadow pilot per role, per-module redaction, the cache keyed by scope
/// and permissions, and the refusals of the trend and "what moved" for partial and derived nodes.
/// </summary>
[TestSubject(typeof(RiskIndexReadService))]
public class RiskIndexReadServiceTest : RiskIndexSeedTestBase
{
    private static readonly DateOnly D = DateOnly.FromDateTime(Now);
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);

    /// <summary>Everything Unit X's node holds: itself, the lab, the process linked by EAV and the process's application.</summary>
    private static readonly int[] UnitXMembers = [UnitX, Lab, Enrolment, Portal];

    private IRiskIndexReadService Service => GetService<IRiskIndexReadService>();

    public RiskIndexReadServiceTest()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(new RiskIndexProfile
        {
            Id = 1, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
            Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), Justification = "pilot",
            SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
            EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
        }));
        var outcome = GetService<IRiskIndexSnapshotService>().RunAsync(Now).GetAwaiter().GetResult();
        Assert.Equal(RiskIndexSnapshotResult.Written, outcome.Result);
    }

    /// <summary>The committee: explicit approve, every module permission.</summary>
    private static RiskIndexCaller Committee(params string[] extra) =>
        new(1, false, new HashSet<string>(new[] { RiskIndexPermissions.View, RiskIndexPermissions.Approve }
            .Concat(RiskIndexModulePermissions.All).Concat(extra)));

    private static RiskIndexCaller Caller(params string[] permissions) =>
        new(2, false, new HashSet<string>(permissions.Append(RiskIndexPermissions.View)));

    private static readonly RiskIndexCaller Admin = new(3, true, new HashSet<string>());

    private void Publish() =>
        SeedUnscoped(ctx => ctx.Settings.Add(new Setting { Name = RiskIndexSettingKeys.Publication, Value = "published" }));

    private static string Key(int entity) => RiskIndexNodeKey.ForEntity(entity).ToString();

    // --- the shadow pilot (S39 §13.3; S40 §8.2) ------------------------------------------------------------------------

    [Fact]
    public async Task TestInShadowOnlyTheCommitteeAndAdministratorsSeeTheIndex()
    {
        var viewer = Caller(RiskIndexModulePermissions.RiskManagement);
        var configurer = Caller(RiskIndexPermissions.Configure);

        Assert.Equal(RiskIndexErrors.InShadow,
            (await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetOverviewAsync("org", null, viewer))).Code);
        Assert.Equal(403, (await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetOverviewAsync("org", null, configurer))).Status);
        Assert.NotNull((await Service.GetOverviewAsync("org", null, Committee())).Headline);
        Assert.NotNull((await Service.GetOverviewAsync("org", null, Admin)).Headline);
    }

    /// <summary>The administrator flag never counts as the explicit approve permission — but it does pass the shadow rule.</summary>
    [Fact]
    public async Task TestOnceAPublishedIndexIsVisibleToAViewer()
    {
        Publish();

        var overview = await Service.GetOverviewAsync("org", null, Caller());

        Assert.Equal(RiskIndexProvenance.Official, overview.Provenance);
        Assert.Equal(D, overview.SnapshotDate);
    }

    [Fact]
    public async Task TestTheLatestSnapshotAndItsDatesAreOutsideTheShadowRule()
    {
        var latest = await Service.GetLatestAsync();

        Assert.Equal((D, 1, RiskIndexPublicationState.Shadow), (latest!.Date, latest.ProfileId, latest.Publication));
        Assert.Equal(new[] { D }, await Service.GetSnapshotDatesAsync(null, null));
    }

    // --- the official read rule and partial views (S40 §11.3) ---------------------------------------------------------

    [Fact]
    public async Task TestTheOrganizationNeedsGlobalScope()
    {
        ScopeTo(UnitXMembers);

        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetOverviewAsync("org", null, Committee()));

        Assert.Equal((403, RiskIndexErrors.OrgRequiresGlobalScope), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task TestClaimsCoveringEveryMemberReadTheOfficialSnapshot()
    {
        ScopeTo(UnitXMembers);

        var overview = await Service.GetOverviewAsync(Key(UnitX), null, Committee());

        Assert.Equal(RiskIndexProvenance.Official, overview.Provenance);
        ScopeToEverything();
        var official = await Service.GetOverviewAsync(Key(UnitX), null, Committee());
        Assert.Equal(official.Headline!.Unfloored, overview.Headline!.Unfloored);
        Assert.Equal(official.ObjectCount, overview.ObjectCount);
    }

    /// <summary>
    /// S40 §12.2: the process and the application reach Unit X only through EAV links, and their objects' scope entities are
    /// outside a unit-only claim — so the read is partial, computed over the visible objects alone, with no hidden count.
    /// </summary>
    [Fact]
    public async Task TestClaimsCoveringPartOfTheMembersReadAPartialViewWithoutHiddenCounts()
    {
        var official = await Service.GetOverviewAsync(Key(UnitX), null, Committee());
        ScopeTo(UnitX, Lab);

        var partial = await Service.GetOverviewAsync(Key(UnitX), null, Committee());

        Assert.Equal(RiskIndexProvenance.PartialRestricted, partial.Provenance);
        Assert.Contains(partial.Chips, c => c.Key == "partial_visibility");
        Assert.True(partial.ObjectCount < official.ObjectCount);
        var visible = await Service.GetObjectsAsync(Key(UnitX), null, new RiskIndexObjectQuery(PageSize: 500), Committee());
        Assert.Equal(partial.ObjectCount, visible.Rows.Select(r => r.Object.Object).Distinct().Count());
        Assert.DoesNotContain(visible.Rows, r => r.Object.Object.Kind == RiskIndexObjectKind.Application);
        Assert.Null(partial.Headline!.Delta7d);
    }

    [Fact]
    public async Task TestAPartialViewHasNoTrendAndNoChanges()
    {
        ScopeTo(UnitX, Lab);

        var trend = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetTrendAsync(Key(UnitX), 30, RiskIndexCategory.Icr, null, Committee()));
        var changes = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetChangesAsync(Key(UnitX), null, null, Committee()));

        Assert.Equal((409, RiskIndexErrors.TrendUnavailablePartial), (trend.Status, trend.Code));
        Assert.Equal((409, RiskIndexErrors.ChangesUnavailablePartial), (changes.Status, changes.Code));
    }

    [Fact]
    public async Task TestADerivedNodeHasNoYearOfTrend()
    {
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetTrendAsync(Key(UnitX) + ";c:devices", 365, RiskIndexCategory.Icr, null, Committee()));

        Assert.Equal((400, RiskIndexErrors.WindowRequiresPrecomputedNode), (refusal.Status, refusal.Code));
        var month = await Service.GetTrendAsync(Key(UnitX) + ";c:devices", 30, RiskIndexCategory.Icr, null, Committee());
        Assert.Single(month.Points);
    }

    [Fact]
    public async Task TestAnOfficialTrendReadsTheNodesRows()
    {
        var trend = await Service.GetTrendAsync(Key(UnitX), 90, RiskIndexCategory.Icr, null, Committee());

        var point = Assert.Single(trend.Points);
        Assert.Equal(D, point.Date);
        Assert.NotNull(point.Unfloored);
        Assert.Single(trend.BandSegments);
    }

    /// <summary>
    /// Coverage is per day (S40 §11.3): claims that cover the node today but not on an earlier day — the node then held an
    /// entity outside them — read no value of that day in the trend, and no "what moved" across it.
    /// </summary>
    [Fact]
    public async Task TestADayTheClaimsDidNotCoverIsNeitherInTheTrendNorInTheChanges()
    {
        await GetService<IRiskIndexSnapshotService>().RunAsync(Now.AddDays(1));
        SeedUnscoped(ctx => ctx.RiskIndexNodeMembers.Add(new RiskIndexNodeMember
        {
            SnapshotDate = D, ProfileId = 1, NodeEntityId = UnitX, MemberKind = RiskIndexNodeMemberKind.Closure,
            MemberEntityId = Invalid, CreatedAt = Now
        }));
        ScopeTo(UnitXMembers);

        var trend = await Service.GetTrendAsync(Key(UnitX), 30, RiskIndexCategory.Icr, D.AddDays(1), Committee());
        var changes = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetChangesAsync(Key(UnitX), D, D.AddDays(1), Committee()));

        Assert.Equal(new[] { D.AddDays(1) }, trend.Points.Select(p => p.Date));
        Assert.Equal((409, RiskIndexErrors.ChangesUnavailablePartial), (changes.Status, changes.Code));
        ScopeToEverything();
        Assert.Equal(2, (await Service.GetTrendAsync(Key(UnitX), 30, RiskIndexCategory.Icr, D.AddDays(1), Committee())).Points.Count);
    }

    /// <summary>S40 §9.6: a snapshot without its members is never read as "no members" by a scoped reader.</summary>
    [Fact]
    public async Task TestASnapshotWithoutMembersIsRefusedToAScopedReader()
    {
        SeedUnscoped(ctx => ctx.RiskIndexNodeMembers.RemoveRange(ctx.RiskIndexNodeMembers.Where(m => m.NodeEntityId == Lab)));
        ScopeTo(Lab);

        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetOverviewAsync(Key(Lab), null, Committee()));

        Assert.Equal((403, RiskIndexErrors.SnapshotMembershipMissing), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task TestMyScopeListsTheClaimedEntitiesWithoutAHeadline()
    {
        ScopeTo(UnitX, Lab);

        var overview = await Service.GetOverviewAsync("scope", null, Committee());

        Assert.Null(overview.Headline);
        Assert.Equal(new[] { Key(UnitX), Key(Lab) }, overview.ScopeEntities.Select(e => e.Key));
    }

    [Fact]
    public async Task TestAnUnknownNodeOrAMalformedKeyIsRefused()
    {
        var malformed = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetOverviewAsync("e:abc", null, Committee()));
        var unknown = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetOverviewAsync(Key(999), null, Committee()));

        Assert.Equal((400, RiskIndexErrors.InvalidNode), (malformed.Status, malformed.Code));
        Assert.Equal((404, RiskIndexErrors.NodeNotFound), (unknown.Status, unknown.Code));
    }

    // --- redaction (S40 §11.3) ----------------------------------------------------------------------------------------

    /// <summary>S40 §12.2: a caller with risk_index_view alone gets no scenario title, scenario input or signal text.</summary>
    [Fact]
    public async Task TestWithoutTheModulePermissionsObjectsAreRedacted()
    {
        Publish();
        var viewer = Caller();

        var rows = (await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(PageSize: 500), viewer)).Rows;
        var scenario = rows.First(r => r.Object.Object.Kind == RiskIndexObjectKind.Scenario);
        var host = rows.First(r => r.Object.Object.Kind == RiskIndexObjectKind.Host);

        Assert.Equal(("Scenario #1", RiskIndexModulePermissions.RiskManagement), (scenario.Object.Label, scenario.Object.RequiresPermission));
        Assert.Equal(RiskIndexModulePermissions.Hosts, host.Object.RequiresPermission);
        Assert.DoesNotContain(rows, r => r.Object.Label == "Risk 1");

        var detail = await Service.GetObjectAsync(RiskIndexObjectKind.Scenario, "1", null, null, viewer);
        Assert.Null(detail.Scenario);
        Assert.Empty(detail.Navigation);
        Assert.All(detail.Categories.SelectMany(c => c.Factors).SelectMany(f => f.Signals), s => Assert.Null(s.Text));

        var hostDetail = await Service.GetObjectAsync(RiskIndexObjectKind.Host, Db.ToString(), null, null, viewer);
        Assert.All(hostDetail.Categories.SelectMany(c => c.Factors).SelectMany(f => f.Signals), s => Assert.Null(s.Text));
    }

    [Fact]
    public async Task TestWithTheModulePermissionsObjectsAreNamed()
    {
        var rows = (await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(PageSize: 500), Committee())).Rows;

        Assert.Contains(rows, r => r.Object.Label == "Risk 1" && r.Object.RequiresPermission is null);
        Assert.Contains(rows, r => r.Object.Label == $"host-{Srv}");
        var detail = await Service.GetObjectAsync(RiskIndexObjectKind.Scenario, "1", null, null, Committee());
        Assert.NotNull(detail.Scenario);
        Assert.Contains(detail.Navigation, n => n.Module == RiskIndexNavigationModule.Risk);
    }

    /// <summary>The label filter matches only what the caller sees: a redacted title never reveals its row.</summary>
    [Fact]
    public async Task TestTheLabelFilterNeverMatchesARedactedTitle()
    {
        Publish();

        var redacted = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery("label=Risk 1"), Caller());
        var named = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery("label=Risk 1"), Committee());

        Assert.Equal(0, redacted.Total);
        Assert.True(named.Total > 0);
    }

    /// <summary>S40 §12.2: the cache is keyed by the module permissions — the same node and scope, two payloads.</summary>
    [Fact]
    public async Task TestTheCacheIsKeyedByTheModulePermissions()
    {
        Publish();

        var named = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(PageSize: 500), Committee());
        var redacted = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(PageSize: 500), Caller());

        Assert.Contains(named.Rows, r => r.Object.Label == "Risk 1");
        Assert.DoesNotContain(redacted.Rows, r => r.Object.Label == "Risk 1");
    }

    /// <summary>S40 §12.2: two scopes on the same node never share an entry.</summary>
    [Fact]
    public async Task TestTheCacheIsKeyedByTheScope()
    {
        ScopeTo(UnitXMembers);
        var covered = await Service.GetOverviewAsync(Key(UnitX), null, Committee());
        ScopeTo(UnitX, Lab);
        var partial = await Service.GetOverviewAsync(Key(UnitX), null, Committee());

        Assert.Equal(RiskIndexProvenance.Official, covered.Provenance);
        Assert.Equal(RiskIndexProvenance.PartialRestricted, partial.Provenance);
    }

    [Fact]
    public void TestTheScopeKeyChangesWithTheHierarchyRevision()
    {
        var hierarchy = new ServerServices.Security.EntityHierarchy();
        var cache = new RiskIndexReadCache(hierarchy);
        var scope = DAL.Context.EntityScope.ForEntities([UnitX]);

        var before = cache.ScopeKey(scope);
        hierarchy.Invalidate();

        Assert.NotEqual(before, cache.ScopeKey(scope));
        Assert.Equal("global", cache.ScopeKey(DAL.Context.EntityScope.Unrestricted));
    }

    // --- the object list's contract (S40 §8.2) ------------------------------------------------------------------------

    [Theory]
    [InlineData("sensitivity", null)]
    [InlineData("delta", null)]
    [InlineData(null, "owner=1")]
    [InlineData(null, "category")]
    public async Task TestNoSortBySensitivityOrDeltaAndOnlyDeclaredFilters(string? sorts, string? filters)
    {
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(filters, sorts), Committee()));

        Assert.Equal(400, refusal.Status);
        Assert.Contains(refusal.Code, new[] { RiskIndexErrors.InvalidSort, RiskIndexErrors.InvalidFilter });
    }

    [Fact]
    public async Task TestObjectsArePagedSortedAndFiltered()
    {
        var all = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(PageSize: 500), Committee());
        var page = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(null, "-r", 2, 3), Committee());
        var exposure = await Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery("category=Exp"), Committee());

        Assert.Equal(all.Total, page.Total);
        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(all.Rows.OrderByDescending(r => r.R).Skip(3).First().R, page.Rows[0].R);
        Assert.All(exposure.Rows, r => Assert.Equal(RiskIndexCategory.Exp, r.Category));
        await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetObjectsAsync("org", null, new RiskIndexObjectQuery(PageSize: 501), Committee()));
    }

    [Fact]
    public async Task TestAnObjectOutOfScopeIsNotFound()
    {
        ScopeTo(Lab);

        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetObjectAsync(RiskIndexObjectKind.Host, Srv.ToString(), null, null, Committee()));

        Assert.Equal((404, RiskIndexErrors.ObjectNotFound), (refusal.Status, refusal.Code));
        Assert.NotNull(await Service.GetObjectAsync(RiskIndexObjectKind.Host, LabHost.ToString(), null, null, Committee()));
    }

    // --- the other reads ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheCategoriesCarryTheirEulerPoints()
    {
        var overview = await Service.GetOverviewAsync("org", null, Committee());

        var points = overview.Categories.Where(c => c.State == RiskIndexCategoryState.Available).Sum(c => c.Points ?? 0);
        Assert.Equal(overview.Headline!.Unfloored!.Value, points, 6);
        Assert.Equal(3, overview.Bands.Count);
    }

    [Fact]
    public async Task TestTheDrillDownListsUnitsAndUnassigned()
    {
        var children = await Service.GetNodesAsync("org", RiskIndexDimension.Unit, null, null, Committee());

        Assert.Contains(children.Rows, r => r.Node.Key == Key(UnitX) && r.Provenance == RiskIndexProvenance.Official);
        Assert.Contains(children.Rows, r => r.Node.Key == "u" && r.Provenance == RiskIndexProvenance.OfficialDerived);
        Assert.NotEmpty(children.Groups);

        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetNodesAsync("org", RiskIndexDimension.Unit, "e:10", null, Committee()));
        Assert.Equal(RiskIndexErrors.InvalidFilter, refusal.Code);
    }

    /// <summary>A partial parent lists only children with visible objects (S40 §5.6.5).</summary>
    [Fact]
    public async Task TestAScopedDrillDownOmitsChildrenWithNothingVisible()
    {
        ScopeTo(UnitX, Lab);

        var classes = await Service.GetNodesAsync(Key(UnitX), RiskIndexDimension.AssetClass, null, null, Committee());

        Assert.All(classes.Rows, r => Assert.True(r.ObjectCount > 0));
        Assert.All(classes.Rows, r => Assert.Equal(RiskIndexProvenance.PartialRestricted, r.Provenance));
        Assert.DoesNotContain(classes.Rows, r => r.Node.Key.Contains("c:application", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestTheInventoryNeedsATabAndCountsTheAssessed()
    {
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetInventoryAsync("org", null, null, Committee()));
        Assert.Equal(RiskIndexErrors.InvalidTab, refusal.Code);

        var devices = await Service.GetInventoryAsync("org", RiskIndexTabGroup.Devices, null, Committee());

        Assert.True(devices.InventoryCount > 0);
        Assert.Equal(devices.Overall.Assessed, devices.RDistributionByBand.Values.Sum());
        Assert.Contains(devices.Sources, s => s.SourceKey == RiskIndexSources.TenableVm && !s.Enabled);
    }

    [Fact]
    public async Task TestChangesNeedAnEarlierSnapshotAndAValidPeriod()
    {
        var missing = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetChangesAsync("org", null, null, Committee()));
        var backwards = await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetChangesAsync("org", D, D, Committee()));
        var tooLong = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.GetChangesAsync("org", D.AddDays(-31), D, Committee()));

        Assert.Equal(404, missing.Status);
        Assert.Equal((400, RiskIndexErrors.InvalidPeriod), (backwards.Status, backwards.Code));
        Assert.Equal(RiskIndexErrors.InvalidPeriod, tooLong.Code);
    }

    [Fact]
    public async Task TestChangesSumTheStoredAttributionOfTheNextDay()
    {
        await GetService<IRiskIndexSnapshotService>().RunAsync(Now.AddDays(1));

        var changes = await Service.GetChangesAsync("org", null, D.AddDays(1), Committee());

        Assert.Equal(D, changes.From);
        Assert.True(Math.Abs(changes.Residual) < 0.5, $"residual {changes.Residual}");
    }

    [Fact]
    public async Task TestTheGovernanceIndicatorsAreVolumeBesideTheIndex()
    {
        var indicators = await Service.GetGovernanceIndicatorsAsync("org", null, Committee());

        Assert.Equal(30, indicators.ExpiryWindowDays);
        Assert.Equal("requires_m43", indicators.FlagsStateKey);
        Assert.Equal("requires_m45_m46", indicators.GateBStateKey);
    }

    /// <summary>
    /// Finding 5 of the M53 review: the governance indicators are live counts cached for a day. Seeds, at Unit X, one row of
    /// each kind the caller sees (under Unit X) and one it does not (under the enrolment process, reached by an EAV link),
    /// then snapshots the next day so the hidden scenario is the node's.
    /// </summary>
    private DateOnly SeedHiddenGovernanceRows()
    {
        var now = DateTime.UtcNow;
        SeedUnscoped(ctx =>
        {
            var risk = NewRisk(3, Enrolment, "New");
            risk.Entities.Add(ctx.Entities.Find(Enrolment)!);
            ctx.Risks.Add(risk);
            ctx.RiskScorings.Add(new RiskScoring { Id = 3, CalculatedRisk = 7f, ResidualRisk = 7f, ClassicImpact = 4, ScoringMethod = 1 });

            foreach (var (id, riskId, entity) in new[] { (2, 1, UnitX), (3, 3, Enrolment) })
                ctx.RiskAcceptances.Add(new RiskAcceptance
                {
                    Id = id, Name = $"A{id}", RiskId = riskId, EntityId = entity, StartDate = now.AddDays(-20), CreatedAt = now.AddDays(-20),
                    ExpiresAt = now.AddDays(10), Status = RiskAcceptanceStatus.Active, AuthorizingManagerId = 50, CreatedById = 50
                });
            foreach (var (id, entity) in new[] { (1, UnitX), (2, Enrolment) })
                ctx.RiskReviewCampaigns.Add(new RiskReviewCampaign
                {
                    Id = id, EntityId = entity, Name = $"C{id}", PeriodStart = now.AddDays(-60), PeriodEnd = now.AddDays(-30),
                    DueDate = now.AddDays(-5), Status = RiskReviewCampaignStatus.Overdue, CreatedAt = now.AddDays(-60)
                });

            // SLA: met on a visible host, breached on the process's host the partial reader cannot see.
            ctx.Vulnerabilities.Add(Finding(Srv, "nessus", cvss3: 5f, cve: "CVE-2026-0901", sla: now.AddDays(5), entity: UnitX));
            ctx.Vulnerabilities.Add(Finding(ProcessHost, "nessus", cvss3: 5f, cve: "CVE-2026-0902", sla: now.AddDays(-5), entity: Enrolment));
        });
        foreach (var riskId in new[] { 1, 3 })
            SeedUnscoped(ctx =>
            {
                var review = Review(riskId, DateTime.UtcNow.AddDays(-5), Reviewer);
                review.SegregationOverrideReason = "the only reviewer is the owner";
                ctx.MgmtReviews.Add(review);
            });

        Assert.Equal(RiskIndexSnapshotResult.Written, GetService<IRiskIndexSnapshotService>().RunAsync(Now.AddDays(1)).GetAwaiter().GetResult().Result);
        return D.AddDays(1);
    }

    /// <summary>
    /// At a partial node every indicator counts only what the reader's scope shows — acceptances, overrides, campaigns and SLA
    /// — and the day's cache never hands one scope's counts to the other, in either order (S40 §5.2.5, §8.1, §11.3).
    /// </summary>
    [Fact]
    public async Task TestTheGovernanceIndicatorsOfAPartialNodeCountOnlyTheVisible()
    {
        var day = SeedHiddenGovernanceRows();

        var full = await Service.GetGovernanceIndicatorsAsync(Key(UnitX), day, Committee());
        ScopeTo(UnitX, Lab);
        var partial = await Service.GetGovernanceIndicatorsAsync(Key(UnitX), day, Committee());
        ScopeToEverything();
        var again = await Service.GetGovernanceIndicatorsAsync(Key(UnitX), day, Committee());

        Assert.Equal((2, 2, 2), (full.AcceptancesExpiringSoon, full.SegregationOverrides90d, full.OverdueCampaigns));
        Assert.Equal((1, 1, 1), (partial.AcceptancesExpiringSoon, partial.SegregationOverrides90d, partial.OverdueCampaigns));
        Assert.Equal(1.0, partial.SlaCompliance);
        Assert.NotNull(full.SlaCompliance);
        Assert.True(full.SlaCompliance < 1.0, $"full SLA {full.SlaCompliance}");
        Assert.Equal(full, again);
    }

    /// <summary>The inventory of a partial node counts the visible objects only, and its cache entry is the scope's own.</summary>
    [Fact]
    public async Task TestTheInventoryOfAPartialNodeCountsOnlyTheVisible()
    {
        Publish();
        var full = await Service.GetInventoryAsync(Key(UnitX), RiskIndexTabGroup.Devices, null, Committee());
        ScopeTo(UnitX, Lab);
        var partial = await Service.GetInventoryAsync(Key(UnitX), RiskIndexTabGroup.Devices, null, Committee());
        var visible = await Service.GetObjectsAsync(Key(UnitX), null, new RiskIndexObjectQuery(PageSize: 500), Committee());
        ScopeToEverything();
        var again = await Service.GetInventoryAsync(Key(UnitX), RiskIndexTabGroup.Devices, null, Committee());

        Assert.True(partial.InventoryCount < full.InventoryCount, $"{partial.InventoryCount} of {full.InventoryCount}");
        Assert.DoesNotContain(visible.Rows, r => r.Object.Object is { Kind: RiskIndexObjectKind.Host, Ref: "110" });
        Assert.Equal(partial.InventoryCount, visible.Rows.Count(r => r.Category == RiskIndexCategory.Exp &&
            Enum.TryParse<RiskIndexAssetClass>(r.AssetClass, out var c) && RiskIndexTabGroups.Of(c) == RiskIndexTabGroup.Devices));
        Assert.Equal(partial.Overall.Assessed, partial.RDistributionByBand.Values.Sum());
        Assert.Equal(full.InventoryCount, again.InventoryCount);
    }

    [Fact]
    public async Task TestTheContributionsAreTheStoredLargestOnes()
    {
        var contributions = await Service.GetContributionsAsync("org", null, null, Committee());

        Assert.NotEmpty(contributions.Rows);
        Assert.InRange(contributions.EffectiveRegisterWeight, 0, 1);
        Assert.Equal(contributions.Rows.OrderByDescending(r => r.Points).Select(r => r.Points), contributions.Rows.Select(r => r.Points));
    }

    // --- publication ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The committee's user (id 1) with <c>risk_index_approve</c> granted by a role, and the administrator (id 3) with the
    /// flag alone — what <see cref="Committee"/> and <see cref="Admin"/> claim, as the database holds it.
    /// </summary>
    private void SeedPublishers() => SeedUnscoped(ctx =>
    {
        var approve = new Permission { Id = 950, Key = RiskIndexPermissions.Approve, Name = "approve", Description = "" };
        ctx.Roles.Add(new Role { Value = 950, Name = "RiskManager", Permissions = [approve] });
        ctx.Roles.Add(new Role { Value = 951, Name = "Administrator", Admin = true });
        ctx.Users.Add(Person(1, 950, admin: false));
        ctx.Users.Add(Person(3, 951, admin: true));
    });

    private static User Person(int id, int role, bool admin) => new()
    {
        Value = id, Name = $"user {id}", Login = $"u{id}", Enabled = true, Type = "local", Salt = "s",
        Password = System.Text.Encoding.UTF8.GetBytes("p"), Email = $"u{id}@example.invalid", RoleId = role, Admin = admin
    };

    [Fact]
    public async Task TestPublishingNeedsAnApprovedProfileAndApprove()
    {
        SeedPublishers();
        var refusal = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.SetPublicationAsync(RiskIndexPublicationState.Published, Caller()));
        Assert.Equal(403, refusal.Status);

        var published = await Service.SetPublicationAsync(RiskIndexPublicationState.Published, Committee());
        Assert.Equal(RiskIndexPublicationState.Published, published.State);

        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Single(p => p.Id == 1).CommitteeDecisionRef = "");
        var noProfile = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.SetPublicationAsync(RiskIndexPublicationState.Published, Committee()));
        Assert.Equal((409, RiskIndexErrors.NoApprovedProfile), (noProfile.Status, noProfile.Code));
    }

    /// <summary>
    /// Finding 6 of the M53 review: the policy attribute passes the administrator flag, but publishing or suspending is the
    /// committee's decision — the grant re-read from the database, as the approval does (S40 §6.4, §11.4).
    /// </summary>
    [Fact]
    public async Task TestTheAdministratorFlagAloneNeitherPublishesNorSuspends()
    {
        SeedPublishers();
        Assert.True(Admin.Has(RiskIndexPermissions.Approve)); // what the policy attribute sees

        var publish = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.SetPublicationAsync(RiskIndexPublicationState.Published, Admin));
        var suspend = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.SetPublicationAsync(RiskIndexPublicationState.Shadow, Admin));
        var stranger = await Assert.ThrowsAsync<RiskIndexException>(() =>
            Service.SetPublicationAsync(RiskIndexPublicationState.Published, new RiskIndexCaller(404, true, new HashSet<string>())));

        Assert.All(new[] { publish, suspend, stranger },
            r => Assert.Equal((403, RiskIndexErrors.ApproverPermissionNotExplicit), (r.Status, r.Code)));
        Assert.Equal(RiskIndexPublicationState.Shadow, (await Service.GetPublicationAsync()).State);
        Assert.Equal(RiskIndexPublicationState.Published,
            (await Service.SetPublicationAsync(RiskIndexPublicationState.Published, Committee())).State);
    }

    // --- the derived-computation budget (S40 §8.1) -----------------------------------------------------------------------

    [Fact]
    public async Task TestTheBudgetRefusesOverItsLimitWithRetryAfter()
    {
        var budget = new RiskIndexDerivedBudget(perUser: 1, perProcess: 2, TimeSpan.FromSeconds(5));
        var gate = new TaskCompletionSource();
        var running = budget.RunAsync(7, async _ => { await gate.Task; return 1; });

        var busy = await Assert.ThrowsAsync<RiskIndexException>(() => budget.RunAsync(7, _ => Task.FromResult(2)));
        Assert.Equal((503, RiskIndexErrors.DerivedBusy), (busy.Status, busy.Code));
        Assert.NotNull(busy.RetryAfter);
        Assert.Equal(3, await budget.RunAsync(8, _ => Task.FromResult(3))); // another user still has room

        gate.SetResult();
        Assert.Equal(1, await running);
    }

    [Fact]
    public async Task TestTheProcessLimitHoldsAcrossUsers()
    {
        var budget = new RiskIndexDerivedBudget(perUser: 2, perProcess: 1, TimeSpan.FromSeconds(5));
        var gate = new TaskCompletionSource();
        var running = budget.RunAsync(1, async _ => { await gate.Task; return 1; });

        var busy = await Assert.ThrowsAsync<RiskIndexException>(() => budget.RunAsync(2, _ => Task.FromResult(2)));

        Assert.Equal(RiskIndexErrors.DerivedBusy, busy.Code);
        gate.SetResult();
        await running;
        Assert.Equal(2, await budget.RunAsync(2, _ => Task.FromResult(2)));
    }

    [Fact]
    public async Task TestAComputationOverItsTimeIsATimeoutAndKeepsItsSlotUntilItEnds()
    {
        var budget = new RiskIndexDerivedBudget(perUser: 1, perProcess: 1, TimeSpan.FromMilliseconds(100));
        var gate = new TaskCompletionSource();

        var timeout = await Assert.ThrowsAsync<RiskIndexException>(() =>
            budget.RunAsync(1, async _ => { await gate.Task; return 1; }));
        Assert.Equal((503, RiskIndexErrors.DerivedTimeout), (timeout.Status, timeout.Code));

        // Still running: its slot is not released yet.
        await Assert.ThrowsAsync<RiskIndexException>(() => budget.RunAsync(1, _ => Task.FromResult(2)));
        gate.SetResult();
        await Task.Delay(50);
        Assert.Equal(2, await budget.RunAsync(1, _ => Task.FromResult(2)));
    }

    /// <summary>A derived node is computed under the budget; an official overview never is.</summary>
    [Fact]
    public async Task TestADerivedReadGoesThroughTheBudget()
    {
        var budget = new RiskIndexDerivedBudget(perUser: 0, perProcess: 0, TimeSpan.FromSeconds(5));
        var service = new RiskIndexReadService(GetService<ServerServices.Services.IDalService>(), new RiskIndexReadCache(), budget,
            GetService<Serilog.ILogger>(), GetService<ServerServices.Interfaces.IPermissionsService>());

        Assert.NotNull((await service.GetOverviewAsync("org", null, Committee())).Headline);
        var busy = await Assert.ThrowsAsync<RiskIndexException>(() =>
            service.GetOverviewAsync(Key(UnitX) + ";c:devices", null, Committee()));
        Assert.Equal(RiskIndexErrors.DerivedBusy, busy.Code);
    }
}
