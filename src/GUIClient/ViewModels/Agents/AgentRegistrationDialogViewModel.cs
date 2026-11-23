using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using ReactiveUI.Binding;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>
/// "An AI agent wants to connect" (T346): name, description, client and the fingerprint the agent also shows, plus how
/// many other requests are queued behind this one. Approve issues the key; anything else refuses.
/// </summary>
public class AgentRegistrationDialogViewModel : ParameterizedDialogViewModelBase<DialogResultBase, AgentRegistrationDialogParameter>
{
    public string StrTitle { get; } = Localizer["AgentRegistrationTitle"];
    public string StrAgentName { get; } = Localizer["AgentName"];
    public string StrAgentDescription { get; } = Localizer["AgentDescription"];
    public string StrAgentClient { get; } = Localizer["AgentClient"];
    public string StrAgentFingerprint { get; } = Localizer["AgentFingerprint"];
    public string StrNotice { get; } = Localizer["AgentRegistrationNoticeMSG"];
    public string StrApprove { get; } = Localizer["AgentApprove"];
    public string StrDeny { get; } = Localizer["AgentDeny"];

    private string _agentName = "";
    public string AgentName
    {
        get => _agentName;
        set => this.RaiseAndSetIfChanged(ref _agentName, value);
    }

    private string _agentDescription = "";
    public string AgentDescription
    {
        get => _agentDescription;
        set => this.RaiseAndSetIfChanged(ref _agentDescription, value);
    }

    private string _clientText = "";
    public string ClientText
    {
        get => _clientText;
        set => this.RaiseAndSetIfChanged(ref _clientText, value);
    }

    private string _fingerprint = "";
    public string Fingerprint
    {
        get => _fingerprint;
        set => this.RaiseAndSetIfChanged(ref _fingerprint, value);
    }

    private string _queueText = "";
    public string QueueText
    {
        get => _queueText;
        set => this.RaiseAndSetIfChanged(ref _queueText, value);
    }

    private bool _hasQueue;
    public bool HasQueue
    {
        get => _hasQueue;
        set => this.RaiseAndSetIfChanged(ref _hasQueue, value);
    }

    private bool _isDecisionArmed;
    /// <summary>False until the dialog has been on screen for the arm delay; Approve is disabled until then.</summary>
    public bool IsDecisionArmed
    {
        get => _isDecisionArmed;
        set => this.RaiseAndSetIfChanged(ref _isDecisionArmed, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtApproveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDenyClicked { get; }

    public AgentRegistrationDialogViewModel()
    {
        BtApproveClicked = ReactiveCommand.Create(() => Close(new DialogResultBase { Action = ResultActions.Ok }),
            this.WhenAnyValue(x => x.IsDecisionArmed));
        BtDenyClicked = ReactiveCommand.Create(Dismiss);
    }

    /// <summary>Called by the view when it opens: Approve becomes clickable after <paramref name="delay"/>.</summary>
    public void StartArming(System.TimeSpan delay) =>
        Avalonia.Threading.DispatcherTimer.RunOnce(() => IsDecisionArmed = true, delay);

    /// <summary>Closes as a refusal — the deny button, and the prompter when the request stops waiting.</summary>
    public void Dismiss() => Close(new DialogResultBase { Action = ResultActions.Cancel });

    public override void Activate(AgentRegistrationDialogParameter parameter)
    {
        if (parameter.Prompt is not { } prompt) return;

        AgentName = prompt.Name;
        AgentDescription = prompt.Description;
        ClientText = string.IsNullOrEmpty(prompt.Client.Name)
            ? Localizer["AgentUnknownClient"]
            : (prompt.Client.Name + " " + prompt.Client.Version).Trim();
        Fingerprint = prompt.Fingerprint;
        HasQueue = prompt.WaitingCount > 0 || prompt.RefusedCount > 0;
        QueueText = string.Format(Localizer["AgentQueueStatusMSG"], prompt.WaitingCount, prompt.RefusedCount);
    }
}
