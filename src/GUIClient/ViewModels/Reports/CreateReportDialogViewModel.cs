using System.Threading;
using System.Threading.Tasks;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;

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
    
    
    #region METHODS
    public override Task ActivateAsync(ReportDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            
        });
        //throw new System.NotImplementedException();
    }
    
    public void CreateReport()
    {
        //throw new System.NotImplementedException();
    }
    
    public void Cancel()
    {
        //throw new System.NotImplementedException();
    }
    
    #endregion
}