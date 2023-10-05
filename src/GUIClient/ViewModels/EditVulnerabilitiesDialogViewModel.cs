using System.Threading;
using System.Threading.Tasks;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.ViewModels;

public class EditVulnerabilitiesDialogViewModel: ParameterizedDialogViewModelBaseAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>
{
    public override Task ActivateAsync(VulnerabilityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            /*if(parameter.Title != null) StrTitle = parameter.Title;
            if(parameter.FieldName != null) StrFieldName = parameter.FieldName;
            */
        });
    }
}