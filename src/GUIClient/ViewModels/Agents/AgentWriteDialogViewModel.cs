using System.Collections.Generic;
using System.Linq;
using GUIClient.Agents;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>One field of the change, old value and new value, as plain text.</summary>
public class AgentWriteChangeRow
{
    public string Field { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
}

/// <summary>
/// "An AI agent wants to change an item" (T357): the agent, the tool, the item and every field's old and new value.
/// Deny has the focus and Approve arms late (M61 safety rules); closing it any other way, or access ending while it is
/// open, writes nothing.
/// </summary>
public class AgentWriteDialogViewModel : ParameterizedDialogViewModelBase<DialogResultBase, AgentWriteDialogParameter>
{
    public string StrTitle { get; } = Localizer["AgentWriteTitle"];
    public string StrAgentName { get; } = Localizer["AgentName"];
    public string StrTool { get; } = Localizer["AgentWriteTool"];
    public string StrItem { get; } = Localizer["AgentWriteItem"];
    public string StrChanges { get; } = Localizer["AgentWriteChanges"];
    public string StrBefore { get; } = Localizer["AgentWriteBefore"];
    public string StrAfter { get; } = Localizer["AgentWriteAfter"];
    public string StrNotice { get; } = Localizer["AgentWriteNoticeMSG"];
    public string StrApprove { get; } = Localizer["AgentWriteApprove"];
    public string StrDeny { get; } = Localizer["AgentDeny"];

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

    private string _toolName = "";
    public string ToolName
    {
        get => _toolName;
        set => this.RaiseAndSetIfChanged(ref _toolName, value);
    }

    private string _itemText = "";
    public string ItemText
    {
        get => _itemText;
        set => this.RaiseAndSetIfChanged(ref _itemText, value);
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

    private List<AgentWriteChangeRow> _changes = new();
    public List<AgentWriteChangeRow> Changes
    {
        get => _changes;
        set => this.RaiseAndSetIfChanged(ref _changes, value);
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

    public AgentWriteDialogViewModel()
    {
        BtApproveClicked = ReactiveCommand.Create(() => Close(new DialogResultBase { Action = ResultActions.Ok }),
            this.WhenAnyValue(x => x.IsDecisionArmed));
        BtDenyClicked = ReactiveCommand.Create(Dismiss);
    }

    /// <summary>Called by the view when it opens: Approve becomes clickable after <paramref name="delay"/>.</summary>
    public void StartArming(System.TimeSpan delay) =>
        Avalonia.Threading.DispatcherTimer.RunOnce(() => IsDecisionArmed = true, delay);

    /// <summary>Closes as a refusal — the deny button, and the prompter when the write stops waiting.</summary>
    public void Dismiss() => Close(new DialogResultBase { Action = ResultActions.Cancel });

    public override void Activate(AgentWriteDialogParameter parameter)
    {
        if (parameter.Prompt is not { } prompt) return;

        AgentName = prompt.AgentName;
        Fingerprint = prompt.AgentFingerprint;
        ToolName = prompt.ToolName;
        ItemText = string.Format(Localizer["AgentWriteItemValueMSG"], KindLabel(prompt.Item.Kind), prompt.Item.Id,
            prompt.ItemLabel);
        HasQueue = prompt.WaitingCount > 0;
        QueueText = string.Format(Localizer["AgentWriteQueueMSG"], prompt.WaitingCount);
        var empty = Localizer["AgentWriteEmptyValue"];
        Changes = prompt.Changes.Select(c => new AgentWriteChangeRow
        {
            Field = c.Field,
            Before = string.IsNullOrEmpty(c.Before) ? empty : c.Before,
            After = string.IsNullOrEmpty(c.After) ? empty : c.After
        }).ToList();
    }

    /// <summary>The localized name of an item kind (risk, finding…), shared by the status bar and the activity panel.</summary>
    public static string KindLabel(string kind) => kind switch
    {
        AgentItemKinds.Risk => Localizer["AgentItemKindRisk"],
        AgentItemKinds.Finding => Localizer["AgentItemKindFinding"],
        AgentItemKinds.Incident => Localizer["AgentItemKindIncident"],
        AgentItemKinds.ResponsePlan => Localizer["AgentItemKindResponsePlan"],
        AgentItemKinds.Assessment => Localizer["AgentItemKindAssessment"],
        AgentItemKinds.AssessmentRun => Localizer["AgentItemKindAssessmentRun"],
        AgentItemKinds.Entity => Localizer["AgentItemKindEntity"],
        AgentItemKinds.Host => Localizer["AgentItemKindHost"],
        _ => kind
    };
}
