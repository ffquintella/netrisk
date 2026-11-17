using GUIClient.Agents;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>The registration an agent asked for, as the approval dialog shows it (T346).</summary>
public class AgentRegistrationDialogParameter : NavigationParameterBase
{
    public RegistrationPrompt? Prompt { get; init; }
}

/// <summary>The access an agent asked for, as the approval dialog shows it (T348).</summary>
public class AgentAccessDialogParameter : NavigationParameterBase
{
    public AccessPrompt? Prompt { get; init; }
}

/// <summary>One edit an agent prepared, as the per-write dialog shows it (T357).</summary>
public class AgentWriteDialogParameter : NavigationParameterBase
{
    public WritePrompt? Prompt { get; init; }
}
