using Avalonia.Controls;

namespace GUIClient.Views.RiskOverview;

/// <summary>The data-quality detail (S40 §5.12.2; T256). No code-behind logic: the view-model owns every rule.</summary>
public partial class RiskQualityPanel : UserControl
{
    public RiskQualityPanel()
    {
        InitializeComponent();
    }
}
