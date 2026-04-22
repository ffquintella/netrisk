using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

public partial class CloseRiskWindow : Window
{
    public CloseRiskWindow()
    {
        InitializeComponent();
#if DEBUG
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}