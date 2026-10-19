using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.RiskOverview;

/// <summary>A node's monthly category summary (S40 §5.5.1, §5.6.1). No code-behind logic: the view-model owns every rule.</summary>
public partial class RiskMonthlySummary : UserControl
{
    public RiskMonthlySummary()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
