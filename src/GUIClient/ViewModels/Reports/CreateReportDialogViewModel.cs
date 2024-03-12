using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports;

public class CreateReportDialogViewModel: ParameterizedDialogViewModelBaseAsync<ReportDialogResult, ReportDialogParameter>
{
    #region LANGUAGE
    public string StrCreateReport { get; } = Localizer["CreateReport"];
    public string StrReportType { get; } = Localizer["ReportType"];
    public string StrDetailedEntitiesRisks { get; } = Localizer["DetailedEntitiesRisks"];
    
    public string StrCreate { get; } = Localizer["Create"];
    
    public string StrCancel { get; } = Localizer["Cancel"];
    #endregion
    
    #region PROPERTIES

    private int _selectedReportType = 0;
    public int SelectedReportType
    {
        get => _selectedReportType;
        set => this.RaiseAndSetIfChanged(ref _selectedReportType, value);
    }
    
    private ReportDialogResult Result { get; set; } = new();
        
    #endregion
    
    #region METHODS
    public override Task ActivateAsync(ReportDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            
        }, cancellationToken);
    }
    
    public void CreateReport()
    {
        Result.ReportType = SelectedReportType;
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