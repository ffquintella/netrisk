using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class SecretVaultPickerDialog : DialogWindowBase<SecretVaultPickerResult>
{
    public SecretVaultPickerDialog()
    {
        InitializeComponent();
    }
}
