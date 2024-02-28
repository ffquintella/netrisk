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
    #endregion
    
    
    #region METHODS
    public override Task ActivateAsync(ReportDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            
        });
        //throw new System.NotImplementedException();
    }
    
    #endregion
}