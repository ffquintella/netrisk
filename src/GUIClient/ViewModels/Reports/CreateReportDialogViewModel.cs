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

public class CreateReportDialogViewModel: ParameterizedDialogViewModelBaseAsync<ReportDialogResult, ReportDialogParameter>
{
    #region LANGUAGE
    public string StrCreateReport { get; } = Localizer["CreateReport"];
    public string StrReportType { get; } = Localizer["ReportType"];
    public string StrDetailedEntitiesRisks { get; } = Localizer["DetailedEntitiesRisks"];
    public string StrHostVulnerabilityPrioritization { get; } = Localizer["HostVulnerabilityPrioritization"];

    public string StrCreate { get; } = Localizer["Create"];

    public new string StrCancel { get; } = Localizer["Cancel"];
    #endregion

    #region SERVICES

    private IReportTemplatesService ReportTemplatesService { get; } = GetService<IReportTemplatesService>();

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

    private ReportDialogResult Result { get; set; } = new();

    #endregion

    #region METHODS
    public override async Task ActivateAsync(ReportDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        var options = new List<ReportTypeOption>
        {
            new() { Name = StrDetailedEntitiesRisks, ReportType = 0 },
            new() { Name = StrHostVulnerabilityPrioritization, ReportType = 1 },
        };

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

    public void CreateReport()
    {
        if (SelectedReportOption == null) return;

        Result.ReportType = SelectedReportOption.ReportType;
        Result.TemplateId = SelectedReportOption.TemplateId;
        Result.ReportName = SelectedReportOption.Name;
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
