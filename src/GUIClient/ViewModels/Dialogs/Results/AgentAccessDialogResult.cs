using GUIClient.Agents;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>The grant the user chose: at most the blocks asked for, at most the mode asked for.</summary>
public class AgentAccessDialogResult : DialogResultBase
{
    public int Blocks { get; init; }
    public AgentAccessMode Mode { get; init; }

    /// <summary>T357: every edit waits for a one-click approval. Meaningful only for a read-write grant.</summary>
    public bool ConfirmEachWrite { get; init; } = AgentAccessChoices.DefaultConfirmEachWrite;
}
