using System;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.Agents;

/// <summary>The agent activity panel (T355).</summary>
public partial class AgentActivityWindow : AuxiliaryWindowBase
{
    public AgentActivityWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // The view-model listens to the agent runtime; let it go when the window does.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
