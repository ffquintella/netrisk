using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.UiTests.Infrastructure;
using Material.Icons;
using Material.Icons.Avalonia;
using Model.Risks.Chain;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// Selecting a risk fills the detail pane of <c>RiskView</c>: the identification block, the structured scenario,
/// the linkage chain, the scores, the mitigation, the last review, the incident response plan, the closure and the
/// paged list of the risk's open findings — each read from the doubles and asserted on the text the pane shows.
/// </summary>
public class RiskDetailFlowTest
{
    [UiFact]
    public async Task TestSelectingANewRiskShowsItsIdentificationScenarioAndScores()
    {
        Arrange();
        var screen = await OpenAsync(RiskUsers.Reader);

        // Nothing selected: no detail, no lifecycle toolbar.
        Assert.False(IsShown(screen.View, Localized("Details")));
        Assert.False(IsShown(screen.View, Localized("RiskLifecycle")));

        await screen.SelectAsync(LeakSubject);

        Assert.True(IsShown(screen.View, Localized("Details")));
        Assert.True(IsShown(screen.View, Localized("RiskLifecycle")));
        Assert.Equal(Leak.ToString(), screen.DetailValue(Localized("Id") + ": "));
        Assert.Equal("CTRL-001", screen.DetailValue(Localized("CtrlNumber") + ": "));
        Assert.Equal("New", screen.DetailValue(Localized("Status") + ": "));
        Assert.Equal(LeakSubject, screen.DetailValue(Localized("Subject") + ": "));
        await UiHost.WaitUntilAsync(() => screen.DetailValue(Localized("Source") + ": ") == "Auditoria interna",
            "the risk's source name to load");
        Assert.Equal("Segurança da informação", screen.DetailValue(Localized("Category") + ": "));
        Assert.Equal("Analista de Riscos", screen.DetailValue(Localized("SubmittedBy") + ":"));
        Assert.Equal("Analista de Riscos", screen.DetailValue(Localized("Owner") + ":"));
        Assert.Equal("Gestora de Riscos", screen.DetailValue(Localized("Manager") + ":"));

        // Stage 9.2: every scenario field is shown; an empty one reads "not informed".
        Assert.Equal("Credenciais vazadas", screen.DetailValue(Localized("ScenarioCause") + ":"));
        Assert.Equal(Localized("ChainNotInformed"), screen.DetailValue(Localized("ScenarioVulnerability") + ":"));
        Assert.Equal("Acesso indevido à base de clientes", screen.DetailValue(Localized("ScenarioCentralEvent") + ":"));

        // The impact types, the scores (likelihood 3 × impact 2 = 6, residual 4.5) and the scale names.
        Assert.Contains("Controle de acesso", screen.DetailTexts);
        Assert.Equal("6,00", screen.DetailValue(Localized("Value") + ":"));
        Assert.Equal("Possível", screen.DetailValue(Localized("Probability") + ":"));
        Assert.Equal("Menor", screen.DetailValue(Localized("Impact") + ":"));
        Assert.Equal("6,00", screen.DetailValue(Localized("TotalScore") + ": "));
        Assert.Equal("4,50", screen.DetailValue(Localized("Residual") + ":"));
        Assert.Equal("1,50", screen.DetailValue(Localized("ResidualDelta") + ":"));

        // No mitigation and no review yet — said, not left blank — and the notes.
        Assert.Contains(Localized("MitigationNotPlannedMSG"), screen.DetailTexts);
        Assert.Contains(Localized("ReviewNotDone"), screen.DetailTexts);
        Assert.Contains(screen.Detail.GetVisualDescendants().OfType<TextBox>(),
            t => t.Text == "Revisar os controles de acesso ao CRM." && t.IsReadOnly);

        UiHost.AssertClean();
    }

    /// <summary>The status reads in the colour of its stage: green for a new risk, orange once management reviewed it.</summary>
    [UiTheory]
    [InlineData(LeakSubject, "New", "#FF90EE90")]
    [InlineData(ErpSubject, "Mitigation Planned", "#FFFFA07A")]
    [InlineData(FraudSubject, "Mgmt Reviewed", "#FFFFA500")]
    public async Task TestTheStatusIsShownInTheColourOfItsStage(string subject, string status, string colour)
    {
        Arrange();
        var screen = await OpenAsync(RiskUsers.Reader);

        await screen.SelectAsync(subject);

        var statusText = screen.Detail.Find<TextBlock>(t => t.Text == status && t.IsEffectivelyVisible);
        Assert.Equal(Color.Parse(colour), Assert.IsAssignableFrom<ISolidColorBrush>(statusText.Foreground).Color);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestAPlannedMitigationAndTheLastReviewAreShownByName()
    {
        Arrange();
        var services = UiServices.Current;
        services.Double<IMitigationService>().GetByRiskIdAsync(Erp).Returns(_ => Task.FromResult<Mitigation?>(
            new Mitigation
            {
                Id = 20, RiskId = Erp, PlanningStrategy = 1, MitigationCost = 2, MitigationEffort = 3,
                MitigationPercent = 40, PlanningDate = new DateOnly(2026, 11, 30), SubmissionDate = new DateTime(2026, 9, 2),
                LastUpdate = new DateTime(2026, 9, 3), CurrentSolution = "", SecurityRequirements = "",
                SecurityRecommendations = ""
            }));
        services.Double<IRisksService>().GetRiskLastMgmtReviewAsync(Fraud).Returns(_ => Task.FromResult<MgmtReview?>(
            new MgmtReview
            {
                Id = 30, RiskId = Fraud, Reviewer = RiskUsers.ManagerId, Comments = "",
                SubmissionDate = new DateTime(2026, 9, 10), NextReview = new DateOnly(2027, 3, 10),
                ReviewNavigation = new Review { Value = 1, Name = "Aprovar" },
                NextStepNavigation = new NextStep { Value = 1, Name = "Aceitar até a próxima revisão" }
            }));

        var screen = await OpenAsync(RiskUsers.Reader);

        await screen.SelectAsync(ErpSubject);
        await UiHost.WaitUntilAsync(() => screen.DetailValue(Localized("Strategy")) == "Mitigar",
            "the mitigation's strategy to be shown by name");
        Assert.Equal("Médio", screen.DetailValue(Localized("Cost")));
        Assert.Equal("Considerável", screen.DetailValue(Localized("Effort")));
        Assert.Equal("40", screen.DetailValue("%"));
        Assert.DoesNotContain(Localized("MitigationNotPlannedMSG"), screen.DetailTexts);

        await screen.SelectAsync(FraudSubject);
        await UiHost.WaitUntilAsync(() => screen.DetailTexts.Contains("Gestora de Riscos"),
            "the last review's reviewer to be shown by name");
        Assert.Equal("Aprovar", screen.DetailValue(Localized("ReviewDecision") + ":"));
        Assert.Equal("Aceitar até a próxima revisão", screen.DetailValue(Localized("NextStep") + ":"));
        Assert.DoesNotContain(Localized("ReviewNotDone"), screen.DetailTexts);

        // Moving on clears what the previous risk showed.
        Assert.Contains(Localized("MitigationNotPlannedMSG"), screen.DetailTexts);

        UiHost.AssertClean();
    }

    /// <summary>A closed risk shows when and why it was closed; an open one does not.</summary>
    [UiFact]
    public async Task TestAClosedRiskShowsItsClosure()
    {
        Arrange();
        var screen = await OpenAsync(RiskUsers.Reader);
        Assert.DoesNotContain(Localized("Closed").ToUpper(), screen.DetailTexts);

        UiInput.Click(screen.Bar("BtClosedFilter"));
        await UiHost.WaitUntilAsync(() => screen.Subjects.Contains(BackupSubject), "the closed risk to be listed");
        await screen.SelectAsync(BackupSubject);

        await UiHost.WaitUntilAsync(() => screen.DetailTexts.Contains("Aceito pela diretoria"),
            "the closure reason to be shown");
        Assert.Contains(Localized("Closed").ToUpper(), screen.DetailTexts);

        UiHost.AssertClean();
    }

    /// <summary>
    /// The incident response plan block: "no plan" and the add button (enabled only with
    /// <c>incident-response-plans</c>), or the plan's date, approval and the view/edit/delete buttons.
    /// </summary>
    [UiFact]
    public async Task TestTheIncidentResponsePlanBlockFollowsThePlanAndThePermissions()
    {
        Arrange();
        UiServices.Current.Double<IRisksService>().GetIncidentResponsePlanAsync(Erp).Returns(_ =>
            Task.FromResult<IncidentResponsePlan?>(new IncidentResponsePlan
            {
                Id = 50, Name = "Plano de contingência do ERP", LastUpdate = new DateTime(2026, 9, 20),
                HasBeenApproved = true
            }));

        var screen = await OpenAsync(RiskUsers.With("incident-response-plans"));

        await screen.SelectAsync(LeakSubject);
        Assert.Contains(Localized("NoIRPFound"), screen.DetailTexts);
        Assert.True(IrpButton(screen, MaterialIconKind.Plus).IsEffectivelyEnabled);

        await screen.SelectAsync(ErpSubject);
        await UiHost.WaitUntilAsync(() => !screen.DetailTexts.Contains(Localized("NoIRPFound")),
            "the plan to replace the \"no plan\" warning");
        Assert.True(IrpIcon(screen, MaterialIconKind.Check).IsEffectivelyVisible);
        Assert.True(IrpButton(screen, MaterialIconKind.Eye).IsEffectivelyEnabled);
        Assert.True(IrpButton(screen, MaterialIconKind.Pencil).IsEffectivelyEnabled);
        // Deleting a plan is irp-delete, which this user does not hold.
        Assert.False(IrpButton(screen, MaterialIconKind.Delete).IsEffectivelyEnabled);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestWithoutThePlanPermissionTheAddPlanButtonIsDisabled()
    {
        Arrange();
        var screen = await OpenAsync(RiskUsers.Reader);

        await screen.SelectAsync(LeakSubject);

        Assert.False(IrpButton(screen, MaterialIconKind.Plus).IsEffectivelyEnabled);

        UiHost.AssertClean();
    }

    /// <summary>
    /// The risk's open findings, ten a page: the grid shows the page, P/T the page and the total, and the arrows page
    /// through — every row with its title, team and analyst, including a finding nobody has been assigned yet.
    /// </summary>
    [UiFact]
    public async Task TestTheOpenFindingsArePagedTenAtATime()
    {
        var server = Arrange();
        var findings = Enumerable.Range(1, 12).Select(i => new Vulnerability
        {
            Id = 100 + i, Title = $"Achado {i:00}", Severity = "3", Score = 7.5, Status = 1,
            FirstDetection = new DateTime(2026, 9, 1), FixTeamId = 5, AnalystId = i == 12 ? null : RiskUsers.AnalystId
        }).ToList();
        server.Risks.GetOpenVulnerabilitiesPageAsync(Leak, Arg.Any<int>(), 10).Returns(call =>
        {
            var page = call.ArgAt<int>(1);
            return Task.FromResult(Tuple.Create(findings.Count, findings.Skip((page - 1) * 10).Take(10).ToList()));
        });

        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(LeakSubject);

        var grid = screen.Detail.Find<DataGrid>(g => g.Name == "VulnerabilitiesDataGrid");
        await UiHost.WaitUntilAsync(() => Rows(grid).Count == 10, "the first page of ten findings");
        Assert.Equal("1", screen.DetailValue("P:"));
        Assert.Equal("12", screen.DetailValue("T:"));
        Assert.Contains("Achado 01", CellTexts(grid));
        Assert.Contains("Infraestrutura", CellTexts(grid));
        Assert.Contains("Alto", CellTexts(grid));
        Assert.Contains(grid.GetVisualDescendants().OfType<MaterialIcon>(), i => i.Kind == MaterialIconKind.NewReleases);

        UiInput.ScrollToAndClick(PageButton(screen, MaterialIconKind.ArrowRight));
        await UiHost.WaitUntilAsync(() => Rows(grid).Count == 2, "the second page of two findings");
        Assert.Equal("2", screen.DetailValue("P:"));
        Assert.Contains("Achado 12", CellTexts(grid));

        // There is no third page.
        UiInput.ScrollToAndClick(PageButton(screen, MaterialIconKind.ArrowRight));
        Assert.Equal("2", screen.DetailValue("P:"));

        UiInput.ScrollToAndClick(PageButton(screen, MaterialIconKind.ArrowLeft));
        await UiHost.WaitUntilAsync(() => Rows(grid).Count == 10, "the first page again");
        Assert.Equal("1", screen.DetailValue("P:"));

        UiHost.AssertClean();
    }

    /// <summary>Stage 9.1: the five levels of the linkage chain, a missing one reading "not informed".</summary>
    [UiFact]
    public async Task TestTheLinkageChainShowsEveryLevel()
    {
        Arrange();
        UiServices.Current.Double<IRiskChainService>().GetRiskChainAsync(Leak).Returns(_ =>
            Task.FromResult(Chain(Leak,
                (RiskChainLevel.Process, "Atendimento ao cliente"), (RiskChainLevel.ItService, "CRM"),
                (RiskChainLevel.ItService, "Portal do cliente"))));

        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(LeakSubject);

        await UiHost.WaitUntilAsync(() => screen.DetailTexts.Contains(Localized("LinkageChain")),
            "the linkage chain block to appear");
        Assert.Equal(Localized("ChainNotInformed"), screen.DetailValue(Localized("ChainLevelObjective") + ":"));
        Assert.Equal("Atendimento ao cliente", screen.DetailValue(Localized("ChainLevelProcess") + ":"));
        Assert.Equal("CRM, Portal do cliente", screen.DetailValue(Localized("ChainLevelItService") + ":"));
        Assert.Equal(Localized("ChainNotInformed"), screen.DetailValue(Localized("ChainLevelAsset") + ":"));

        UiHost.AssertClean();
    }

    /// <summary>
    /// The Track 9 workspace is collapsed by default and loads nothing; expanding it loads its panels for the selected
    /// risk.
    /// </summary>
    [UiFact]
    public async Task TestExpandingTheTrack9WorkspaceLoadsItForTheSelectedRisk()
    {
        Arrange();
        var flags = UiServices.Current.Double<IRiskFlagsService>();
        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(LeakSubject);

        var workspace = screen.Detail.Find<Expander>();
        Assert.False(workspace.IsExpanded);
        await flags.DidNotReceive().GetRiskFlagsAsync(Leak);

        UiInput.ScrollToAndClick(workspace.Find<ToggleButton>());

        Assert.True(workspace.IsExpanded);
        await UiHost.WaitUntilAsync(() => flags.ReceivedCalls().Any(c =>
                c.GetMethodInfo().Name == nameof(IRiskFlagsService.GetRiskFlagsAsync) && (int)c.GetArguments()[0]! == Leak),
            "the workspace to load the selected risk's flags");

        UiHost.AssertClean();
    }

    internal static RiskChainDto Chain(int riskId, params (RiskChainLevel Level, string Name)[] links)
    {
        var id = 900;
        return new RiskChainDto
        {
            RiskId = riskId,
            Levels = Enum.GetValues<RiskChainLevel>().Select(level => new RiskChainLevelDto
            {
                Level = level,
                Links = links.Where(l => l.Level == level).Select(l => new RiskChainLinkDto
                {
                    Id = ++id, RiskId = riskId, Level = level, EntityId = id, TargetName = l.Name,
                    TargetType = "entity", Origin = RiskChainLinkOrigin.Declared, CreatedAt = new DateTime(2026, 9, 1)
                }).ToList()
            }).ToList()
        };
    }

    private static bool IsShown(Avalonia.Visual root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);

    private static List<DataGridRow> Rows(DataGrid grid) =>
        grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible && r.DataContext is Vulnerability)
            .ToList();

    private static List<string> CellTexts(DataGrid grid) =>
        Rows(grid).SelectMany(r => r.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text ?? "").ToList();

    private static Button PageButton(RiskScreen screen, MaterialIconKind icon) =>
        screen.Detail.Find<Button>(b => b.Classes.Contains("operation") &&
                                        b.GetVisualDescendants().OfType<MaterialIcon>().Any(i => i.Kind == icon));

    /// <summary>The incident response plan block: the grid under the "IRP" header.</summary>
    private static Grid IrpBlock(RiskScreen screen) =>
        (Grid)screen.Detail.Find<TextBlock>(t => t.Text == Localized("IRP")).Parent!;

    private static Button IrpButton(RiskScreen screen, MaterialIconKind icon) =>
        IrpBlock(screen).Find<Button>(b => b.IsEffectivelyVisible &&
                                           b.GetVisualDescendants().OfType<MaterialIcon>().Any(i => i.Kind == icon));

    private static MaterialIcon IrpIcon(RiskScreen screen, MaterialIconKind icon) =>
        IrpBlock(screen).Find<MaterialIcon>(i => i.Kind == icon && i.IsEffectivelyVisible);
}
