using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class RiskView : UserControl
{
    public RiskView()
    {
        
        DataContext = new RiskViewModel();

        InitializeComponent();

    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}