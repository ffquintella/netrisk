using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.UiTests.Infrastructure;
using Material.Icons;
using Model.Authentication;
using Model.DecisionCycle;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// The two toolbars of <c>RiskView</c>. The bar under the list writes the risk record, so each button follows the
/// policy of the API action it ends in, failing closed (T328): Add and Edit — <c>POST</c>/<c>PUT /Risks</c>,
/// <c>RequireSubmitRisk</c>, the <c>submit_risks</c> permission and nothing else; Close — <c>RequireCloseRisk</c>, the
/// Admin role claim or <c>close_risks</c>; Delete — <c>RequireDeleteRisk</c>, the Admin role claim or
/// <c>delete_risk</c>; and none of them for the third line, which the API refuses every write. The lifecycle toolbar
/// above the detail enables each stage action by the selected risk's state (IX-6).
/// </summary>
public class RiskToolbarFlowTest
{
    [UiTheory]
    [InlineData("riskmanagement only", false, false, false)]
    [InlineData("submit_risks", true, false, false)]
    [InlineData("close_risks", false, true, false)]
    [InlineData("delete_risk", false, false, true)]
    [InlineData("every write permission", true, true, true)]
    [InlineData("admin flag", false, true, true)]
    [InlineData("third line", false, false, false)]
    public async Task TestTheRecordButtonsFollowTheApisPolicies(string audience, bool canSubmit, bool canClose,
        bool canDelete)
    {
        Arrange();
        var screen = await RiskScreen.OpenAsync(UserFor(audience));
        await screen.SelectAsync(LeakSubject);

        Assert.True(canSubmit == screen.Bar("BtAddRisk").IsEffectivelyEnabled, audience + ": Add");
        Assert.True(canSubmit == screen.Bar("BtEditRisk").IsEffectivelyEnabled, audience + ": Edit");
        Assert.True(canClose == screen.Bar("BtCloseRisk").IsEffectivelyEnabled, audience + ": Close");
        Assert.True(canClose == screen.Lifecycle(MaterialIconKind.Close).IsEffectivelyEnabled,
            audience + ": lifecycle Close");
        Assert.True(canDelete == screen.Bar("BtDeleteRisk").IsEffectivelyEnabled, audience + ": Delete");

        // Reading is everyone's: search and reload stay available.
        Assert.True(screen.Bar("BtViewFilterRisk").IsEffectivelyEnabled, audience + ": Search");
        Assert.True(screen.Bar("BtReloadRisk").IsEffectivelyEnabled, audience + ": Reload");

        UiHost.AssertClean();
    }

    /// <summary>Reopening deletes the closure — <c>DELETE /Risks/{id}/Closure</c>, <c>RequireCloseRisk</c>.</summary>
    [UiTheory]
    [InlineData("close_risks", true)]
    [InlineData("delete_risk", false)]
    public async Task TestReopenFollowsTheClosePolicy(string permission, bool canReopen)
    {
        Arrange();
        var screen = await RiskScreen.OpenAsync(RiskUsers.With(permission));
        UiInput.Click(screen.Bar("BtClosedFilter"));
        await UiHost.WaitUntilAsync(() => screen.Subjects.Contains(BackupSubject), "the closed risk to be listed");

        await screen.SelectAsync(BackupSubject);

        Assert.Equal(canReopen, screen.Lifecycle(MaterialIconKind.DoorOpen).IsEffectivelyEnabled);

        UiHost.AssertClean();
    }

    /// <summary>
    /// IX-6: a new risk offers planning its mitigation and a first review; a risk with a mitigation offers revising
    /// it; a reviewed one, editing the review; a closed one, only reopening. Nothing selected hides the toolbar.
    /// </summary>
    [UiFact]
    public async Task TestTheLifecycleToolbarFollowsTheSelectedRisksStage()
    {
        Arrange();
        var services = UiServices.Current;
        services.Double<IMitigationService>().GetByRiskIdAsync(Erp).Returns(_ =>
            Task.FromResult<Mitigation?>(new Mitigation { Id = 20, RiskId = Erp, PlanningStrategy = 1, MitigationCost = 2,
                MitigationEffort = 3, CurrentSolution = "", SecurityRequirements = "", SecurityRecommendations = "" }));
        services.Double<IRisksService>().GetRiskLastMgmtReviewAsync(Fraud).Returns(_ =>
            Task.FromResult<MgmtReview?>(new MgmtReview { Id = 30, RiskId = Fraud, Reviewer = RiskUsers.ManagerId,
                Comments = "", ReviewNavigation = new Review { Name = "Aprovar" },
                NextStepNavigation = new NextStep { Name = "Aceitar" } }));

        var screen = await RiskScreen.OpenAsync(RiskUsers.Manager);
        Assert.DoesNotContain(screen.View.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == RiskScreen.Localized("RiskLifecycle") && t.IsEffectivelyVisible);

        await screen.SelectAsync(LeakSubject);
        AssertLifecycle(screen, plan: true, revise: false, addReview: true, editReview: false, close: true, reopen: false);

        await screen.SelectAsync(ErpSubject);
        await UiHost.WaitUntilAsync(() => screen.Lifecycle(MaterialIconKind.Edit).IsEffectivelyEnabled,
            "Revise mitigation to enable once the mitigation has loaded");
        AssertLifecycle(screen, plan: false, revise: true, addReview: true, editReview: false, close: true, reopen: false);

        await screen.SelectAsync(FraudSubject);
        await UiHost.WaitUntilAsync(() => screen.Lifecycle(MaterialIconKind.BookEdit).IsEffectivelyEnabled,
            "Edit review to enable once the review has loaded");
        AssertLifecycle(screen, plan: true, revise: false, addReview: false, editReview: true, close: true, reopen: false);

        UiInput.Click(screen.Bar("BtClosedFilter"));
        await UiHost.WaitUntilAsync(() => screen.Subjects.Contains(BackupSubject), "the closed risk to be listed");
        await screen.SelectAsync(BackupSubject);
        AssertLifecycle(screen, plan: false, revise: false, addReview: false, editReview: false, close: false, reopen: true);

        // The governance record is open to every selected risk.
        Assert.True(screen.Lifecycle(MaterialIconKind.ScaleBalance).IsEffectivelyEnabled);

        UiHost.AssertClean();
    }

    private static void AssertLifecycle(RiskScreen screen, bool plan, bool revise, bool addReview, bool editReview,
        bool close, bool reopen)
    {
        var subject = screen.ViewModel.SelectedRisk!.Subject;
        Assert.True(plan == screen.Lifecycle(MaterialIconKind.Planner).IsEffectivelyEnabled, subject + ": plan mitigation");
        Assert.True(revise == screen.Lifecycle(MaterialIconKind.Edit).IsEffectivelyEnabled, subject + ": revise mitigation");
        Assert.True(addReview == screen.Lifecycle(MaterialIconKind.RateReview).IsEffectivelyEnabled, subject + ": add review");
        Assert.True(editReview == screen.Lifecycle(MaterialIconKind.BookEdit).IsEffectivelyEnabled, subject + ": edit review");
        Assert.True(close == screen.Lifecycle(MaterialIconKind.Close).IsEffectivelyEnabled, subject + ": close");
        Assert.True(reopen == screen.Lifecycle(MaterialIconKind.DoorOpen).IsEffectivelyEnabled, subject + ": reopen");
    }

    private static AuthenticatedUserInfo UserFor(string audience) => audience switch
    {
        "riskmanagement only" => RiskUsers.Reader,
        "submit_risks" or "close_risks" or "delete_risk" => RiskUsers.With(audience),
        "every write permission" => RiskUsers.Manager,
        "admin flag" => Admin(),
        "third line" => ThirdLine(),
        _ => throw new System.ArgumentOutOfRangeException(nameof(audience), audience, null)
    };

    /// <summary>The account's admin flag: the server issues the Admin role claim, but not the submit_risks permission.</summary>
    private static AuthenticatedUserInfo Admin()
    {
        var admin = RiskUsers.With();
        admin.IsAdmin = true;
        return admin;
    }

    /// <summary>The third line holds every write permission and is still refused every write (S50 D10).</summary>
    private static AuthenticatedUserInfo ThirdLine() =>
        RiskUsers.With("submit_risks", "close_risks", "delete_risk", ThirdLineAssurance.PermissionKey);
}
