using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

/// <summary>
/// Renders the transient notification stack in the bottom-right of the shell. Hit-test
/// transparent, so it never blocks the content it floats over.
/// </summary>
public partial class NotificationHost : UserControl
{
    public NotificationHost()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
