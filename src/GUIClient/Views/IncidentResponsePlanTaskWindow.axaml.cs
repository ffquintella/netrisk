using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class IncidentResponsePlanTaskWindow : Window
{
    
    private IncidentResponsePlanTaskViewModel? _viewModel;
    
    public new object? DataContext
    {
        get => _viewModel;
        set
        {
            var model = value as IncidentResponsePlanTaskViewModel;
            if(model == null) throw new InvalidCastException(); 
            model.ParentWindow = this;
            base.DataContext = model;
            _viewModel = model;
        }
    }

    public IncidentResponsePlanTaskWindow()
    {
        InitializeComponent();
    }
}