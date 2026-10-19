using Avalonia.Controls;

namespace GUIClient.Views.RiskOverview;

/// <summary>The always-visible governance and review indicators (S40 §5.2.5; T256). No code-behind logic: the view-model owns every rule.</summary>
public partial class RiskGovernanceIndicatorsStrip : UserControl
{
    public RiskGovernanceIndicatorsStrip()
    {
        InitializeComponent();
    }
}
