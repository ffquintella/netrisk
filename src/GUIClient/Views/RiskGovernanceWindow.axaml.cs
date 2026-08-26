using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

/// <summary>
/// One risk's governance record (Track 8): acceptance, counter-signature, treatment tasks,
/// quantitative scoring and the change history.
///
/// A <see cref="DialogWindowBase{TResult}"/> like every other modal in the app, so it inherits the
/// centralised Esc-to-dismiss and parent-dimming behaviour rather than reimplementing them.
/// </summary>
public partial class RiskGovernanceWindow : DialogWindowBase<RiskGovernanceDialogResult>
{
    public RiskGovernanceWindow()
    {
        InitializeComponent();
    }
}
