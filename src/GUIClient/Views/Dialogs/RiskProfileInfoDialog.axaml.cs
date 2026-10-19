using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Dialogs;

/// <summary>The read-only profile dialog behind the Cyber Risk Overview's profile chip (S40 §5.1; T251).</summary>
public partial class RiskProfileInfoDialog : DialogWindowBase<DialogResultBase>
{
    public RiskProfileInfoDialog()
    {
        InitializeComponent();
    }
}
