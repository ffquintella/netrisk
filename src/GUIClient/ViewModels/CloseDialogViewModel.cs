using System.Threading;
using System.Threading.Tasks;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.ViewModels;

public class CloseDialogViewModel: ParameterizedDialogViewModelBaseAsync<CloseDialogResult, CloseDialogParameter>
{
    public override Task ActivateAsync(CloseDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        throw new System.NotImplementedException();
    }
}