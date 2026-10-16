using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M53, T247 (S39 §9.1, §9.5; S40 §7.2, §9.3, §12.6): the daily snapshot over the Unit X organization — profile selection,
/// every row type, idempotence, batching, failure, the previous day and each discontinuity marker.
/// </summary>
[TestSubject(typeof(RiskIndexSnapshotService))]
public class RiskIndexSnapshotServiceTest : RiskIndexSeedTestBase
{
    private static readonly DateOnly D = DateOnly.FromDateTime(Now);
    private static readonly DateTime Tomorrow = Now.AddDays(1);
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);

    private IRiskIndexSnapshotService Service => GetService<IRiskIndexSnapshotService>();

    private void SeedProfile(int id, int version, RiskIndexProfileStatus status = RiskIndexProfileStatus.Active,
        DateTime? effectiveFrom = null, DateTime? retiredAt = null, RiskIndexMode mode = RiskIndexMode.Interim,
        string? parameters = null) =>
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(new RiskIndexProfile
        {
            Id = id, Version = version, Name = $"Profile v{version}", Mode = mode, Status = status,
            Parameters = parameters ?? RiskIndexParametersJson.Serialize(new RiskIndexParameters()),
            Justification = "pilot", SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1),
            CommitteeDecisionRef = "CRTI-2026-09", EffectiveFrom = effectiveFrom ?? Pilot, RetiredAt = retiredAt,
            CreatedAt = Pilot.AddDays(-10)
        }));

    private RiskIndexSnapshot Headline(DateOnly date, string scopeRef)
    {
        using var db = OpenContext();
        return db.RiskIndexSnapshots.AsNoTracking()
            .Single(s => s.SnapshotDate == date && s.ScopeRef == scopeRef && s.Category == RiskIndexCategory.Icr);
    }

    private static IReadOnlyList<RiskIndexMarkerKind> Markers(RiskIndexSnapshot row) =>
        RiskIndexStoredJson.ParseMarkers(row.Markers).Select(m => m.Kind).ToList();

    // --- the instant and the profile -------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-10-05T05:00:00Z", "2026-10-05T05:00:00Z")]
    [InlineData("2026-10-05T23:59:00Z", "2026-10-05T05:00:00Z")]
    [InlineData("2026-10-05T04:59:59Z", "2026-10-04T05:00:00Z")]
    public void TestTheSnapshotInstantIsTheLatestFiveOClockUtc(string now, string expected)
    {
        var instant = RiskIndexSnapshotService.SnapshotInstant(DateTime.Parse(now).ToUniversalTime());

        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), instant);
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    [Fact]
    public async Task TestWithNoProfileInForceNothingIsWrittenAndNothingFails()
    {
        var outcome = await Service.RunAsync(Now);

        Assert.Equal(RiskIndexSnapshotResult.NoProfileInForce, outcome.Result);
        Assert.Equal(D, outcome.Date);
        await using var db = OpenContext();
        Assert.Empty(db.RiskIndexSnapshots);
        Assert.Empty(db.RiskIndexObjectDays);
        Assert.DoesNotContain(db.IntegrationSyncLogs, l => l.Integration == IntegrationKind.RiskIndex);
    }

    [Fact]
    public async Task TestOnlyAnApprovedProfileStartedBeforeTheInstantIsInForce()
    {
        SeedProfile(1, 1, RiskIndexProfileStatus.Draft);
        SeedProfile(2, 2, RiskIndexProfileStatus.PendingApproval);
        SeedProfile(3, 3, RiskIndexProfileStatus.Rejected);
        SeedProfile(4, 4, effectiveFrom: Now.AddMinutes(1));
        SeedProfile(5, 5, RiskIndexProfileStatus.Retired, retiredAt: Now);

        await using (var db = OpenContext())
            Assert.Null(await RiskIndexSnapshotService.ProfileInForceAsync(db, Now));

        // Retired after the instant: it still governed the day (a successor starts at the next snapshot).
        SeedProfile(6, 6, RiskIndexProfileStatus.Retired, retiredAt: Now.AddHours(3));
        await using (var db = OpenContext())
            Assert.Equal(6, (await RiskIndexSnapshotService.ProfileInForceAsync(db, Now))!.Id);

        // Two in force (a successor started exactly at the instant): the latest start wins.
        SeedProfile(7, 7, effectiveFrom: Now);
        await using (var db = OpenContext())
            Assert.Equal(7, (await RiskIndexSnapshotService.ProfileInForceAsync(db, Now))!.Id);
    }

    /// <summary>S40 §12.6: after a suspension (Retire without successor) the next run writes nothing.</summary>
    [Fact]
    public async Task TestASuspendedProfileStopsAtTheNextSnapshot()
    {
        SeedProfile(1, 1);
        Assert.Equal(RiskIndexSnapshotResult.Written, (await Service.RunAsync(Now)).Result);

        SeedUnscoped(ctx =>
        {
            var profile = ctx.RiskIndexProfiles.Single(p => p.Id == 1);
            profile.Status = RiskIndexProfileStatus.Retired;
            profile.RetiredAt = Now.AddHours(6);
            profile.RetireJustification = "suspended by the committee";
        });

        var outcome = await Service.RunAsync(Tomorrow);

        Assert.Equal(RiskIndexSnapshotResult.NoProfileInForce, outcome.Result);
        await using var db = OpenContext();
        Assert.DoesNotContain(db.RiskIndexSnapshots, s => s.SnapshotDate == D.AddDays(1));
        Assert.Contains(db.RiskIndexSnapshots, s => s.SnapshotDate == D);
    }

    // --- what one run writes ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRunWritesEveryKindOfRowAndLogsIt()
    {
        SeedProfile(1, 1);

        var outcome = await Service.RunAsync(Now);

        Assert.Equal(RiskIndexSnapshotResult.Written, outcome.Result);
        var counts = outcome.Counts!;
        await using var db = OpenContext();

        // Seven rows per pre-computed node; "Unassigned" is derived on read, never stored.
        var snapshots = db.RiskIndexSnapshots.AsNoTracking().Where(s => s.SnapshotDate == D).ToList();
        Assert.Equal(counts.Snapshots, snapshots.Count);
        Assert.All(snapshots.GroupBy(s => (s.ScopeKind, s.ScopeRef)), g => Assert.Equal(7, g.Count()));
        Assert.DoesNotContain(snapshots, s => s.ScopeRef == NodeMembership.UnassignedRef);
        Assert.Contains(snapshots, s => s.ScopeKind == RiskIndexScopeKind.AssetClass && s.EntityId == null);
        Assert.Contains(snapshots, s => s.ScopeKind == RiskIndexScopeKind.Source && s.EntityId == null);
        Assert.All(snapshots.Where(s => s.ScopeKind == RiskIndexScopeKind.Entity), s => Assert.NotNull(s.EntityId));

        var root = snapshots.Single(s => s.ScopeKind == RiskIndexScopeKind.Organization && s.Category == RiskIndexCategory.Icr);
        Assert.Null(root.EntityId);
        Assert.NotNull(root.DisplayState);
        Assert.Null(root.CategoryState);
        Assert.NotNull(root.TopContributions);
        Assert.Null(root.ChangeAttribution);
        Assert.Null(root.Markers);
        Assert.Equal(64, root.ExternalInputsHash.Length);
        Assert.Contains(RiskIndexStoredJson.ParseChips(root.Chips), c => c.Key == RiskIndexChipKeys.DataQuality);
        Assert.All(snapshots.Where(s => s.Category != RiskIndexCategory.Icr), s =>
        {
            Assert.Null(s.DisplayState);
            Assert.NotNull(s.CategoryState);
            Assert.Null(s.TopContributions);
        });
        Assert.Equal(RiskIndexCategoryState.Disabled,
            snapshots.Single(s => s.ScopeKind == RiskIndexScopeKind.Organization && s.Category == RiskIndexCategory.Ter).CategoryState);

        // The object rows carry the engine's inputs, so tomorrow rebuilds today from them alone.
        var objects = db.RiskIndexObjectDays.AsNoTracking().Where(o => o.SnapshotDate == D).ToList();
        Assert.Equal(counts.ObjectDays, objects.Count);
        var appExp = objects.Single(o => o.ObjectKind == RiskIndexObjectKind.Application && o.ObjectRef == Portal.ToString()
                                         && o.Category == RiskIndexCategory.Exp);
        Assert.Equal(0.928, appExp.RValue, 3);
        Assert.Equal(RiskIndexAssetClass.Application, appExp.AssetClass);
        Assert.Contains(Portal, RiskIndexStoredJson.ParseAttribution(appExp.Attribution));
        Assert.NotEmpty(RiskIndexStoredJson.Deserialize<List<StoredFactor>>(appExp.Factors)!);

        // Attributions: one row per object and entity; an object attributed to none is "Unassigned" (null).
        var attributions = db.RiskIndexObjectAttributions.AsNoTracking().Where(a => a.SnapshotDate == D).ToList();
        Assert.Equal(counts.Attributions, attributions.Count);
        Assert.Contains(attributions, a => a.AttributedEntityId == null);
        Assert.Contains(attributions, a => a.ObjectKind == RiskIndexObjectKind.Scenario && a.ObjectRef == "1"
                                           && a.AttributedEntityId == Enrolment && a.ScopeEntityId == UnitX);

        // Node members: the closure, and the scope entity of each attributed object — the risk of Unit X attributed to
        // the enrolment process makes Unit X a scope member of the process node.
        var members = db.RiskIndexNodeMembers.AsNoTracking().Where(m => m.SnapshotDate == D).ToList();
        Assert.Equal(counts.NodeMembers, members.Count);
        Assert.Contains(members, m => m.NodeEntityId == UnitX && m.MemberKind == RiskIndexNodeMemberKind.Closure && m.MemberEntityId == Lab);
        Assert.Contains(members, m => m.NodeEntityId == Enrolment && m.MemberKind == RiskIndexNodeMemberKind.Scope && m.MemberEntityId == UnitX);
        Assert.DoesNotContain(members, m => m.MemberKind == RiskIndexNodeMemberKind.Closure && m.MemberEntityId == null);

        // Class days: the root and the nodes ∩ each tab; an interim process has no host EXP, only its applications'.
        var classDays = db.RiskIndexClassDays.AsNoTracking().Where(c => c.SnapshotDate == D).ToList();
        Assert.Equal(counts.ClassDays, classDays.Count);
        Assert.Contains(classDays, c => c.NodeEntityId == null && c.TabGroup == RiskIndexTabGroup.Devices && c.Category == RiskIndexCategory.Exp);
        Assert.Contains(classDays, c => c.NodeEntityId == UnitX && c.TabGroup == RiskIndexTabGroup.Applications && c.Category == RiskIndexCategory.Exp);
        Assert.DoesNotContain(classDays, c => c.NodeEntityId == Enrolment && c.TabGroup == RiskIndexTabGroup.Devices && c.Category == RiskIndexCategory.Exp);
        Assert.Contains(classDays, c => c.NodeEntityId == Enrolment && c.TabGroup == RiskIndexTabGroup.Applications && c.Category == RiskIndexCategory.Exp);
        Assert.DoesNotContain(classDays, c => c.Category is RiskIndexCategory.Reg or RiskIndexCategory.Ame);

        var log = Assert.Single(db.IntegrationSyncLogs, l => l.Integration == IntegrationKind.RiskIndex);
        Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
        Assert.Equal(1, log.ConnectionId);
        Assert.Equal(counts.Total, log.CreatedCount);
        Assert.NotNull(log.FinishedAt);
    }

    [Fact]
    public async Task TestASecondRunOfTheSameDayWritesNothing()
    {
        SeedProfile(1, 1);
        var first = await Service.RunAsync(Now);

        var second = await Service.RunAsync(Now.AddHours(3));

        Assert.Equal(RiskIndexSnapshotResult.AlreadyComputed, second.Result);
        await using var db = OpenContext();
        Assert.Equal(first.Counts!.Snapshots, db.RiskIndexSnapshots.Count());
        Assert.Equal(first.Counts.ObjectDays, db.RiskIndexObjectDays.Count());
        Assert.Equal(first.Counts.NodeMembers, db.RiskIndexNodeMembers.Count());
        Assert.Single(db.IntegrationSyncLogs, l => l.Integration == IntegrationKind.RiskIndex);
    }

    [Fact]
    public async Task TestTheWriteIsBatched()
    {
        SeedProfile(1, 1);
        var service = new RiskIndexSnapshotService(GetService<IDalService>(), GetService<IRiskIndexDayCalculator>(),
            GetService<Serilog.ILogger>()) { BatchSize = 10 };
        var before = SaveChangesCount;

        var outcome = await service.RunAsync(Now);

        var counts = outcome.Counts!;
        var batches = new[] { counts.Snapshots, counts.ObjectDays, counts.Attributions, counts.NodeMembers, counts.ClassDays }
            .Sum(n => (n + 9) / 10);
        Assert.True(SaveChangesCount - before >= batches, $"{SaveChangesCount - before} saves for {batches} batches");
        await using var db = OpenContext();
        Assert.Equal(counts.ObjectDays, db.RiskIndexObjectDays.Count());
        Assert.Equal(counts.ObjectDays, db.RiskIndexObjectDays.Select(o => new { o.ObjectKind, o.ObjectRef, o.Category }).Distinct().Count());
    }

    [Fact]
    public async Task TestAFailedDayWritesNothingAndIsLoggedFailed()
    {
        SeedProfile(1, 1, parameters: "{ not json");

        var outcome = await Service.RunAsync(Now);

        Assert.Equal(RiskIndexSnapshotResult.Failed, outcome.Result);
        Assert.NotNull(outcome.Error);
        await using var db = OpenContext();
        Assert.Empty(db.RiskIndexSnapshots);
        Assert.Empty(db.RiskIndexObjectDays);
        Assert.Empty(db.RiskIndexNodeMembers);
        var log = Assert.Single(db.IntegrationSyncLogs, l => l.Integration == IntegrationKind.RiskIndex);
        Assert.Equal(IntegrationSyncStatus.Failed, log.Status);
        Assert.False(string.IsNullOrEmpty(log.ErrorMessage));
        Assert.Equal(0, log.CreatedCount);
    }

    // --- the previous day and the markers ---------------------------------------------------------------------------

    [Fact]
    public async Task TestTheNextDayIsAttributedAgainstThePreviousSnapshotRebuiltFromItsRows()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);
        var yesterday = Headline(D, NodeMembership.RootRef);

        Assert.Equal(RiskIndexSnapshotResult.Written, (await Service.RunAsync(Tomorrow)).Result);

        var today = Headline(D.AddDays(1), NodeMembership.RootRef);
        var change = RiskIndexStoredJson.Deserialize<StoredChangeAttribution>(today.ChangeAttribution)!;
        Assert.Equal(D, change.ComparedWith);
        // The rebuilt D−1 differs from the published one only by the stored weight's four decimal places.
        Assert.Equal(yesterday.ValueUnfloored, change.Before!.Value, 2);
        Assert.Equal(today.ValueUnfloored, change.After!.Value, 6);
        Assert.Null(today.Markers);
    }

    /// <summary>
    /// T250 (S39 §9.4–§9.5; S40 §9.3 step 4) replaces the wave-3 placeholder that left "what moved" empty on a profile
    /// change: the day now carries the bridge, and its change attribution reconciles through <c>A_cfg</c> = Δ of the version.
    /// </summary>
    [Fact]
    public async Task TestANewProfileMarksEveryNodeAndAttributesTheVersionChangeThroughTheBridge()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);
        SeedUnscoped(ctx =>
        {
            var v1 = ctx.RiskIndexProfiles.Single(p => p.Id == 1);
            v1.Status = RiskIndexProfileStatus.Retired;
            v1.RetiredAt = Tomorrow;
        });
        SeedProfile(2, 2, effectiveFrom: Tomorrow, mode: RiskIndexMode.Full);

        var outcome = await Service.RunAsync(Tomorrow);

        Assert.Equal(2, outcome.ProfileId);
        await using var db = OpenContext();
        var headlines = db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate == D.AddDays(1) && s.Category == RiskIndexCategory.Icr).ToList();
        Assert.NotEmpty(headlines);
        var bridges = db.RiskIndexBridges.AsNoTracking().Where(b => b.ToProfileId == 2).ToList();
        Assert.NotEmpty(bridges);
        Assert.All(bridges, b => Assert.Equal((1, D.AddDays(1)), (b.FromProfileId, b.BridgeDate)));
        Assert.All(headlines, h =>
        {
            Assert.Contains(RiskIndexMarkerKind.Profile, Markers(h));
            Assert.Contains(RiskIndexMarkerKind.Mode, Markers(h));
            Assert.Equal(RiskIndexMode.Full, h.Mode);
        });
        var valued = headlines.Where(h => h.DisplayState != RiskIndexDisplayState.NoData).ToList();
        Assert.NotEmpty(valued);
        Assert.All(valued, h =>
        {
            // ΔICR* = A_cfg + the previous version's own change since D−1: the configuration share is the bridge's Δ.
            var bridge = bridges.Single(b => b.ScopeKind == h.ScopeKind && b.ScopeRef == h.ScopeRef && b.Category == RiskIndexCategory.Icr);
            var change = RiskIndexStoredJson.Deserialize<StoredChangeAttribution>(h.ChangeAttribution)!;
            Assert.Equal(bridge.ValueNew - bridge.ValueOld, change.Configuration, 9);
            Assert.Equal(h.ValueUnfloored, change.After!.Value, 9);
            Assert.True(bridge.OverlayPartial); // a mode change is never replayable from stored rows
        });
        var profileMarker = RiskIndexStoredJson.ParseMarkers(headlines[0].Markers).Single(m => m.Kind == RiskIndexMarkerKind.Profile);
        Assert.Equal("1", profileMarker.Args["from_version"]);
        Assert.Equal("2", profileMarker.Args["to_version"]);
    }

    [Fact]
    public async Task TestAChangedExternalInputMarksEveryNode()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);
        SeedUnscoped(ctx => ctx.Settings.Add(new Setting { Name = RiskCalculationService.ResidualStrategySetting, Value = "custom" }));

        await Service.RunAsync(Tomorrow);

        await using var db = OpenContext();
        var headlines = db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate == D.AddDays(1) && s.Category == RiskIndexCategory.Icr).ToList();
        Assert.All(headlines, h => Assert.Equal([RiskIndexMarkerKind.ExternalInput], Markers(h)));
        Assert.NotEqual(Headline(D, NodeMembership.RootRef).ExternalInputsHash, headlines[0].ExternalInputsHash);
    }

    [Fact]
    public async Task TestAnAppetiteChangeMarksTheNodesWhoseClosureHoldsItsEntity()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);
        SeedUnscoped(ctx =>
        {
            ctx.RiskAppetites.Add(new RiskAppetite { Id = 2, EntityId = Lab, MaxAcceptableResidual = 3.0, CreatedAt = Now.AddHours(2) });
            ctx.AuditLogs.Add(new AuditLog
            {
                EntityType = nameof(RiskAppetite), EntityId = 2, Field = nameof(RiskAppetite.MaxAcceptableResidual),
                NewValue = "3", Action = AuditLogAction.Create, Actor = "risk.manager", OccurredAt = Now.AddHours(2)
            });
        });

        await Service.RunAsync(Tomorrow);

        var day = D.AddDays(1);
        Assert.Contains(RiskIndexMarkerKind.Appetite, Markers(Headline(day, NodeMembership.RootRef)));
        Assert.Contains(RiskIndexMarkerKind.Appetite, Markers(Headline(day, UnitX.ToString())));
        Assert.Contains(RiskIndexMarkerKind.Appetite, Markers(Headline(day, Lab.ToString())));
        Assert.DoesNotContain(RiskIndexMarkerKind.Appetite, Markers(Headline(day, Portal.ToString())));
        Assert.DoesNotContain(RiskIndexMarkerKind.Appetite, Markers(Headline(day, Invalid.ToString())));
    }

    [Fact]
    public async Task TestAGlobalAppetiteChangeMarksEveryNode()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);
        SeedUnscoped(ctx => ctx.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(RiskAppetite), EntityId = 1, Field = nameof(RiskAppetite.MaxAcceptableResidual),
            OldValue = "5", NewValue = "4", Action = AuditLogAction.Update, Actor = "risk.manager", OccurredAt = Tomorrow
        }));

        await Service.RunAsync(Tomorrow);

        await using var db = OpenContext();
        Assert.All(db.RiskIndexSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate == D.AddDays(1) && s.Category == RiskIndexCategory.Icr).ToList(),
            h => Assert.Contains(RiskIndexMarkerKind.Appetite, Markers(h)));
    }

    [Fact]
    public async Task TestAChangedClosureMarksOnlyItsNode()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);
        SeedUnscoped(ctx => ctx.Entities.Single(e => e.Id == Lab).Parent = Org);

        await Service.RunAsync(Tomorrow);

        var day = D.AddDays(1);
        var unit = RiskIndexStoredJson.ParseMarkers(Headline(day, UnitX.ToString()).Markers);
        var marker = Assert.Single(unit);
        Assert.Equal(RiskIndexMarkerKind.EntityMap, marker.Kind);
        Assert.Equal(Lab.ToString(), marker.Args["removed"]);
        Assert.Empty(Markers(Headline(day, Org.ToString())));
        Assert.Empty(Markers(Headline(day, Portal.ToString())));
    }

    /// <summary>S39 §9.2: the first snapshot that sees an incident closed freezes its date; reopening releases it.</summary>
    [Fact]
    public async Task TestAClosureDateIsFrozenAndReleasedWhenTheIncidentReopens()
    {
        SeedProfile(1, 1);
        await Service.RunAsync(Now);

        await using (var db = OpenContext())
        {
            var frozen = Assert.Single(db.RiskIndexIncidentClosures);
            Assert.Equal(1, frozen.IncidentId);
            Assert.Equal(Now.AddDays(-20), frozen.ClosedAt);
            Assert.Equal(D, frozen.FrozenOn);
        }

        // An edit after the closure moves LastUpdate; the decay keeps the frozen date.
        SeedUnscoped(ctx => ctx.Incidents.Single(i => i.Id == 1).LastUpdate = Now.AddHours(1));
        var second = await Service.RunAsync(Tomorrow);
        Assert.Equal(0, second.Counts!.ClosuresFrozen);
        await using (var db = OpenContext())
            Assert.Equal(Now.AddDays(-20), Assert.Single(db.RiskIndexIncidentClosures).ClosedAt);

        SeedUnscoped(ctx => ctx.Incidents.Single(i => i.Id == 1).Status = (int)IntStatus.New);
        var third = await Service.RunAsync(Tomorrow.AddDays(1));
        Assert.Equal(1, third.Counts!.ClosuresReleased);
        await using (var db = OpenContext())
            Assert.Empty(db.RiskIndexIncidentClosures);
    }

    // --- the rows, from a hand-built day ------------------------------------------------------------------------------

    /// <summary>An entity node with an object whose source row has no scope entity gets a null scope member (S40 §11.3).</summary>
    [Fact]
    public void TestAnUnscopedObjectIsANullScopeMemberAndNoDataIsStoredAsSuch()
    {
        var node = new RiskIndexNodeDefinition { ScopeKind = RiskIndexScopeKind.Entity, ScopeRef = "10", EntityId = 10 };
        var day = new RiskIndexDay
        {
            Nodes =
            [
                new RiskIndexNodeDay
                {
                    Node = node,
                    Members = new NodeMembers
                    {
                        Node = node, Closure = new HashSet<int> { 10 }, ScopeEntities = new HashSet<int> { 10 },
                        HasUnscopedObjects = true
                    },
                    Result = new IcrNodeResult { DisplayState = IcrDisplayState.NoData }
                }
            ]
        };
        var profile = new RiskIndexProfile { Id = 1, Version = 1, Name = "v1", Parameters = "{}" };

        var rows = RiskIndexSnapshotService.BuildRows(day, profile, new RiskIndexParameters(), D, new string('A', 64), null, []);

        Assert.Contains(rows.NodeMembers, m => m.MemberKind == RiskIndexNodeMemberKind.Scope && m.MemberEntityId == null);
        Assert.Contains(rows.NodeMembers, m => m.MemberKind == RiskIndexNodeMemberKind.Closure && m.MemberEntityId == 10);
        var headline = Assert.Single(rows.Snapshots);
        Assert.Equal(RiskIndexDisplayState.NoData, headline.DisplayState);
        Assert.Equal(0, headline.ValueDisplayed);
        Assert.Null(headline.IntervalLow);
    }

    /// <summary>The stored states are the engine's, value for value: the cast in the writer depends on it.</summary>
    [Fact]
    public void TestTheStoredStatesMatchTheEngineValueForValue()
    {
        Assert.Equal(Enum.GetValues<IcrDisplayState>().Select(v => (int)v), Enum.GetValues<RiskIndexDisplayState>().Select(v => (int)v));
        Assert.Equal((int)IcrDisplayState.WithInterval, (int)RiskIndexDisplayState.PointWithInterval);
        Assert.Equal((int)IcrDisplayState.NoData, (int)RiskIndexDisplayState.NoData);
        Assert.Equal(Enum.GetNames<IcrCategoryStatus>(), Enum.GetNames<RiskIndexCategoryState>());
        Assert.Equal(Enum.GetValues<IcrCategoryStatus>().Select(v => (int)v), Enum.GetValues<RiskIndexCategoryState>().Select(v => (int)v));
    }
}
