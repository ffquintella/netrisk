using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Admin;
using Model.Plugins;

namespace GUIClient.Views.Admin;

public partial class PluginsView : UserControl
{
    public PluginsView()
    {
        InitializeComponent();
    }

    private bool _psOldValue = false;
    private bool _psInitialized = false;

    private void PluginSwitch_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {

        
        var pVm = DataContext as PluginsViewModel;
        
        var pluginSwitch = sender as ToggleSwitch;
        var pluginInfo = pluginSwitch?.DataContext as PluginInfo;
        
        if (pluginInfo == null || pVm == null)
            return;

        if (!_psInitialized)
        {
            _psInitialized = true;
            _psOldValue = pluginInfo.IsEnabled;
        }



        if (_psOldValue != pluginInfo.IsEnabled)
        {
            _psOldValue = pluginInfo.IsEnabled;
            pVm.SetPluginEnabledStatus(pluginInfo.Name, pluginInfo.IsEnabled);
        }
            
    }
}