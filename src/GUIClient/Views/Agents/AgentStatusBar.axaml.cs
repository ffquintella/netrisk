using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Agents;

namespace GUIClient.Views.Agents;

/// <summary>The agent lease indicator at the bottom of the main window (T355). Carries its own view-model.</summary>
public partial class AgentStatusBar : UserControl
{
    public AgentStatusBar()
    {
        if (!Design.IsDesignMode) DataContext = new AgentStatusBarViewModel();
        AvaloniaXamlLoader.Load(this);
        DetachedFromVisualTree += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
