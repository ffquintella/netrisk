using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

public partial class AdminWindow : Window
{
    public AdminWindow()
    {
        InitializeComponent();
        
        #if DEBUG
        this.AttachDevTools();
        #endif
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}