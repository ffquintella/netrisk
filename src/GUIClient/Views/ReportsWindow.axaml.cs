using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

public partial class ReportsWindow : Window
{
    public ReportsWindow()
    {
        InitializeComponent();
        WindowsManager.AllWindows.Add(this);
    }
}