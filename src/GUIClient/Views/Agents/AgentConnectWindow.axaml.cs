using Avalonia.Markup.Xaml;

namespace GUIClient.Views.Agents;

/// <summary>"Connect an agent" (M65): registers the NetRisk MCP server in the AI clients on this computer.</summary>
public partial class AgentConnectWindow : AuxiliaryWindowBase
{
    public AgentConnectWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
