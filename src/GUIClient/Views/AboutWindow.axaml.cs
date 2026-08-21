using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

public partial class AboutWindow : AuxiliaryWindowBase
{
    public AboutWindow()
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