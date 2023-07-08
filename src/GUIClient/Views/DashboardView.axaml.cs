
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;


namespace GUIClient.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        DataContext = new DashboardViewModel();
        InitializeComponent();
    }

    private void OnInitialized(object sender, System.EventArgs e)
    {
        //((DashboardViewModel) DataContext).Initialize();
    }
    
    private void InitializeComponent()
    {
       
        AvaloniaXamlLoader.Load(this);
    }
}