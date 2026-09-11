using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    /// <summary>
    /// The delete button in a plugin row.
    /// </summary>
    /// <remarks>
    /// Handled here rather than through a command binding for the same reason the toggle below is:
    /// the cell's data context is the <see cref="PluginInfo"/>, so the row's own plugin is what the
    /// handler has in hand, and reaching the view model from inside the template is the part that
    /// goes wrong silently in a compiled binding.
    /// </remarks>
    private void DeletePlugin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginsViewModel viewModel) return;
        if ((sender as Button)?.DataContext is not PluginInfo plugin) return;

        _ = viewModel.DeletePluginAsync(plugin);
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