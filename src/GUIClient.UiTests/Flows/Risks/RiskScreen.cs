using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Tools;
using GUIClient.UiTests.Infrastructure;
using GUIClient.ViewModels;
using GUIClient.Views;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Model.Authentication;
using Model.DTO;
using Model.Entities;
using Model.Globalization;
using Model.Governance;
using Model.Risks.Chain;
using NSubstitute;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>Synthetic users of the risk register (S54 §9: no production data).</summary>
internal static class RiskUsers
{
    public const int AnalystId = 2;
    public const int ManagerId = 3;

    /// <summary>
    /// A user of the register: <c>riskmanagement</c> (every <c>RequireRiskmanagement</c> read the screen does) plus
    /// <paramref name="permissions"/>. Neither the admin flag nor the Admin role.
    /// </summary>
    public static AuthenticatedUserInfo With(params string[] permissions) => new()
    {
        UserId = AnalystId,
        UserName = "Analista de Riscos",
        UserAccount = "analista.riscos",
        UserEmail = "analista.riscos@example.invalid",
        UserRole = "Analyst",
        UserPermissions = ["riskmanagement", .. permissions]
    };

    /// <summary>Reads the register and may do nothing else.</summary>
    public static AuthenticatedUserInfo Reader => With();

    /// <summary>Holds every permission the risk record's writes need (RisksController's policies).</summary>
    public static AuthenticatedUserInfo Manager =>
        With("submit_risks", "close_risks", "delete_risk", "incident-response-plans", "irp-delete");
}

/// <summary>
/// The register a fake server holds for one test, and the doubles that serve it. State lives here rather than in
/// canned answers so a write the screen makes (delete, close, create) shows in the next read, as it would against
/// the API.
/// </summary>
internal sealed class RiskServer
{
    public const int Leak = 1;
    public const int Erp = 2;
    public const int Fraud = 3;
    public const int Backup = 4;

    public const string LeakSubject = "Vazamento de dados de clientes";
    public const string ErpSubject = "Indisponibilidade do ERP financeiro";
    public const string FraudSubject = "Fraude em pagamentos a fornecedores";
    public const string BackupSubject = "Perda de cópias de segurança";

    public List<Risk> Rows { get; } =
    [
        new()
        {
            Id = Leak, Subject = LeakSubject, Status = "New", ControlNumber = "CTRL-001", Source = 1, Category = 2,
            Owner = RiskUsers.AnalystId, Manager = RiskUsers.ManagerId, SubmittedBy = RiskUsers.AnalystId,
            SubmissionDate = new DateTime(2026, 9, 1), Notes = "Revisar os controles de acesso ao CRM.",
            ScenarioCause = "Credenciais vazadas", ScenarioCentralEvent = "Acesso indevido à base de clientes",
            RiskCatalogs = [new RiskCatalog { Id = 7, Number = "A.9", Name = "Controle de acesso", Description = "" }]
        },
        new()
        {
            Id = Erp, Subject = ErpSubject, Status = "Mitigation Planned", ControlNumber = "CTRL-002", Source = 1,
            Category = 2, Owner = RiskUsers.AnalystId, Manager = RiskUsers.ManagerId,
            SubmissionDate = new DateTime(2026, 8, 15), Notes = ""
        },
        new()
        {
            Id = Fraud, Subject = FraudSubject, Status = "Mgmt Reviewed", ControlNumber = "CTRL-003", Source = 1,
            Category = 2, Owner = RiskUsers.AnalystId, Manager = RiskUsers.ManagerId,
            SubmissionDate = new DateTime(2026, 7, 20), Notes = ""
        },
        new()
        {
            Id = Backup, Subject = BackupSubject, Status = "Closed", ControlNumber = "CTRL-004", Source = 1,
            Category = 2, Owner = RiskUsers.AnalystId, Manager = RiskUsers.ManagerId,
            SubmissionDate = new DateTime(2026, 6, 1), Notes = ""
        }
    ];

    public static readonly List<Likelihood> Likelihoods =
    [
        new() { Value = 1, Name = "Raro" },
        new() { Value = 2, Name = "Improvável" },
        new() { Value = 3, Name = "Possível" }
    ];

    public static readonly List<Impact> Impacts =
    [
        new() { Value = 1, Name = "Insignificante" },
        new() { Value = 2, Name = "Menor" },
        new() { Value = 3, Name = "Moderado" }
    ];

    public static readonly List<CloseReason> CloseReasons =
    [
        new() { Value = 1, Name = "Mitigado" },
        new() { Value = 2, Name = "Aceito pela diretoria" }
    ];

    public IRisksService Risks { get; }

    private RiskServer(UiServices services)
    {
        Risks = services.Double<IRisksService>();
        var risks = Risks;

        risks.GetAllRisksAsync(Arg.Any<bool>()).Returns(call =>
        {
            var includeClosed = call.Arg<bool>();
            return Task.FromResult(Rows.Where(r => includeClosed || r.Status != "Closed").ToList());
        });

        // Reference data — the ConstantManager and the editor read it — answered at once.
        risks.GetProbabilitiesAsync().Returns(_ => Task.FromResult<List<Likelihood>?>(Likelihoods.ToList()));
        risks.GetImpactsAsync().Returns(_ => Task.FromResult<List<Impact>?>(Impacts.ToList()));
        risks.GetRiskCategoriesAsync().Returns(_ => Task.FromResult(new List<Category>
        {
            new() { Value = 1, Name = "Operacional" }, new() { Value = 2, Name = "Segurança da informação" }
        }));
        risks.GetRiskSourcesAsync().Returns(_ => Task.FromResult<List<Source>?>(
        [
            new Source { Value = 1, Name = "Auditoria interna" }, new Source { Value = 2, Name = "Autoavaliação" }
        ]));
        risks.GetRiskTypesAsync().Returns(_ => Task.FromResult(new List<RiskCatalog>
        {
            new() { Id = 7, Number = "A.9", Name = "Controle de acesso", Description = "" },
            new() { Id = 8, Number = "A.12", Name = "Segurança nas operações", Description = "" }
        }));
        risks.GetRiskCloseReasons().Returns(_ => CloseReasons.ToList());
        risks.GetRiskSourceAsync(1).Returns("Auditoria interna");
        risks.GetRiskCategoryAsync(2).Returns("Segurança da informação");
        risks.GetRiskScore(Arg.Any<int>(), Arg.Any<int>()).Returns(call => (float)(call.ArgAt<int>(0) * call.ArgAt<int>(1)));

        // The detail's first read answers after a dispatcher hop, as every REST call does: Hydrated.Risk raises its
        // change events from the load its constructor starts, and the risk screen subscribes only once the
        // constructor has returned — a double answering synchronously would finish that load first and the scores,
        // mitigation and review would never reach the screen (T327).
        risks.GetRiskScoringAsync(Arg.Any<int>()).Returns(call => Later(Scoring(call.Arg<int>())));
        risks.GetRiskClosure(Backup).Returns(new Closure
        {
            Id = 40, RiskId = Backup, CloseReason = 2, ClosureDate = new DateTime(2026, 9, 30), Note = "Aceito."
        });

        var users = services.Double<IUsersService>();
        users.GetAllAsync(Arg.Any<bool>()).Returns(_ => Task.FromResult(new List<UserListing>
        {
            new() { Id = RiskUsers.AnalystId, Name = "Analista de Riscos", Username = "analista.riscos" },
            new() { Id = RiskUsers.ManagerId, Name = "Gestora de Riscos", Username = "gestora.riscos" }
        }));
        users.GetUserName(RiskUsers.AnalystId).Returns("Analista de Riscos");
        users.GetUserName(RiskUsers.ManagerId).Returns("Gestora de Riscos");
        users.GetUserNameAsync(RiskUsers.AnalystId).Returns("Analista de Riscos");
        users.GetUserNameAsync(RiskUsers.ManagerId).Returns("Gestora de Riscos");

        var mitigations = services.Double<IMitigationService>();
        mitigations.GetStrategiesAsync().Returns(_ => Task.FromResult<List<PlanningStrategy>?>(
            [new PlanningStrategy { Value = 1, Name = "Mitigar" }, new PlanningStrategy { Value = 2, Name = "Aceitar" }]));
        mitigations.GetCostsAsync().Returns(_ => Task.FromResult<List<MitigationCost>?>(
            [new MitigationCost { Value = 2, Name = "Médio" }]));
        mitigations.GetEffortsAsync().Returns(_ => Task.FromResult<List<MitigationEffort>?>(
            [new MitigationEffort { Value = 3, Name = "Considerável" }]));

        services.Double<IRiskGovernanceService>().GetScorePairsAsync(Arg.Any<List<int>?>()).Returns(_ =>
            Task.FromResult(new List<RiskScorePair>
            {
                new() { RiskId = Leak, Inherent = 6, Residual = 4.5f },
                new() { RiskId = Erp, Inherent = 4 }
            }));

        services.Double<IImpactsService>().GetAllAsync().Returns(_ => Task.FromResult(new List<LocalizableListItem>
        {
            new() { Key = 2, Value = "Medium", LocalizedValue = "Médio" },
            new() { Key = 3, Value = "High", LocalizedValue = "Alto" }
        }));
        services.Double<ITeamsService>().GetByIdAsync(Arg.Any<int>(), Arg.Any<bool>())
            .Returns(_ => Task.FromResult(new Team { Value = 5, Name = "Infraestrutura" }));

        services.Double<IEntitiesService>().GetEntitiesConfigurationAsync().Returns(_ =>
            Task.FromResult(new EntitiesConfiguration
            {
                Definitions = { ["unit"] = new EntityDefinition { IconKind = "Domain" } }
            }));
    }

    /// <summary>Arranges this test's doubles with the register above.</summary>
    public static RiskServer Arrange() => new(UiServices.Current);

    /// <summary>What the server answers for a risk's scoring: inherent 6 (likelihood 3 × impact 2), residual 4.5.</summary>
    public static RiskScoring Scoring(int riskId) => new()
    {
        Id = riskId, ScoringMethod = 1, CalculatedRisk = 6, ClassicLikelihood = 3, ClassicImpact = 2,
        ResidualRisk = riskId == Leak ? 4.5f : null
    };

    /// <summary>A task that completes after one dispatcher hop, the way a REST call does.</summary>
    public static async Task<T> Later<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    public Risk Row(int id) => Rows.Single(r => r.Id == id);
}

/// <summary>
/// The risk register (<see cref="RiskView"/>) as a user meets it: hosted in a window, signed in, loaded, and driven
/// through simulated input. Element lookups go by the names and icons the view declares.
/// </summary>
internal sealed class RiskScreen
{
    private RiskScreen(RiskView view, Window window)
    {
        View = view;
        Window = window;
    }

    public RiskView View { get; }
    public Window Window { get; }
    public RiskViewModel ViewModel => (RiskViewModel)View.DataContext!;

    /// <summary>
    /// Signs <paramref name="user"/> in, shows the register the way the shell does — the view first, then the
    /// authentication event its view-model waits for — and waits for <paramref name="expectedRows"/> rows.
    /// </summary>
    public static async Task<RiskScreen> OpenAsync(AuthenticatedUserInfo user, int expectedRows = 3)
    {
        var services = UiServices.Current;
        var auth = services.Double<IAuthenticationService>();
        auth.IsAuthenticated.Returns(true);
        auth.AuthenticatedUserInfo.Returns(user);

        var view = new RiskView();
        var window = UiHost.Host(view, 1600, 1100);
        services.Double<IMainWindowProvider>().GetActiveWindow().Returns(window);
        services.Double<IMainWindowProvider>().GetMainWindow().Returns(window);

        auth.AuthenticationSucceeded += Raise.Event();

        var screen = new RiskScreen(view, window);
        if (expectedRows > 0)
            await UiHost.WaitUntilAsync(() => screen.Subjects.Count == expectedRows,
                $"the register to list {expectedRows} risks");
        return screen;
    }

    public ListBox List => View.Find<ListBox>(l => l.Name == "LstRisks");

    /// <summary>The rows the list shows, top to bottom, by their first line (the subject).</summary>
    public IReadOnlyList<string> Subjects => RowItems.Select(SubjectOf).ToList();

    /// <summary>The second line of the row whose subject is <paramref name="subject"/> (the register flags).</summary>
    public string FlagLine(string subject) => RowTexts(Row(subject)).ElementAtOrDefault(1) ?? string.Empty;

    /// <summary>The third line of the row whose subject is <paramref name="subject"/> (inherent → residual).</summary>
    public string ScoreLine(string subject) => RowTexts(Row(subject)).ElementAtOrDefault(2) ?? string.Empty;

    /// <summary>Clicks the row of <paramref name="subject"/> and waits for its detail to finish loading.</summary>
    public async Task SelectAsync(string subject)
    {
        UiInput.Click(Row(subject));
        await UiHost.WaitUntilAsync(() =>
                ViewModel.SelectedRisk?.Subject == subject && ViewModel.SelectedRiskId != null &&
                !ViewModel.LoadingSpinner && ViewModel.HdRisk?.Scoring.Id == ViewModel.SelectedRiskId,
            $"the detail of \"{subject}\" to load");
    }

    public ListBoxItem Row(string subject) =>
        RowItems.FirstOrDefault(i => SubjectOf(i) == subject)
        ?? throw new InvalidOperationException(
            $"No row \"{subject}\" in the register; it lists: {string.Join(" | ", Subjects)}");

    /// <summary>A button of the bar under the list, by the name the view gives it (BtAddRisk, BtDeleteRisk, …).</summary>
    public Button Bar(string name) => View.Find<Button>(b => b.Name == name);

    /// <summary>A button of the lifecycle toolbar above the detail, by its icon.</summary>
    public Button Lifecycle(MaterialIconKind icon) =>
        View.Find<Button>(b => b.Classes.Contains("toolbar") &&
                               b.GetVisualDescendants().OfType<MaterialIcon>().Any(i => i.Kind == icon));

    public ScrollViewer Detail => View.Find<ScrollViewer>(s => s.Name == "DetailsScroll");

    /// <summary>
    /// The value shown next to <paramref name="label"/> in the detail — the next text in the label's row —
    /// or null when the label is not on screen.
    /// </summary>
    public string? DetailValue(string label)
    {
        var labelBlock = Detail.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == label && t.IsEffectivelyVisible);
        if (labelBlock?.Parent is not Panel row) return null;

        return row.Children.OfType<TextBlock>()
            .SkipWhile(t => !ReferenceEquals(t, labelBlock)).Skip(1)
            .FirstOrDefault(t => t.IsEffectivelyVisible)?.Text;
    }

    /// <summary>Every text the detail shows right now.</summary>
    public IReadOnlyList<string> DetailTexts =>
        Detail.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text))
            .Select(t => t.Text!).ToList();

    /// <summary>Waits for a dialog of type <typeparamref name="T"/> to open over the register, and returns it laid out.</summary>
    public async Task<T> WaitForDialogAsync<T>() where T : Window
    {
        await UiHost.WaitUntilAsync(() => Window.OwnedWindows.OfType<T>().Any(), $"the {typeof(T).Name} to open");
        var dialog = Window.OwnedWindows.OfType<T>().Last();
        UiHost.Settle(dialog);
        return dialog;
    }

    /// <summary>Waits for every dialog over the register to close.</summary>
    public Task WaitForDialogsToCloseAsync(string what) =>
        UiHost.WaitUntilAsync(() => Window.OwnedWindows.Count == 0, what);

    /// <summary>
    /// The pt-BR text of <paramref name="key"/>. A key no resource defines reads as empty, which would make every
    /// <c>Assert.Contains</c> on it pass, so it fails here instead.
    /// </summary>
    public static string Localized(string key)
    {
        string text = UiServices.Current.GetRequiredService<ILocalizationService>()
            .GetLocalizer(typeof(GUIClient.App).Assembly)[key];
        return string.IsNullOrEmpty(text)
            ? throw new InvalidOperationException($"No resource defines the localized text \"{key}\".")
            : text;
    }

    private IEnumerable<ListBoxItem> RowItems =>
        List.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsVisible && i.DataContext is Risk);

    private static string SubjectOf(ListBoxItem item) => RowTexts(item).FirstOrDefault() ?? string.Empty;

    private static List<string> RowTexts(ListBoxItem item) =>
        item.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
}
