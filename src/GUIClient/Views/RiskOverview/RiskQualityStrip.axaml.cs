using Avalonia.Controls;

namespace GUIClient.Views.RiskOverview;

/// <summary>The always-visible data-quality strip (S40 §5.12.1; T256). No code-behind logic: the view-model owns every rule.</summary>
public partial class RiskQualityStrip : UserControl
{
    public RiskQualityStrip()
    {
        InitializeComponent();
    }
}
