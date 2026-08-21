using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class MasterDashboardView : UserControl
{
    public MasterDashboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Loads on first realisation rather than in the constructor so the shell can create the
    /// view eagerly without firing an admin-only request for a user who never opens it.
    /// </summary>
    private void OnInitialized(object? sender, System.EventArgs e)
    {
        if (DataContext is MasterDashboardViewModel vm)
        {
            _ = vm.InitializeAsync();
        }
    }
}
