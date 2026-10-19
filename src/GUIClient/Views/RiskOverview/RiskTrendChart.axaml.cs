using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.RiskOverview;

/// <summary>A panel of the Cyber Risk Overview (S40 §10.1). No code-behind logic: the view-model owns every rule.</summary>
public partial class RiskTrendChart : UserControl
{
    public RiskTrendChart()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
