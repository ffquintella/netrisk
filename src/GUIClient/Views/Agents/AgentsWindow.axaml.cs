using System;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.Agents;

public partial class AgentsWindow : AuxiliaryWindowBase
{
    public AgentsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // The view-model listens to the agent runtime; let it go when the window does.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
