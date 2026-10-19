using Avalonia.Controls;

namespace GUIClient.Views.RiskOverview;

/// <summary>The monetary panel, interim and unavailable (S40 §5.10; T256). No code-behind logic: the view-model owns every rule.</summary>
public partial class RiskMonetaryPanel : UserControl
{
    public RiskMonetaryPanel()
    {
        InitializeComponent();
    }
}
