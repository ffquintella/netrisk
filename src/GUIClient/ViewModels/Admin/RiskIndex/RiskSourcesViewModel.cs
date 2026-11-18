using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>One source line of the Sources and freshness tab, editing the shared draft session.</summary>
public sealed class RiskSourceLineViewModel : ReactiveObject
{
    private readonly RiskProfileDraftSession _session;
    private bool _loading;

    public RiskSourceLineViewModel(RiskSourceLine line, RiskProfileDraftSession session)
    {
        _session = session;
        Source = line.Source;
        Load(line);
    }

    public RiskSourceInfo Source { get; }
    public string Id => Source.Id;
    public string Feeds => Source.Feeds;
    public string Availability => RiskAdminText.Text("RiskSourceAvailability_" + Source.Availability) +
                                  (Source.Qualifier is null ? "" : " " + Source.Qualifier);
    public bool HasWindow => Source.HasFreshnessWindow;

    private string _enabledActive = "";
    public string EnabledActive { get => _enabledActive; private set => this.RaiseAndSetIfChanged(ref _enabledActive, value); }

    private string _windowActive = "—";
    public string WindowActive { get => _windowActive; private set => this.RaiseAndSetIfChanged(ref _windowActive, value); }

    private bool _enabledDraft;

    public bool EnabledDraft
    {
        get => _enabledDraft;
        set
        {
            this.RaiseAndSetIfChanged(ref _enabledDraft, value);
            if (!_loading) _session.Edit(p => RiskSourceCatalogue.WithEnabled(p, Source.Id, value));
        }
    }

    private decimal? _windowDraft;

    public decimal? WindowDraft
    {
        get => _windowDraft;
        set
        {
            this.RaiseAndSetIfChanged(ref _windowDraft, value);
            if (!_loading && value is { } days) _session.Edit(p => RiskSourceCatalogue.WithWindow(p, Source.Id, (int)days));
        }
    }

    private Gate ToggleGate => RiskSourceCatalogue.Toggle(Source, EnabledDraft, CanEditDraft);

    /// <summary>A source can be edited when the user configures and no version is pending approval (S40 §6.5).</summary>
    private bool CanEditDraft => _session.IsEditable;

    public bool IsToggleEnabled => ToggleGate.Enabled;

    public string ToggleReason => ToggleGate.Enabled ? ""
        : ToggleGate.ReasonKey == "RiskSourceRequiresTask"
            ? RiskAdminText.Format("RiskSourceRequiresTaskFMT", Source.Qualifier ?? "")
            : RiskAdminText.Reason(ToggleGate);

    public bool IsWindowEnabled => HasWindow && CanEditDraft;

    private RiskSourceStatusCells _status = RiskSourceStatusFormatter.Cells(null, RiskAdminText.Optional, CultureInfo.CurrentCulture);

    /// <summary>The operational columns from <c>GET /RiskIndex/Sources</c> (S40 §6.5; T263).</summary>
    public RiskSourceStatusCells Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public void Load(RiskSourceLine line)
    {
        _loading = true;
        try
        {
            EnabledActive = RiskAdminText.Text(line.EnabledActive ? "Yes" : "No");
            WindowActive = line.WindowActive is { } w ? RiskAdminText.Format("RiskAdminDaysFMT", w) : "—";
            EnabledDraft = line.EnabledDraft;
            WindowDraft = line.WindowDraft;
        }
        finally
        {
            _loading = false;
        }

        this.RaisePropertyChanged(nameof(IsToggleEnabled));
        this.RaisePropertyChanged(nameof(ToggleReason));
        this.RaisePropertyChanged(nameof(IsWindowEnabled));
    }
}

/// <summary>
/// The Sources and freshness tab (S40 §6.5; T263): the S39 §3.2 catalogue with what each source feeds and its availability in
/// this version, enabled and windowed in the active profile and in the draft. It is another view of the one draft session:
/// the same Save and Discard as the Draft tab, and a Save with no draft first creates one from the active profile.
/// </summary>
public class RiskSourcesViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrCaution { get; } = Localizer["RiskSourcesCautionMSG"];
    public string StrSource { get; } = Localizer["RiskSourcesSource"];
    public string StrFeeds { get; } = Localizer["RiskSourcesFeeds"];
    public string StrAvailability { get; } = Localizer["RiskSourcesAvailability"];
    public string StrEnabledActive { get; } = Localizer["RiskSourcesEnabledActive"];
    public string StrEnabledDraft { get; } = Localizer["RiskSourcesEnabledDraft"];
    public string StrWindowActive { get; } = Localizer["RiskSourcesWindowActive"];
    public string StrWindowDraft { get; } = Localizer["RiskSourcesWindowDraft"];
    public string StrSaveDraft { get; } = Localizer["RiskAdminSaveDraft"];
    public string StrReset { get; } = Localizer["RiskAdminDiscardChanges"];
    public string StrRunsNote { get; } = Localizer["RiskSourcesRunsNoteMSG"];
    public string StrConnections { get; } = Localizer["RiskSourcesConnections"];
    public string StrLastSync { get; } = Localizer["RiskSourcesLastSync"];
    public string StrStatus { get; } = Localizer["RiskSourcesStatus"];
    public string StrObjects { get; } = Localizer["RiskSourcesObjects"];
    public string StrStale { get; } = Localizer["RiskSourcesStale"];
    public string StrRuns { get; } = Localizer["RiskSourcesRuns"];
    public string StrSourcesSubTab { get; } = Localizer["RiskSourcesSubTabSources"];
    public string StrMappingsSubTab { get; } = Localizer["RiskSourcesSubTabMappings"];
    public string StrRefreshStatus { get; } = Localizer["Reload"];

    #endregion

    private readonly RiskProfileDraftSession _session;
    private readonly IRiskIndexConfigService? _configService;
    private IReadOnlyDictionary<string, RiskIndexSourceDto> _status = new Dictionary<string, RiskIndexSourceDto>();

    public RiskSourcesViewModel(RiskProfileDraftSession session, IRiskIndexConfigService? configService = null,
        EntityMappingsViewModel? mappings = null)
    {
        _session = session;
        _configService = configService;
        Mappings = mappings;
        _session.SessionChanged += reloaded =>
        {
            if (reloaded || Lines.Count == 0) Reload();
            else Refresh();
        };
    }

    /// <summary>The vendor → entity rules panel (S40 §6.6; T263), hosted in this tab.</summary>
    public EntityMappingsViewModel? Mappings { get; }

    public ObservableCollection<RiskSourceLineViewModel> Lines { get; } = [];

    /// <summary>The "Execuções do índice" block: the latest snapshot runs, or why they are not shown.</summary>
    public ObservableCollection<string> Runs { get; } = [];

    private string _runsMessage = "";

    public string RunsMessage
    {
        get => _runsMessage;
        private set => this.RaiseAndSetIfChanged(ref _runsMessage, value);
    }

    /// <summary>
    /// Reads the operational columns and the latest snapshot runs (S40 §6.5; T263). The server decides what this user sees:
    /// a scoped reader gets the counts withheld and the runs refused with <c>runs_require_global_scope</c>, shown as such.
    /// </summary>
    public async Task LoadStatusAsync()
    {
        if (_configService is null) return;
        try
        {
            _status = RiskSourceStatusFormatter.ById(await _configService.GetSourcesAsync());
            ApplyStatus();
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not read the sources' status: {Message}", ex.Message);
            Toasts.Error(RiskAdminText.Explain(ex));
        }

        Runs.Clear();
        try
        {
            foreach (var run in await _configService.GetRunsAsync(RiskIndexRunKind.Snapshot))
                Runs.Add(RiskSourceStatusFormatter.RunLine(run, RiskAdminText.Optional, CultureInfo.CurrentCulture));
            RunsMessage = Runs.Count == 0 ? RiskAdminText.Text("RiskSourcesNoRunsMSG") : "";
        }
        catch (InvalidHttpRequestException ex) when (ex is { StatusCode: HttpStatusCode.Forbidden })
        {
            RunsMessage = RiskAdminText.Text(RiskSourceStatusFormatter.RequiresGlobalScope);
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not read the index runs: {Message}", ex.Message);
            RunsMessage = RiskAdminText.Explain(ex);
        }
    }

    public Task BtRefreshStatusClicked() => LoadStatusAsync();

    private void ApplyStatus()
    {
        foreach (var line in Lines)
            line.Status = RiskSourceStatusFormatter.Cells(_status.GetValueOrDefault(line.Id), RiskAdminText.Optional,
                CultureInfo.CurrentCulture);
    }

    /// <summary>The issues of the freshness group (W, the inventory window against the slowest source), live.</summary>
    public ObservableCollection<string> Issues { get; } = [];

    public string DraftStatus => _session.Draft is { } d
        ? RiskAdminText.Format("RiskSourcesDraftFMT", d.Version, RiskAdminText.Text(RiskOverviewTopBar.ProfileStatusKey(d.Status)))
        : RiskAdminText.Text("RiskSourcesNoDraftMSG");

    private Gate SaveGate => _session.Draft is null && _session.Active is null && _session.Rights.Configure
        ? Gate.Closed("RiskAdminNoActiveProfile")
        : ProfileWorkflowGates.SaveDraft(_session.Rights.Configure, _session.IsDirty, _session.IsBusy, _session.StructurallyValid,
            _session.Draft?.Status);

    public bool IsSaveEnabled => SaveGate.Enabled;
    public string StrSaveReason => RiskAdminText.Reason(SaveGate);

    public bool IsResetEnabled => _session.IsDirty;
    public string StrResetReason => IsResetEnabled ? "" : RiskAdminText.Text("RiskAdminNoChanges");

    public async Task BtSaveDraftClicked()
    {
        if (!SaveGate.Enabled) return;
        if (_session.Draft is null && _session.Active is { } active &&
            !await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["RiskSourcesCreateDraftConfirmMSG"], active.Version + 1)))
            return;
        await _session.SaveAsync();
    }

    public async Task BtResetClicked()
    {
        if (!IsResetEnabled) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["RiskAdminDiscardConfirmMSG"], _session.UnsavedChangesSummary)))
            return;
        _session.Discard();
    }

    private void Reload()
    {
        Lines.Clear();
        foreach (var line in RiskSourceCatalogue.Lines(_session.Active?.Parameters, _session.Edited))
            Lines.Add(new RiskSourceLineViewModel(line, _session));
        ApplyStatus();
        Refresh();
    }

    private void Refresh()
    {
        Issues.Clear();
        if (_session.LiveValidation is { } validation)
            foreach (var issue in validation.Errors.Concat(validation.Warnings)
                         .Where(i => RiskIndexProfileRules.GroupOf(i.ParameterKey ?? "") == "time"))
                Issues.Add(ProfileIssueText.Line(issue, RiskAdminText.Optional));

        foreach (var name in new[] { nameof(DraftStatus), nameof(IsSaveEnabled), nameof(StrSaveReason), nameof(IsResetEnabled),
                     nameof(StrResetReason) })
            this.RaisePropertyChanged(name);
    }
}
