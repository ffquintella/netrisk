using System.Collections.Generic;
using System.Linq;
using GUIClient.Agents;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using ReactiveUI.Binding;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>
/// "An AI agent is asking for access" (T348): agent, reason, requested minutes and mode. The user may grant fewer
/// minutes or read-only; the choices never exceed the request and start at one block, read-only
/// (<see cref="AgentAccessChoices"/>).
/// </summary>
public class AgentAccessDialogViewModel : ParameterizedDialogViewModelBase<AgentAccessDialogResult, AgentAccessDialogParameter>
{
    public string StrTitle { get; } = Localizer["AgentAccessTitle"];
    public string StrAgentName { get; } = Localizer["AgentName"];
    public string StrAgentFingerprint { get; } = Localizer["AgentFingerprint"];
    public string StrReason { get; } = Localizer["AgentReason"];
    public string StrRequested { get; } = Localizer["AgentRequested"];
    public string StrGrantDuration { get; } = Localizer["AgentGrantDuration"];
    public string StrGrantMode { get; } = Localizer["AgentGrantMode"];
    public string StrNotice { get; } = Localizer["AgentAccessNoticeMSG"];
    public string StrGrant { get; } = Localizer["AgentGrant"];
    public string StrDeny { get; } = Localizer["AgentDeny"];
    public string StrConfirmEachWrite { get; } = Localizer["AgentConfirmEachWrite"];
    public string StrConfirmEachWriteHint { get; } = Localizer["AgentConfirmEachWriteHintMSG"];

    /// <summary>
    /// T361: the LGPD disclosure — item content, personal data included, goes to the agent's model provider; what is
    /// withheld, with the data classes the policy excludes now (T362); and that the agent must be one the organisation
    /// approved.
    /// </summary>
    /// <summary>T359 review #2: the same-user agent assumption — MCP-layer limits do not bind an agent with a shell.</summary>
    public string StrSandbox { get; } = Localizer["AgentAccessSandboxMSG"];

    public string StrLgpd { get; } = string.Format(Localizer["AgentAccessLgpdMSG"],
        ExcludedClassesText(GetService<AgentCoworkService>().ExclusionPolicy));

    /// <summary>The policy's classes as localized labels, or "none".</summary>
    public static string ExcludedClassesText(AgentExclusionPolicy policy) => policy.IsActive
        ? string.Join("; ", policy.Excluded.OrderBy(c => c).Select(c => Localizer[AgentDataClasses.LabelKey(c)]))
        : Localizer["AgentDataClassesNone"];

    private IReadOnlyList<int> _blocks = AgentAccessChoices.Blocks(1);
    private IReadOnlyList<AgentAccessMode> _modes = AgentAccessChoices.Modes(AgentAccessMode.Read);

    private string _agentName = "";
    public string AgentName
    {
        get => _agentName;
        set => this.RaiseAndSetIfChanged(ref _agentName, value);
    }

    private string _fingerprint = "";
    public string Fingerprint
    {
        get => _fingerprint;
        set => this.RaiseAndSetIfChanged(ref _fingerprint, value);
    }

    private string _reason = "";
    public string Reason
    {
        get => _reason;
        set => this.RaiseAndSetIfChanged(ref _reason, value);
    }

    private string _requestedText = "";
    public string RequestedText
    {
        get => _requestedText;
        set => this.RaiseAndSetIfChanged(ref _requestedText, value);
    }

    private List<string> _durationLabels = new();
    public List<string> DurationLabels
    {
        get => _durationLabels;
        set => this.RaiseAndSetIfChanged(ref _durationLabels, value);
    }

    private int _selectedDurationIndex;
    public int SelectedDurationIndex
    {
        get => _selectedDurationIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedDurationIndex, value);
    }

    private List<string> _modeLabels = new();
    public List<string> ModeLabels
    {
        get => _modeLabels;
        set => this.RaiseAndSetIfChanged(ref _modeLabels, value);
    }

    private int _selectedModeIndex;
    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedModeIndex, value);
            IsReadWriteSelected = SelectedMode == AgentAccessMode.ReadWrite;
        }
    }

    private bool _isReadWriteSelected;
    /// <summary>T357: the confirm-each-write choice only shows for a read-write grant.</summary>
    public bool IsReadWriteSelected
    {
        get => _isReadWriteSelected;
        set => this.RaiseAndSetIfChanged(ref _isReadWriteSelected, value);
    }

    private bool _confirmEachWrite = AgentAccessChoices.DefaultConfirmEachWrite;
    public bool ConfirmEachWrite
    {
        get => _confirmEachWrite;
        set => this.RaiseAndSetIfChanged(ref _confirmEachWrite, value);
    }

    private AgentAccessMode SelectedMode => _modes[System.Math.Clamp(SelectedModeIndex, 0, _modes.Count - 1)];

    private bool _isDecisionArmed;
    /// <summary>False until the dialog has been on screen for the arm delay; Grant is disabled until then.</summary>
    public bool IsDecisionArmed
    {
        get => _isDecisionArmed;
        set => this.RaiseAndSetIfChanged(ref _isDecisionArmed, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtGrantClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDenyClicked { get; }

    public AgentAccessDialogViewModel()
    {
        BtGrantClicked = ReactiveCommand.Create(ExecuteGrant, this.WhenAnyValue(x => x.IsDecisionArmed));
        BtDenyClicked = ReactiveCommand.Create(Dismiss);
    }

    /// <summary>Called by the view when it opens: Grant becomes clickable after <paramref name="delay"/>.</summary>
    public void StartArming(System.TimeSpan delay) =>
        Avalonia.Threading.DispatcherTimer.RunOnce(() => IsDecisionArmed = true, delay);

    /// <summary>Closes as a refusal — the deny button, and the prompter when the request stops waiting.</summary>
    public void Dismiss() => Close(new AgentAccessDialogResult { Action = ResultActions.Cancel });

    public override void Activate(AgentAccessDialogParameter parameter)
    {
        if (parameter.Prompt is not { } prompt) return;

        AgentName = prompt.AgentName;
        Fingerprint = prompt.AgentFingerprint;
        // T355: a request the user started ("Grant another 10 min") says so instead of an agent's reason.
        Reason = prompt.UserInitiated ? Localizer["AgentExtensionReasonMSG"] : prompt.Reason;
        RequestedText = string.Format(Localizer["AgentRequestedValueMSG"],
            prompt.RequestedBlocks * AgentLeaseOptions.BlockMinutes, ModeLabel(prompt.RequestedMode));

        _blocks = AgentAccessChoices.Blocks(prompt.RequestedBlocks);
        _modes = AgentAccessChoices.Modes(prompt.RequestedMode);
        DurationLabels = _blocks
            .Select(b => string.Format(Localizer["AgentMinutesOption"], b * AgentLeaseOptions.BlockMinutes))
            .ToList();
        ModeLabels = _modes.Select(ModeLabel).ToList();
        SelectedDurationIndex = _blocks.ToList().IndexOf(AgentAccessChoices.DefaultBlocks);
        SelectedModeIndex = _modes.ToList().IndexOf(AgentAccessChoices.DefaultMode);
    }

    private void ExecuteGrant()
    {
        var blocks = _blocks[System.Math.Clamp(SelectedDurationIndex, 0, _blocks.Count - 1)];
        var mode = SelectedMode;
        Close(new AgentAccessDialogResult
        {
            Action = ResultActions.Ok, Blocks = blocks, Mode = mode,
            ConfirmEachWrite = mode != AgentAccessMode.ReadWrite || ConfirmEachWrite
        });
    }

    private static string ModeLabel(AgentAccessMode mode) =>
        mode == AgentAccessMode.ReadWrite ? Localizer["AgentModeReadWrite"] : Localizer["AgentModeRead"];
}
