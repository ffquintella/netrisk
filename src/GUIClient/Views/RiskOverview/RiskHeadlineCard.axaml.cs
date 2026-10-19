using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.RiskOverview;

/// <summary>The headline card of the Cyber Risk Overview (S40 §5.2; T251). No code-behind logic.</summary>
public partial class RiskHeadlineCard : UserControl
{
    public RiskHeadlineCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
