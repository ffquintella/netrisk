using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GUIClient.Views.RiskOverview;

/// <summary>
/// The Explore panel (S40 §5.6.1; T254). The one piece of code-behind closes "+ Filtro"'s flyout once a value is chosen:
/// a <c>Flyout</c> does not close on a click inside it, and the view-model has no business reaching for a popup.
/// </summary>
public partial class RiskExplorePanel : UserControl
{
    public RiskExplorePanel()
    {
        InitializeComponent();
        FilterMenuContent?.AddHandler(Button.ClickEvent, (_, _) => AddFilterButton.Flyout?.Hide(), RoutingStrategies.Bubble);
    }
}
