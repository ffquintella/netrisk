using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class HostsView : UserControl
{
    public HostsView()
    {
        DataContext = new HostsViewModel();
        InitializeComponent();
    }
}