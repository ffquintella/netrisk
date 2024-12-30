using System.Reflection.Emit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.Models;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class EditIncidentWindow : Window
{
    public EditIncidentWindow(OperationType windowOperationType)
    {
        InitializeComponent();
        
        EditIncidentViewModel viewModel = new(windowOperationType);
        viewModel.ParentWindow = this;

        DataContext = viewModel;
    }

}