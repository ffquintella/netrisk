using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class EditIncidentWindow : Window
{
    public EditIncidentWindow(EditIncidentViewModel viewModel)
    {
        viewModel.ParentWindow = this;
        InitializeComponent();
        DataContext = viewModel;
    }

}