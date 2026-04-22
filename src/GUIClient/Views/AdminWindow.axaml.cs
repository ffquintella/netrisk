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
        #endif
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        WindowsManager.AllWindows.Add(this);
    }
    
}