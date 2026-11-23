using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using ReactiveUI;
using ReactiveUI.Binding;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>A node of the preview: the active and the draft value, Δ and the band move (S40 §6.3).</summary>
public sealed record RiskPreviewNodeRow(string Node, string Type, string Active, string Draft, string Delta, string Bands,
    bool BandChanged, double SortDelta);

/// <summary>A tornado line (±20 % on one parameter, S39 §13.2), with the share of the widest swing for the bar.</summary>
public sealed record RiskTornadoRow(string Parameter, string Minus, string Plus, double MinusShare, double PlusShare);

/// <summary>A value of the node under a preset (the "Média pura" row shows the dilution, S39 §12.3).</summary>
public sealed record RiskPresetValueRow(string Preset, string Node, string Value);

/// <summary>An object that moves most between versions (|Δ R·w|).</summary>
public sealed record RiskMovingObjectRow(string Object, string Category, string Delta);

/// <summary>
/// The sensitivity preview and the submission (S40 §6.3; T261): run the 90-day preview of the saved draft and follow its
/// state, read the node deltas, band changes, Kendall τ, tornado, effective register weight, presets and top movers, then
/// submit with a justification once the server says the draft is valid and the preview matches these parameters.
/// </summary>
public class RiskProfilePreviewViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrRunPreview { get; } = Localizer["RiskAdminRunPreview"];
    public string StrNodes { get; } = Localizer["RiskAdminPreviewNodes"];
    public string StrNode { get; } = Localizer["RiskAdminNode"];
    public string StrType { get; } = Localizer["RiskAdminNodeType"];
    public string StrActive { get; } = Localizer["RiskAdminActiveValue"];
    public string StrDraft { get; } = Localizer["RiskAdminDraftValue"];
    public string StrDelta { get; } = Localizer["RiskAdminDelta"];
    public string StrBand { get; } = Localizer["RiskAdminBandMove"];
    public string StrBandChanged { get; } = Localizer["RiskAdminBandChanged"];
    public string StrRanking { get; } = Localizer["RiskAdminRanking"];
    public string StrRegisterWeight { get; } = Localizer["RiskAdminRegisterWeight"];
    public string StrTornado { get; } = Localizer["RiskAdminTornado"];
    public string StrParameter { get; } = Localizer["RiskProfileParameterKey"];
    public string StrMinus { get; } = Localizer["RiskAdminMinus20"];
    public string StrPlus { get; } = Localizer["RiskAdminPlus20"];
    public string StrPresets { get; } = Localizer["RiskAdminPresetValues"];
    public string StrPureMean { get; } = Localizer["RiskAdminPureMean"];
    public string StrTopMovers { get; } = Localizer["RiskAdminTopMovers"];
    public string StrObject { get; } = Localizer["RiskAdminObject"];
    public string StrCategory { get; } = Localizer["RiskAdminCategory"];
    public string StrValue { get; } = Localizer["RiskProfileParameterValue"];
    public string StrSubmission { get; } = Localizer["RiskAdminSubmission"];
    public string StrJustification { get; } = Localizer["RiskAdminJustification"];
    public string StrSubmit { get; } = Localizer["RiskAdminSubmit"];
    public string StrNoDraft { get; } = Localizer["RiskAdminNoDraftMSG"];
    public string StrPartial { get; } = Localizer["RiskAdminPreviewPartial"];

    #endregion

    private readonly RiskProfileDraftSession _session;
    private readonly IRiskIndexConfigService _configService;
    private int _pollGeneration;

    public RiskProfilePreviewViewModel(RiskProfileDraftSession session, IRiskIndexConfigService configService)
    {
        _session = session;
        _configService = configService;
        _session.SessionChanged += reloaded =>
        {
            if (reloaded) _ = LoadAsync();
            else RefreshChecks();
        };
        this.WhenAnyValue(vm => vm.Justification).Subscribe(_ => RaiseGates());
    }

    #region PROPERTIES

    private RiskIndexPreviewDto? _preview;

    public RiskIndexPreviewDto? Preview
    {
        get => _preview;
        private set => this.RaiseAndSetIfChanged(ref _preview, value);
    }

    public RiskIndexPreviewState State => Preview?.Status.State ?? RiskIndexPreviewState.NotRun;

    public bool IsRunning => State is RiskIndexPreviewState.Queued or RiskIndexPreviewState.Running;

    public bool HasReport => State == RiskIndexPreviewState.Completed || (State == RiskIndexPreviewState.Stale && Preview?.Nodes.Count > 0);

    public string StateText => Preview?.Status is not { } status
        ? RiskAdminText.Text("RiskPreviewState_NotRun")
        : status.State switch
        {
            RiskIndexPreviewState.Running => RiskAdminText.Format("RiskPreviewState_RunningFMT", status.Progress),
            RiskIndexPreviewState.Completed => RiskAdminText.Format("RiskPreviewState_CompletedFMT", RiskAdminText.Moment(status.FinishedAtUtc)),
            RiskIndexPreviewState.Failed => RiskAdminText.Format("RiskPreviewState_FailedFMT", RiskAdminText.Dash(status.ErrorMessage)),
            _ => RiskAdminText.Text("RiskPreviewState_" + status.State)
        };

    public double Progress => Preview?.Status.Progress ?? 0;

    public bool HasDraft => _session.HasDraft;

    public string Header => _session.Draft is { } d ? RiskAdminText.Format("RiskAdminPreviewHeaderFMT", d.Version, d.Name) : "";

    public ObservableCollection<RiskPreviewNodeRow> Nodes { get; } = [];
    public ObservableCollection<RiskTornadoRow> Tornado { get; } = [];
    public ObservableCollection<RiskPresetValueRow> PresetValues { get; } = [];
    public ObservableCollection<RiskMovingObjectRow> TopMovers { get; } = [];

    public string RankingText => Preview is not { } p
        ? "—"
        : p.KendallTau is { } tau
            ? RiskAdminText.Format("RiskAdminKendallFMT", tau.ToString("0.00", CultureInfo.CurrentCulture), p.ComparedUnits)
            : RiskAdminText.Text("RiskAdminKendallNone");

    public string RegisterWeightText => Preview is not { } p
        ? "—"
        : RiskAdminText.Format("RiskAdminRegisterWeightFMT", p.RegisterWeightActive.ToString("P0", CultureInfo.CurrentCulture),
            p.RegisterWeightDraft.ToString("P0", CultureInfo.CurrentCulture));

    public bool IsPartial => Preview?.OverlayPartial ?? false;

    private string? _justification;

    public string? Justification
    {
        get => _justification;
        set => this.RaiseAndSetIfChanged(ref _justification, value);
    }

    /// <summary>The submission's checklist (S40 §6.3): valid on the server, preview current, rebaseline or Board notes.</summary>
    public ObservableCollection<string> SubmitChecks { get; } = [];

    public string RebaselineNotice
    {
        get
        {
            var validation = _session.ServerValidation;
            if (validation is not { RequiresRebaseline: true }) return "";
            var triggers = string.Join(" · ", validation.RebaselineTriggers.Select(t => RiskAdminText.Text("RiskRebaseline_" + t)));
            return RiskAdminText.Format("RiskAdminRebaselineProposalFMT", triggers);
        }
    }

    public bool HasRebaselineNotice => RebaselineNotice.Length > 0;

    private string? _errorMessage;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // ---------- gates ----------

    private Gate RunGate => ProfileWorkflowGates.RunPreview(_session.Rights.Configure,
        _session.Draft?.Status == RiskIndexProfileStatus.Draft, _session.IsDirty, State);

    public bool IsRunEnabled => RunGate.Enabled;
    public string StrRunReason => RiskAdminText.Reason(RunGate);

    private Gate SubmitGate => _session.Draft?.Status != RiskIndexProfileStatus.Draft
        ? Gate.Closed("RiskAdminNoDraft")
        : ProfileWorkflowGates.Submit(_session.Rights.Configure, _session.IsDirty, _session.ServerValidation, State, Justification);

    public bool IsSubmitEnabled => SubmitGate.Enabled;
    public string StrSubmitReason => RiskAdminText.Reason(SubmitGate);

    #endregion

    #region COMMANDS

    public async Task BtRunPreviewClicked()
    {
        if (!RunGate.Enabled || _session.Draft is not { } draft) return;
        var queued = false;
        await RunRiskAsync(Localizer["RiskAdminPreviewQueuedMSG"], async () =>
        {
            await _configService.RequestPreviewAsync(draft.Id);
            queued = true;
        });
        if (queued) await PollAsync();
    }

    public async Task BtSubmitClicked()
    {
        if (!SubmitGate.Enabled || _session.Draft is not { } draft) return;
        var submitted = false;
        await RunRiskAsync(string.Format(Localizer["RiskAdminSubmittedMSG"], draft.Version), async () =>
        {
            await _configService.SubmitAsync(draft.Id, new RiskIndexSubmitRequest(Justification!.Trim()));
            submitted = true;
        });
        if (!submitted) return;
        Justification = null;
        await _session.RefreshAfterLifecycleAsync();
    }

    #endregion

    #region METHODS

    /// <summary>Reads the draft's preview; the server validation the submission needs is fetched alongside.</summary>
    public async Task LoadAsync()
    {
        Clear();
        if (_session.Draft is not { } draft) return;
        await ReadPreviewAsync(draft.Id);
        if (draft.Status == RiskIndexProfileStatus.Draft && _session.Rights.Configure && !_session.IsDirty &&
            _session.ServerValidation is null)
            await _session.ValidateOnServerAsync();
        RefreshChecks();
        if (IsRunning) _ = PollAsync();
    }

    private async Task ReadPreviewAsync(int id)
    {
        try
        {
            Preview = await _configService.GetPreviewAsync(id);
            ErrorMessage = null;
        }
        catch (Model.Exceptions.InvalidHttpRequestException ex) when (ex.ErrorCode == "preview_missing")
        {
            Preview = null;
        }
        catch (Exception ex)
        {
            Logger.Warning("Error reading the preview of profile {Id}: {Message}", id, ex.Message);
            Preview = null;
            ErrorMessage = RiskAdminText.Explain(ex);
        }

        Fill();
    }

    /// <summary>Follows a queued or running preview every 5 s (S40 §6.3) until it ends or another load replaces it.</summary>
    private async Task PollAsync()
    {
        var generation = ++_pollGeneration;
        while (generation == _pollGeneration && _session.Draft is { } draft)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (generation != _pollGeneration) return;
            await ReadPreviewAsync(draft.Id);
            if (!IsRunning) return;
        }
    }

    private void Clear()
    {
        _pollGeneration++;
        Preview = null;
        Fill();
    }

    private void Fill()
    {
        Nodes.Clear();
        Tornado.Clear();
        PresetValues.Clear();
        TopMovers.Clear();
        var culture = CultureInfo.CurrentCulture;

        if (Preview is { } p && HasReport)
        {
            foreach (var node in p.Nodes.OrderByDescending(n => Math.Abs(n.Draft - (n.Active ?? n.Draft))))
            {
                var delta = node.Active is { } a ? node.Draft - a : (double?)null;
                Nodes.Add(new RiskPreviewNodeRow(node.Node.Label, node.Node.NodeType,
                    node.Active?.ToString("0.0", culture) ?? "—", node.Draft.ToString("0.0", culture),
                    delta?.ToString("+0.0;−0.0;0", culture) ?? "—",
                    RiskAdminText.Dash(node.BandKeyActive) + " → " + RiskAdminText.Dash(node.BandKeyDraft), node.BandChanged,
                    Math.Abs(delta ?? 0)));
            }

            var widest = p.Tornado.Select(t => Math.Max(Math.Abs(t.Minus), Math.Abs(t.Plus))).DefaultIfEmpty(0).Max();
            foreach (var row in p.Tornado.OrderByDescending(t => Math.Abs(t.Plus - t.Minus)))
                Tornado.Add(new RiskTornadoRow(ProfileIssueText.Label(row.ParameterKey, RiskAdminText.Optional) ?? row.ParameterKey,
                    row.Minus.ToString("+0.0;−0.0;0", culture), row.Plus.ToString("+0.0;−0.0;0", culture),
                    widest > 0 ? Math.Abs(row.Minus) / widest : 0, widest > 0 ? Math.Abs(row.Plus) / widest : 0));

            foreach (var value in p.Presets.Concat(p.PureMean))
                PresetValues.Add(new RiskPresetValueRow(RiskAdminText.Text("RiskPreset_" + value.PresetKey), value.Node.Label,
                    value.Value.ToString("0.0", culture)));

            foreach (var mover in p.TopMovers)
                TopMovers.Add(new RiskMovingObjectRow(mover.Object.Label, mover.Category.ToString(),
                    mover.DeltaRw.ToString("+0.000;−0.000;0", culture)));
        }

        foreach (var name in new[]
                 {
                     nameof(State), nameof(IsRunning), nameof(HasReport), nameof(StateText), nameof(Progress), nameof(HasDraft),
                     nameof(Header), nameof(RankingText), nameof(RegisterWeightText), nameof(IsPartial)
                 })
            this.RaisePropertyChanged(name);
        RefreshChecks();
    }

    private void RefreshChecks()
    {
        SubmitChecks.Clear();
        var validation = _session.ServerValidation;
        SubmitChecks.Add(RiskAdminText.Text(validation is null ? "RiskAdminCheckNotValidated"
            : validation.IsValid ? "RiskAdminCheckValid" : "RiskAdminCheckInvalid"));
        SubmitChecks.Add(RiskAdminText.Text(State == RiskIndexPreviewState.Completed ? "RiskAdminCheckPreviewCurrent"
            : State == RiskIndexPreviewState.Stale ? "RiskAdminCheckPreviewStale" : "RiskAdminCheckPreviewMissing"));
        if (validation?.RequiresBoardRef == true) SubmitChecks.Add(RiskAdminText.Text("RiskAdminCheckBoardRequired"));
        RaiseGates();
    }

    private void RaiseGates()
    {
        foreach (var name in new[]
                 {
                     nameof(IsRunEnabled), nameof(StrRunReason), nameof(IsSubmitEnabled), nameof(StrSubmitReason),
                     nameof(RebaselineNotice), nameof(HasRebaselineNotice)
                 })
            this.RaisePropertyChanged(name);
    }

    #endregion
}
