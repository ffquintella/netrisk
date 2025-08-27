using System;
using System.Reflection.Emit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class EditIncidentWindow : Window
{

    public EditIncidentWindow()
    {
        InitializeComponent();
        
        EditIncidentViewModel viewModel = new(OperationType.Create);
        viewModel.ParentWindow = this;

        DataContext = viewModel;
    }
    
    public EditIncidentWindow(OperationType windowOperationType)
    {
        InitializeComponent();
        
        EditIncidentViewModel viewModel = new(windowOperationType);
        viewModel.ParentWindow = this;

        DataContext = viewModel;
    }
    
    public EditIncidentWindow(OperationType windowOperationType, Incident incident)
    {
        InitializeComponent();

        if (windowOperationType == OperationType.Create) throw new NotSupportedException("Use the other constructor for Create operation");
        EditIncidentViewModel viewModel = new(windowOperationType, incident);
        viewModel.ParentWindow = this;

        DataContext = viewModel;
    }

}