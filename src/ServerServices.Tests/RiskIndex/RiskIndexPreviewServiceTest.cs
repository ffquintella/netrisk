using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Profiles;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M53, T250 (S39 §13.2; S40 §6.3, §9.6, §12.6 <c>RiskIndexPreviewJobTest</c>): the queue — oldest first, claimed once,
/// abandoned runs requeued by heartbeat — and the report over the Unit X organization: the draft against the profile in
/// force on the latest stored day, the overlay of the stored days, and the report kept off the profile row.
/// </summary>
[TestSubject(typeof(RiskIndexPreviewService))]
public class RiskIndexPreviewServiceTest : RiskIndexSeedTestBase
{
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);

    private RiskIndexPreviewService Service => (RiskIndexPreviewService)GetService<IRiskIndexPreviewService>();

    private void SeedActive()
    {
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Add(new RiskIndexProfile
        {
            Id = 1, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
            Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), Justification = "pilot",
            SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
            EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
        }));
    }

    private int SeedDraft(RiskIndexParameters parameters, DateTime? requested = null, RiskIndexPreviewStatus status = RiskIndexPreviewStatus.Queued,
        DateTime? heartbeat = null)
    {
        var json = RiskIndexParametersJson.Serialize(parameters);
        var id = 0;
        SeedUnscoped(ctx =>
        {
            var draft = ctx.RiskIndexProfiles.FirstOrDefault(p => p.Id == 2);
            if (draft is null)
            {
                draft = new RiskIndexProfile
                {
                    Id = 2, Version = 2, Name = "Draft", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Draft,
                    Parameters = json, CreatedAt = Now
                };
                ctx.RiskIndexProfiles.Add(draft);
            }

            var request = new RiskIndexPreviewRequest
            {
                ProfileId = 2, ParametersHash = RiskIndexPreviewService.ParametersHash(RiskIndexMode.Interim, json), Status = status,
                CreatedAt = requested ?? Now, StartedAt = heartbeat, HeartbeatAt = heartbeat
            };
            ctx.RiskIndexPreviewRequests.Add(request);
            ctx.SaveChanges();
            id = request.Id;
        });
        return id;
    }

    private RiskIndexPreviewRequest Request(int id)
    {
        using var db = OpenContext();
        return db.RiskIndexPreviewRequests.AsNoTracking().Single(r => r.Id == id);
    }

    [Fact]
    public async Task TestAnEmptyQueueIsIdle() =>
        Assert.Equal(RiskIndexPreviewRunResult.Idle, await Service.ProcessNextAsync(Now));

    /// <summary>The atomic claim: a request already claimed by another run is not computed again.</summary>
    [Fact]
    public async Task TestARequestIsClaimedOnce()
    {
        var id = SeedDraft(new RiskIndexParameters());

        Assert.True(await Service.ClaimAsync(id, Now, default));
        Assert.False(await Service.ClaimAsync(id, Now, default));
        Assert.Equal(RiskIndexPreviewStatus.Running, Request(id).Status);
        Assert.Equal(RiskIndexPreviewRunResult.Idle, await Service.ProcessNextAsync(Now));
    }

    [Fact]
    public async Task TestTheOldestQueuedRequestGoesFirst()
    {
        var newer = SeedDraft(new RiskIndexParameters(), Now.AddMinutes(5));
        var older = SeedDraft(new RiskIndexParameters(), Now.AddMinutes(-5));

        await Service.ProcessNextAsync(Now);

        Assert.Equal(RiskIndexPreviewStatus.Completed, Request(older).Status);
        Assert.Equal(RiskIndexPreviewStatus.Queued, Request(newer).Status);
    }

    /// <summary>A Running request silent for 15 minutes was abandoned (a restart); one with a recent heartbeat was not.</summary>
    [Fact]
    public async Task TestOnlyAnAbandonedRunIsRequeued()
    {
        var now = DateTime.UtcNow;
        var abandoned = SeedDraft(new RiskIndexParameters(), now.AddHours(-1), RiskIndexPreviewStatus.Running, now.AddMinutes(-16));
        var alive = SeedDraft(new RiskIndexParameters(), now.AddHours(-1), RiskIndexPreviewStatus.Running, now.AddMinutes(-2));

        await Service.ProcessNextAsync(now);

        Assert.Equal(RiskIndexPreviewStatus.Completed, Request(abandoned).Status);
        Assert.Equal(RiskIndexPreviewStatus.Running, Request(alive).Status);
    }

    [Fact]
    public async Task TestParametersChangedDuringThePreviewFailIt()
    {
        var id = SeedDraft(new RiskIndexParameters());
        SeedUnscoped(ctx => ctx.RiskIndexProfiles.Single(p => p.Id == 2).Parameters =
            RiskIndexParametersJson.Serialize(new RiskIndexParameters { P = 6 }));

        Assert.Equal(RiskIndexPreviewRunResult.Failed, await Service.ProcessNextAsync(Now));

        var request = Request(id);
        Assert.Equal(RiskIndexPreviewStatus.Failed, request.Status);
        Assert.Contains("changed", request.ErrorMessage);
    }

    /// <summary>The first profile of all (S39 §13.2): today under the draft, no Δ against a profile in force, no τ.</summary>
    [Fact]
    public async Task TestTheFirstProfilesPreviewCoversTodayOnly()
    {
        var id = SeedDraft(RiskIndexPresets.Parameters(RiskIndexPresets.Conservative));

        Assert.Equal(RiskIndexPreviewRunResult.Completed, await Service.ProcessNextAsync(DateTime.UtcNow));

        var report = JsonSerializer.Deserialize<RiskIndexPreviewDto>(Request(id).Report!, RiskIndexPreviewService.ReportJson)!;
        Assert.NotEmpty(report.Nodes);
        Assert.All(report.Nodes, n => Assert.Null(n.Active));
        Assert.Null(report.KendallTau);
        Assert.Null(report.Overlay);
        Assert.NotEmpty(report.Tornado);
        Assert.Equal(4, report.Presets.Count);
        await using var db = OpenContext();
        var draft = db.RiskIndexProfiles.AsNoTracking().Single(p => p.Id == 2);
        Assert.Null(draft.SensitivityReport); // the report lives on the request, never on the profile (S40 §9.6)
        Assert.Equal(0, draft.Revision);
    }

    /// <summary>Against the profile in force: Δ per node on the latest stored day, and the overlay of the stored days.</summary>
    [Fact]
    public async Task TestThePreviewComparesTheDraftWithTheProfileInForce()
    {
        SeedActive();
        var snapshots = GetService<IRiskIndexSnapshotService>();
        await snapshots.RunAsync(Now.AddDays(-1));
        await snapshots.RunAsync(Now);
        var draft = new RiskIndexParameters { TauG = 0.95 };
        var id = SeedDraft(draft);

        Assert.Equal(RiskIndexPreviewRunResult.Completed, await Service.ProcessNextAsync(Now.AddHours(1)));

        var report = JsonSerializer.Deserialize<RiskIndexPreviewDto>(Request(id).Report!, RiskIndexPreviewService.ReportJson)!;
        var root = report.Nodes.Single(n => n.Node.Key == "org");
        Assert.NotNull(root.Active);
        Assert.True(root.Draft >= root.Active!.Value);
        Assert.Equal(2, report.Overlay!.Points.Count);
        Assert.False(report.OverlayPartial); // τ_G is replayable from the stored rows
        Assert.InRange(report.RegisterWeightActive, 0, 1);
        Assert.Contains(report.Tornado, t => t.ParameterKey == "tau_g");
    }
}
