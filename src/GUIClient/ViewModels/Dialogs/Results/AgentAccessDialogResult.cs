using GUIClient.Agents;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>The grant the user chose: at most the blocks asked for, at most the mode asked for.</summary>
public class AgentAccessDialogResult : DialogResultBase
{
    public int Blocks { get; init; }
    public AgentAccessMode Mode { get; init; }
}
