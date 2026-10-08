using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.9 (S50 §4.7, §8 TL1–TL7) — the service-side half of the third line's read-only rule (T198): what others cannot
/// make the third line do. The API refuses the auditor's own writes before any controller runs (API.Tests
/// ThirdLineReadOnlyInventoryTest); these are the paths where someone else names the auditor — as the authorizing manager
/// of an acceptance, a business reviewer, a committee member — and the paths an auditor would reach if a host forgot the
/// API guard. Every one refuses with <c>third_line_cannot_approve</c>, the auditor being an administrator too: like the
/// Track 8 segregation of duties, the administrator flag lifts nothing.
/// </summary>
[TestSubject(typeof(ThirdLineGuard))]
public class ThirdLineGuardInMemoryTest : DecisionCycleTestBase
{
    private IRiskAcceptancesService Acceptances => GetService<IRiskAcceptancesService>();

    private static RiskAcceptanceRequest Acceptance(int? authorizer = null) => new()
    {
        Name = "Exception", BusinessJustification = "Insured.", ExpiresAt = DateTime.UtcNow.AddDays(90),
        AuthorizingManagerId = authorizer
    };

    private static void AssertThirdLine(RuleBrokenException ex) => Assert.Equal(ThirdLineAssurance.CannotApproveRule, ex.RuleName);

    /// <summary>TL1 — a manager naming the auditor as the authorizing manager: refused, nothing written.</summary>
    [Fact]
    public async Task TestTL1_TheAuditorCannotBeNamedTheAuthorizingManager()
    {
        ReviewedRisk(1);

        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Acceptances.CreateAsync(1, Acceptance(authorizer: Auditor), Cro)));

        Assert.Empty(Read(ctx => ctx.RiskAcceptances.ToList()));
    }

    /// <summary>TL2 — the auditor accepting or renewing, administrator or not: refused.</summary>
    [Fact]
    public async Task TestTL2_TheAuditorCannotAcceptNorRenew()
    {
        ReviewedRisk(1);

        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Auditor)));
        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Acceptances.CreateAsync(1, Acceptance(authorizer: Cro), Auditor)));

        var live = await Acceptances.CreateAsync(1, Acceptance(), Cro);
        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.RenewAsync(live.Id, Acceptance(), Auditor)));
        Assert.Single(Read(ctx => ctx.RiskAcceptances.ToList()));
    }

    /// <summary>TL3 — the auditor reviewing a risk or counter-signing a review: refused.</summary>
    [Fact]
    public async Task TestTL3_TheAuditorCannotReviewNorCountersign()
    {
        ReviewedRisk(1);
        var reviews = GetService<IMgmtReviewsService>();

        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() => reviews.CreateReviewAsync(new MgmtReview
        {
            RiskId = 1, Review = 1, NextStep = 2, Comments = "Looks fine.", NextReview = DateOnly.FromDateTime(DateTime.UtcNow)
        }, Auditor)));

        SeedUnscoped(ctx => ctx.MgmtReviews.Single(r => r.RiskId == 1).RequiresCountersignature = true);
        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() =>
            reviews.CountersignAsync(10_001, Auditor)));

        Assert.Single(Read(ctx => ctx.MgmtReviews.ToList()));
        Assert.Null(Read(ctx => ctx.MgmtReviews.Single()).SecondReviewerId);
    }

    /// <summary>TL4 — the auditor cannot be appointed a business reviewer (who decides risks in the portal).</summary>
    [Fact]
    public async Task TestTL4_TheAuditorCannotBeAppointedABusinessReviewer()
    {
        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() =>
            GetService<IEntityRiskReviewersService>().AppointAsync(UnitA, Auditor, isPrimary: true, Cro)));

        Assert.Empty(Read(ctx => ctx.EntityRiskReviewers.ToList()));
    }

    /// <summary>TL5 — the auditor cannot sit on a committee as a voter.</summary>
    [Fact]
    public async Task TestTL5_TheAuditorCannotSitOnACommittee()
    {
        var committees = GetService<IRiskCommitteesService>();
        var id = (await committees.CreateCommitteeAsync(new RiskCommitteeRequest { Name = "C", RequiredApprovals = 2 }, Cro)).Id;

        AssertThirdLine(await Assert.ThrowsAsync<RuleBrokenException>(() => committees.AddMemberAsync(id, Auditor, Cro)));
    }

    /// <summary>TL6 — the permission granted to the user directly, not through the role, marks the third line too.</summary>
    [Fact]
    public async Task TestTL6_ADirectGrantMarksTheThirdLineToo()
    {
        SeedUnscoped(ctx =>
        {
            var user = ctx.Users.Single(u => u.Value == MemberA);
            user.Permissions.Add(ctx.Permissions.Single(p => p.Key == ThirdLineAssurance.PermissionKey));
        });

        await using var db = GetService<ServerServices.Services.IDalService>().GetContext();
        Assert.True(await ThirdLineGuard.IsThirdLineAsync(db, MemberA));
        Assert.True(await ThirdLineGuard.IsThirdLineAsync(db, Auditor));
        Assert.False(await ThirdLineGuard.IsThirdLineAsync(db, Cro));
        Assert.False(await ThirdLineGuard.IsThirdLineAsync(db, 999));
    }

    /// <summary>TL7 — the control: an administrator who is not the third line accepts as before.</summary>
    [Fact]
    public async Task TestTL7_EveryoneElseIsUnaffected()
    {
        ReviewedRisk(1);

        var acceptance = await Acceptances.CreateAsync(1, Acceptance(), Cro);

        Assert.Equal(Cro, acceptance.AuthorizingManagerId);
    }
}
