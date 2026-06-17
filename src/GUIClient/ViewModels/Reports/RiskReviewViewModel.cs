using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using GUIClient.Models;
using GUIClient.Tools;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;
using ClientServices.Interfaces;
using GUIClient.Hydrated;

namespace GUIClient.ViewModels.Reports;

public class RiskReviewViewModel: ReportsViewModelBase
{
    #region LANGUAGE
    public string StrDaysSinceLastReview { get; }
    public string StrSubject { get; }
    public string StrStatus { get; }
    public string StrSubmissionDate { get; }
    public string StrReviewDate { get; }
    
    #endregion

    #region PROPERTIES

    private int _daysSinceLastReview = 30;
    public int DaysSinceLastReview {
        get => _daysSinceLastReview;
        set => this.RaiseAndSetIfChanged(ref _daysSinceLastReview, value);
    }
    
    private ObservableCollection<RiskReviewReportItem>? _risks;

    public ObservableCollection<RiskReviewReportItem>? Risks
    {
        get => _risks;
        set => this.RaiseAndSetIfChanged(ref _risks, value);
    }

    public ReactiveCommand<Unit, Unit> BtGenerateClicked { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    #endregion

    #region FIELDS

    private IRisksService _risksService;

    #endregion

    public RiskReviewViewModel()
    {
        StrDaysSinceLastReview = Localizer["Days since last review"] + ":";
        StrSubject = Localizer["Subject"];
        StrStatus = Localizer["Status"];
        StrSubmissionDate = Localizer["SubmissionDate"];
        StrReviewDate = Localizer["ReviewDate"];
        
        BtGenerateClicked = ReactiveCommand.Create(ExecuteGenerate);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync,
            this.WhenAnyValue(x => x.Risks).Select(r => r != null && r.Count > 0));

        _risksService = GetService<IRisksService>();

    }

    private async Task ExportAsync()
    {
        if (Risks == null || Risks.Count == 0) return;

        var owner = WindowsManager.AllWindows.Find(w => w is Views.ReportsWindow);

        var format = await ExportFileSaver.PickFormatAsync(
            owner, Localizer["Export"], Localizer["Choose the export format"], includePdf: false);

        if (format is null) return;

        var data = format == ExportFormat.Csv
            ? GridDataExporter.ToCsv(Risks)
            : GridDataExporter.ToXlsx(Risks, "RiskReview");

        await ExportFileSaver.SaveAsync(owner, format.Value, data);
    }
    
#region METHODS

    public void ExecuteGenerate()
    {

        var risks = _risksService.GetToReview(DaysSinceLastReview);

        var localRisks = new List<RiskReviewReportItem>();
        foreach (var risk in risks)
        {
            localRisks.Add(new RiskReviewReportItem(risk));
        }

        Risks = new ObservableCollection<RiskReviewReportItem>(localRisks);

    }

#endregion
    
}

