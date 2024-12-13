using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class IncidentResponsePlanWindow : Window
{
    public IncidentResponsePlanWindow()
    {
        InitializeComponent();
    }

    private void TopLevel_OnClosed(object? sender, EventArgs e)
    {
        ((IncidentResponsePlanViewModel)DataContext!).OnClose();
        //var control = this.GetControl<ExperimentalAcrylicBorder>("BorderIRP");
        //control?.Dispose();
        this.Close();
    }


    public new Object? DataContext
    {
        get => base.DataContext;
        set
        {
            if (value is IncidentResponsePlanViewModel)
            {
                ((IncidentResponsePlanViewModel)value).ParentWindow = this;
            }
            
            base.DataContext = value;
        }
    }
}