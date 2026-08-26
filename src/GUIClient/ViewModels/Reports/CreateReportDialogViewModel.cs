using RxVoid = ReactiveUI.Primitives.RxVoid;
using System.Reactive.Linq;
using GUIClient.Interfaces;
using System.Windows.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Reports;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels.Reports;

public class CreateReportDialogViewModel: ParameterizedDialogViewModelBaseAsync<ReportDialogResult, ReportDialogParameter>, ISaveableDialog
{
    #region LANGUAGE
    public string StrCreateReport { get; } = Localizer["CreateReport"];
    public string StrReportType { get; } = Localizer["ReportType"];
    public string StrDetailedEntitiesRisks { get; } = Localizer["DetailedEntitiesRisks"];
    public string StrHostVulnerabilityPrioritization { get; } = Localizer["HostVulnerabilityPrioritization"];
    public string StrGovernanceEvidencePack { get; } = Localizer["GovernanceEvidencePack"];
    public string StrEntity { get; } = Localizer["Entity"];
    public string StrAllEntities { get; } = Localizer["AllEntities"];
    public string StrFrom { get; } = Localizer["From"];
    public string StrTo { get; } = Localizer["To"];

    public string StrCreate { get; } = Localizer["Create"];

    public new string StrCancel { get; } = Localizer["Cancel"];
    #endregion

    #region SERVICES

    private IReportTemplatesService ReportTemplatesService { get; } = GetService<IReportTemplatesService>();

    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();

    #endregion

    #region PROPERTIES

    private ObservableCollection<ReportTypeOption> _reportOptions = new();
    public ObservableCollection<ReportTypeOption> ReportOptions
    {
        get => _reportOptions;
        set => this.RaiseAndSetIfChanged(ref _reportOptions, value);
    }

    private ReportTypeOption? _selectedReportOption;
    public ReportTypeOption? SelectedReportOption
    {
        get => _selectedReportOption;
        set => this.RaiseAndSetIfChanged(ref _selectedReportOption, value);
    }

    /// <summary>
    /// The entities the evidence pack can be scoped to, with a leading "all entities" entry. Only
    /// populated when a scoped report is on offer, so the ordinary report path costs no extra call.
    /// </summary>
    public ObservableCollection<ReportEntityOption> EntityOptions { get; } = [];

    private ReportEntityOption? _selectedEntityOption;
    public ReportEntityOption? SelectedEntityOption
    {
        get => _selectedEntityOption;
        set => this.RaiseAndSetIfChanged(ref _selectedEntityOption, value);
    }

    private DateTimeOffset? _periodStart;
    public DateTimeOffset? PeriodStart
    {
        get => _periodStart;
        set => this.RaiseAndSetIfChanged(ref _periodStart, value);
    }

    private DateTimeOffset? _periodEnd;
    public DateTimeOffset? PeriodEnd
    {
        get => _periodEnd;
        set => this.RaiseAndSetIfChanged(ref _periodEnd, value);
    }

    private bool _showEvidenceScope;

    /// <summary>Whether the entity and period pickers are on screen for the current selection.</summary>
    public bool ShowEvidenceScope
    {
        get => _showEvidenceScope;
        private set => this.RaiseAndSetIfChanged(ref _showEvidenceScope, value);
    }

    private ReportDialogResult Result { get; set; } = new();

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> CreateReportCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <inheritdoc />
    /// <remarks>This dialog's primary action is "create", so that is what Ctrl+S commits.</remarks>
    public ICommand? SaveCommand => CreateReportCommand;

    #endregion

    #region CONSTRUCTOR

    public CreateReportDialogViewModel()
    {
        CreateReportCommand = ReactiveCommand.Create(CreateReport,
            this.WhenAnyValue(x => x.SelectedReportOption).Select(option => option != null));
        CancelCommand = ReactiveCommand.Create(Cancel);

        this.WhenAnyValue(x => x.SelectedReportOption)
            .Subscribe(option => ShowEvidenceScope = option?.NeedsEvidenceScope == true);
    }

    #endregion

    #region METHODS
    public override async Task ActivateAsync(ReportDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        var options = new List<ReportTypeOption>
        {
            new() { Name = StrDetailedEntitiesRisks, ReportType = 0 },
            new() { Name = StrHostVulnerabilityPrioritization, ReportType = 1 },
            new()
            {
                Name = StrGovernanceEvidencePack,
                ReportType = ReportParameters.GovernanceEvidenceReportType,
                NeedsEvidenceScope = true
            },
        };

        // A year back to today, which is the look-back an annual audit asks for and the same default
        // the server applies when the period is left empty.
        PeriodEnd = DateTimeOffset.UtcNow;
        PeriodStart = PeriodEnd.Value.AddYears(-1);

        await LoadEntityOptionsAsync();

        try
        {
            var templates = await ReportTemplatesService.GetAllAsync();

            options.AddRange(templates.Select(t => new ReportTypeOption
            {
                Name = t.Name,
                ReportType = ReportParameters.TemplateReportType,
                TemplateId = t.Id,
            }));
        }
        catch (Exception e)
        {
            Log.Error("Error loading report templates {Message}", e.Message);
        }

        ReportOptions = new ObservableCollection<ReportTypeOption>(options);
        SelectedReportOption = ReportOptions.FirstOrDefault();
    }

    /// <summary>
    /// The entity list for the evidence scope. A failure here leaves the "all entities" entry, which
    /// still produces a valid pack — an export that cannot be narrowed is a smaller problem than an
    /// export nobody can start.
    /// </summary>
    private async Task LoadEntityOptionsAsync()
    {
        EntityOptions.Clear();
        EntityOptions.Add(new ReportEntityOption { Name = StrAllEntities, EntityId = null });

        try
        {
            var entities = await EntitiesService.GetAllAsync();

            foreach (var entity in entities.OrderBy(e => e.Id))
                EntityOptions.Add(new ReportEntityOption
                {
                    Name = EntityDisplayName(entity),
                    EntityId = entity.Id
                });
        }
        catch (Exception e)
        {
            Log.Error("Error loading entities for the evidence report scope {Message}", e.Message);
        }

        SelectedEntityOption = EntityOptions.FirstOrDefault();
    }

    /// <summary>
    /// An entity's name lives in an <c>entities_properties</c> row rather than on the entity, so a
    /// row without one falls back to the id instead of rendering blank.
    /// </summary>
    private static string EntityDisplayName(DAL.Entities.Entity entity)
    {
        var name = entity.EntitiesProperties?
            .FirstOrDefault(p => p.Type == "name")?.Value;

        return string.IsNullOrWhiteSpace(name) ? $"#{entity.Id}" : name;
    }

    public void CreateReport()
    {
        if (SelectedReportOption == null) return;

        Result.ReportType = SelectedReportOption.ReportType;
        Result.TemplateId = SelectedReportOption.TemplateId;
        Result.ReportName = SelectedReportOption.Name;

        if (SelectedReportOption.NeedsEvidenceScope)
        {
            Result.EntityId = SelectedEntityOption?.EntityId;

            // Sent as UTC because the period is compared against UTC timestamps on the server. A
            // local-time boundary would silently move an event into the neighbouring quarter for
            // anybody east or west of the server.
            Result.PeriodStart = PeriodStart?.UtcDateTime;
            Result.PeriodEnd = PeriodEnd?.UtcDateTime;
        }

        Result.Action = ResultActions.Ok;
        Close(Result);
    }

    public void Cancel()
    {
        Result.Action = ResultActions.Cancel;
        Close(Result);
    }

    #endregion
}
