using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Model.RiskFlags;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.5 (S46 §4.7, §8 G1–G10; T170, T173) — Gate A through the real acceptance, renewal, campaign,
/// closure and deletion paths.
///
/// The edge case the methodology names is the order: a non-discretionary flag refuses the acceptance even when
/// the appetite would allow it, and when both would refuse, the refusal is Gate A's — "Gate A precedes Gate B,
/// and the order is the test". G1 and G3 together are that assertion: the same risk above the appetite ceiling
/// is refused by the appetite without the flag and by Gate A with it.
/// </summary>
[TestSubject(typeof(RiskAcceptancesService))]
public class GateAInMemoryTest : RiskChainTestBase
{
    private const int Cro = 1;
    private const int Owner = 2;
    private const int RiskId = 1;
    private const string Log4Shell = "CVE-2021-44228";

    private IRiskAcceptancesService Acceptances => GetService<IRiskAcceptancesService>();
    private IRiskFlagsService Flags => GetService<IRiskFlagsService>();

    public GateAInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(NewUser(Cro, "cro", admin: true));
            ctx.Users.Add(NewUser(Owner, "owner"));
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.BreakGlassSetting, Value = "true" });
            // Gate B: the organisation-wide appetite. Residual above 6 cannot be accepted.
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 5, CreatedAt = DateTime.UtcNow
            });
        });
    }

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin, Type = "local", Salt = "s",
        Password = System.Text.Encoding.UTF8.GetBytes("p"), Email = $"{name}@example.test"
    };

    /// <summary>Risk 1 in unit A, raised, owned and managed by <see cref="Owner"/>, at the given residual.</summary>
    private void SeedRisk(float residual)
    {
        AddRisk(RiskId, UnitA, score: 9f);
        SeedUnscoped(ctx =>
        {
            var risk = ctx.Risks.Single(r => r.Id == RiskId);
            risk.Owner = Owner;
            risk.Manager = Owner;
            risk.SubmittedBy = Owner;
            ctx.RiskScorings.Single(s => s.Id == RiskId).ResidualRisk = residual;
        });
    }

    private Task DeclareHumanSafetyAsync() => Flags.DeclareAsync(RiskId, RiskFlagCode.HumanSafety,
        new RiskFlagDeclarationRequest { Reason = "The pump controller doses patients." }, Author);

    private static RiskAcceptanceRequest Request(string? overrideReason = null) => new()
    {
        Name = "Exception", BusinessJustification = "Compensating monitoring in place.",
        ExpiresAt = DateTime.UtcNow.AddDays(90), SegregationOverrideReason = overrideReason
    };

    private int Acceptances_Count() => Read(ctx => ctx.RiskAcceptances.Count());

    /// <summary>
    /// G1 (T173) — flag 1 on a risk whose residual is also above the appetite ceiling: both gates would refuse,
    /// and the refusal is Gate A's. The order is the assertion.
    /// </summary>
    [Fact]
    public async Task TestG1_WhenBothGatesRefuseTheRefusalIsGateA()
    {
        SeedRisk(residual: 8f);
        await DeclareHumanSafetyAsync();

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(RiskId, Request(), Cro));

        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.NotEqual("risk_appetite_ceiling", ex.RuleName);
        Assert.Contains("flag 1", ex.Message);
        Assert.Contains("before the risk appetite", ex.Message);
        Assert.Equal(0, Acceptances_Count());
    }

    /// <summary>G2 (T173) — flag 1 refuses the acceptance even though the appetite would allow it.</summary>
    [Fact]
    public async Task TestG2_GateARefusesEvenWithinAppetite()
    {
        SeedRisk(residual: 3f);
        Assert.False((await GetService<IRiskWorkflowService>().EvaluateAppetiteAsync(RiskId)).ExceedsCeiling);
        await DeclareHumanSafetyAsync();

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(RiskId, Request(), Cro));

        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Equal(0, Acceptances_Count());
    }

    /// <summary>
    /// G3 — the control for G1 and G2: without the flag the same fixture is refused by the appetite above the
    /// ceiling and accepted within it, so Gate B is live and it is Gate A that made the difference.
    /// </summary>
    [Theory]
    [InlineData(8f, false)]
    [InlineData(3f, true)]
    public async Task TestG3_WithoutTheFlagTheAppetiteDecides(float residual, bool accepted)
    {
        SeedRisk(residual);

        if (accepted)
        {
            Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(RiskId, Request(), Cro)).Status);
            return;
        }

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(RiskId, Request(), Cro));
        Assert.Equal("risk_appetite_ceiling", ex.RuleName);
    }

    /// <summary>G4 — Gate A precedes segregation of duties too: the owner is told nobody may accept, not that they may not.</summary>
    [Fact]
    public async Task TestG4_GateAPrecedesSegregationOfDuties()
    {
        SeedRisk(residual: 3f);

        var withoutFlag = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Acceptances.CreateAsync(RiskId, Request(), Owner));
        Assert.Equal("segregation_of_duties", withoutFlag.RuleName);

        await DeclareHumanSafetyAsync();

        var withFlag = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Acceptances.CreateAsync(RiskId, Request(), Owner));
        Assert.Equal(RiskFlagsService.GateARule, withFlag.RuleName);
    }

    /// <summary>G5 — a renewal is a new acceptance: Gate A refuses it, and the live acceptance is left as it was.</summary>
    [Fact]
    public async Task TestG5_ARenewalIsRefused()
    {
        SeedRisk(residual: 3f);
        var live = await Acceptances.CreateAsync(RiskId, Request(), Cro);
        await DeclareHumanSafetyAsync();

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.RenewAsync(live.Id, Request(), Cro));

        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Equal(RiskAcceptanceStatus.Active, Read(ctx => ctx.RiskAcceptances.Single(a => a.Id == live.Id)).Status);
        Assert.Equal(1, Acceptances_Count());
    }

    /// <summary>G6 — a business reviewer accepting through a campaign (the portal's path) is refused the same way.</summary>
    [Fact]
    public async Task TestG6_ACampaignAcceptanceIsRefused()
    {
        SeedRisk(residual: 3f);
        SeedUnscoped(ctx =>
        {
            ctx.RiskReviewCampaigns.Add(new RiskReviewCampaign
            {
                Id = 1, EntityId = UnitA, Name = "Q4", PeriodStart = DateTime.UtcNow.AddDays(-10),
                PeriodEnd = DateTime.UtcNow.AddDays(80), DueDate = DateTime.UtcNow.AddDays(20), CreatedAt = DateTime.UtcNow
            });
            ctx.RiskReviewCampaignItems.Add(new RiskReviewCampaignItem
                { Id = 1, CampaignId = 1, RiskId = RiskId, CreatedAt = DateTime.UtcNow });
        });
        await DeclareHumanSafetyAsync();

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => GetService<IRiskReviewCampaignsService>()
            .DecideAsync(1, 1, new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Accepted, Acceptance = Request()
            }, Cro));

        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Equal(RiskReviewDecision.Pending, Read(ctx => ctx.RiskReviewCampaignItems.Single()).Decision);
        Assert.Equal(0, Acceptances_Count());
    }

    /// <summary>
    /// G7 — closing is refused, even with the state machine switched off: a configuration row may waive the
    /// workflow rules, not a non-discretionary condition. Without the flag the same close goes through.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestG7_ClosingIsRefusedEvenWithTheStateMachineOff(bool flagged)
    {
        SeedRisk(residual: 3f);
        SeedUnscoped(ctx => ctx.Settings.Add(new Setting
            { Name = RiskWorkflowService.StateMachineSetting, Value = "false" }));
        if (flagged) await DeclareHumanSafetyAsync();

        var risk = Read(ctx => ctx.Risks.AsNoTracking().Single(r => r.Id == RiskId));
        risk.Status = RiskWorkflowService.StatusClosed;

        if (!flagged)
        {
            await Risks.SaveRiskAsync(risk);
            Assert.Equal("Closed", Read(ctx => ctx.Risks.Single(r => r.Id == RiskId)).Status);
            return;
        }

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Risks.SaveRiskAsync(risk));
        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Contains("cannot be closed", ex.Message);
        Assert.Equal("New", Read(ctx => ctx.Risks.Single(r => r.Id == RiskId)).Status);
    }

    /// <summary>G8 — deleting is refused; without the flag the risk is deleted.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestG8_DeletingIsRefused(bool flagged)
    {
        SeedRisk(residual: 3f);
        if (flagged) await DeclareHumanSafetyAsync();

        if (!flagged)
        {
            Risks.DeleteRisk(RiskId);
            Assert.False(Read(ctx => ctx.Risks.Any(r => r.Id == RiskId)));
            return;
        }

        var ex = Assert.Throws<RuleBrokenException>(() => Risks.DeleteRisk(RiskId));
        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.True(Read(ctx => ctx.Risks.Any(r => r.Id == RiskId)));
    }

    /// <summary>
    /// G9 — the segregation break-glass is on and a reason is given, which would let the owner accept: it does
    /// not carry over to Gate A, which has no break-glass (S46 D8).
    /// </summary>
    [Fact]
    public async Task TestG9_TheSegregationBreakGlassDoesNotOpenGateA()
    {
        SeedRisk(residual: 3f);
        await DeclareHumanSafetyAsync();

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Acceptances.CreateAsync(RiskId, Request(overrideReason: "Emergency: CEO instruction."), Owner));

        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Equal(0, Acceptances_Count());
    }

    /// <summary>
    /// G10 — a derived Gate A is evaluated on freshly reconciled flags: the KEV finding refuses the acceptance
    /// with no explicit refresh, and once the CVE leaves KEV the very same request is accepted.
    /// </summary>
    [Fact]
    public async Task TestG10_ADerivedGateAIsReconciledBeforeEveryDecision()
    {
        SeedRisk(residual: 3f);
        SeedUnscoped(ctx =>
        {
            var finding = new Vulnerability
            {
                Id = 5, Title = "Log4Shell", Cves = Log4Shell, EntityId = UnitA, LifecycleStatus = FindingStatus.Active,
                Status = 1, DetectionCount = 1, FirstDetection = DateTime.UtcNow, LastDetection = DateTime.UtcNow
            };
            ctx.Vulnerabilities.Add(finding);
            ctx.Risks.Single(r => r.Id == RiskId).Vulnerabilities.Add(finding);
            ctx.KevEntries.Add(new KevEntry
                { CveId = Log4Shell, VulnerabilityName = "RCE", DateAdded = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        });

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(RiskId, Request(), Cro));
        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
        Assert.Contains($"KEV {Log4Shell} on finding #5", ex.Message);

        SeedUnscoped(ctx => ctx.KevEntries.Single().DelistedAt = DateTime.UtcNow);

        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(RiskId, Request(), Cro)).Status);
        Assert.False(Read(ctx => ctx.RiskFlags.Single()).Derived);
    }
}
