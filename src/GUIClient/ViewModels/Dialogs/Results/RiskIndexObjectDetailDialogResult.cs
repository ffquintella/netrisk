using GUIClient.Navigation;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// What the object detail closes with (S40 §5.9.2, IX-2): the destination the user chose, or none. The caller navigates.
/// It carries the <see cref="NavigationTarget"/> because <c>DialogWindowBase</c> results derive from <see cref="DialogResultBase"/>.
/// </summary>
public class RiskIndexObjectDetailDialogResult : DialogResultBase
{
    public NavigationTarget? Target { get; init; }
}
