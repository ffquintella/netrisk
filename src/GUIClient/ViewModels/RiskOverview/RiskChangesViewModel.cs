using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>One line of "O que mudou"; a line that names an object opens its detail (§5.7 → §5.9.2).</summary>
public sealed class RiskChangeRowViewModel(ChangeRow row, ReactiveCommand<RiskChangeRowViewModel, RxVoid> open)
{
    public string Effect => row.Effect;
    public string Category => row.Category;
    public string Origin => row.Origin;
    public string? OriginHint => row.OriginHint;
    public bool IsRedacted => row.OriginHint is not null;
    public string Points => row.Points;
    public string Percent => row.Percent;
    public RiskIndexObjectLabelDto? Object => row.Object;
    public bool CanOpen => row.HasObject;
    public ReactiveCommand<RiskChangeRowViewModel, RxVoid> Open { get; } = open;
}

/// <summary>A period choice of the panel.</summary>
public sealed record ChangesPeriodOption(ChangesPeriod Period, string Label);

/// <summary>
/// "O que mudou" (S40 §5.7; T255): <c>GET /RiskIndex/Changes</c> for the page's node — the attribution by effect, category
/// and object, the 100 largest lines and the rest summed, and the configuration's share when the period crosses a profile
/// change. Not available in a partial view (the attribution needs the node's whole set): the reason replaces the table.
/// </summary>
public class RiskChangesViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskChangesTitle"];
    public string StrHelp { get; } = Localizer["RiskChangesHelp"];
    public string StrPeriod { get; } = Localizer["RiskChangesPeriod"];
    public string StrColEffect { get; } = Localizer["RiskChangesColEffect"];
    public string StrColCategory { get; } = Localizer["RiskChangesColCategory"];
    public string StrColOrigin { get; } = Localizer["RiskChangesColOrigin"];
    public string StrColPoints { get; } = Localizer["RiskChangesColPoints"];
    public string StrColPercent { get; } = Localizer["RiskChangesColPercent"];
    public string StrEvents { get; } = Localizer["RiskChangesEvents"];
    public string StrEmpty { get; } = Localizer["RiskChangesEmpty"];
    public string StrOpenDetailHint { get; } = Localizer["RiskObjectsOpenDetailHint"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];
    private string StrUnavailablePartial { get; } = Localizer["RiskChangesUnavailablePartial"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private readonly Func<RiskIndexObjectLabelDto, Task> _openDetail;
    private string? _node;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private int _generation;

    public RiskChangesViewModel(Func<RiskIndexObjectLabelDto, Task> openDetail)
    {
        _openDetail = openDetail;
        Periods = Enum.GetValues<ChangesPeriod>()
            .Select(p => new ChangesPeriodOption(p, RiskText.Get(Text, ChangesFormatter.PeriodKey(p)))).ToList();
        _selectedPeriod = Periods[0];
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReadAsync);
        OpenRow = ReactiveCommand.CreateFromTask<RiskChangeRowViewModel>(OpenRowAsync);
    }

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    /// <summary>A line with an object: its detail (§5.7 "Clicar numa linha abre o detalhe do objeto").</summary>
    public ReactiveCommand<RiskChangeRowViewModel, RxVoid> OpenRow { get; }

    #endregion

    #region PROPERTIES

    public IReadOnlyList<ChangesPeriodOption> Periods { get; }

    private ChangesPeriodOption _selectedPeriod;
    public ChangesPeriodOption SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (value is null || value == _selectedPeriod) return;
            this.RaiseAndSetIfChanged(ref _selectedPeriod, value);
            _ = ReadAsync();
        }
    }

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }

    public ObservableCollection<RiskChangeRowViewModel> Rows { get; } = [];

    private RiskChangeRowViewModel? _restRow;
    /// <summary>The lines beyond the 100 largest, summed into one (§5.7).</summary>
    public RiskChangeRowViewModel? RestRow
    {
        get => _restRow;
        private set
        {
            this.RaiseAndSetIfChanged(ref _restRow, value);
            this.RaisePropertyChanged(nameof(HasRest));
        }
    }

    public bool HasRest => RestRow is not null;

    private string _periodText = "";
    public string PeriodText { get => _periodText; private set => this.RaiseAndSetIfChanged(ref _periodText, value); }

    private string? _configurationNote;
    public string? ConfigurationNote
    {
        get => _configurationNote;
        private set
        {
            this.RaiseAndSetIfChanged(ref _configurationNote, value);
            this.RaisePropertyChanged(nameof(HasConfigurationNote));
        }
    }

    public bool HasConfigurationNote => !string.IsNullOrEmpty(ConfigurationNote);

    private string? _residualNote;
    public string? ResidualNote
    {
        get => _residualNote;
        private set
        {
            this.RaiseAndSetIfChanged(ref _residualNote, value);
            this.RaisePropertyChanged(nameof(HasResidualNote));
        }
    }

    public bool HasResidualNote => !string.IsNullOrEmpty(ResidualNote);

    public ObservableCollection<string> Events { get; } = [];

    private bool _hasEvents;
    public bool HasEvents { get => _hasEvents; private set => this.RaiseAndSetIfChanged(ref _hasEvents, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; private set => this.RaiseAndSetIfChanged(ref _isEmpty, value); }

    private bool _hasTable;
    public bool HasTable { get => _hasTable; private set => this.RaiseAndSetIfChanged(ref _hasTable, value); }

    private string? _unavailableText;
    /// <summary>The reason the panel has no table: a partial view or an unofficial preview (§5.7, S39 §10.4).</summary>
    public string? UnavailableText
    {
        get => _unavailableText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _unavailableText, value);
            this.RaisePropertyChanged(nameof(IsUnavailable));
        }
    }

    public bool IsUnavailable => !string.IsNullOrEmpty(UnavailableText);

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

    #endregion

    #region LOADING

    public void Clear()
    {
        _generation++;
        _node = null;
        IsActive = false;
        ShowNothing();
        UnavailableText = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// A new page: the node's "what moved" up to the snapshot shown, through the page's gate. A partial view or an
    /// unofficial preview has none (§5.7): the reason is shown without asking the server.
    /// </summary>
    public Task LoadAsync(string node, DateOnly date, RiskIndexProvenance provenance, SemaphoreSlim gate, CancellationToken token)
    {
        _node = node;
        _date = date;
        _gate = gate;
        _token = token;
        IsActive = true;
        if (provenance is RiskIndexProvenance.PartialRestricted or RiskIndexProvenance.UnofficialPreview)
        {
            _generation++;
            ShowNothing();
            ErrorMessage = null;
            UnavailableText = StrUnavailablePartial;
            return Task.CompletedTask;
        }

        UnavailableText = null;
        return ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (_node is not { } node) return;
        var generation = ++_generation;
        var token = _token;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _gate.WaitAsync(token);
            RiskIndexChangesDto changes;
            try
            {
                changes = await RiskIndexService.GetChangesAsync(node, ChangesFormatter.From(SelectedPeriod.Period, _date), _date, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            Show(ChangesFormatter.Format(changes, Text, CultureInfo.CurrentCulture));
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading what moved in the risk index");
            ShowNothing();
            if (ex is InvalidHttpRequestException { ErrorCode: RiskApiCodes.ChangesUnavailablePartial })
                UnavailableText = StrUnavailablePartial;
            else
                ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                    ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                    : StrLoadError;
        }
        catch (Exception ex)
        {
            // Superseded by a newer read or a new page: nothing to show, but the failure is still recorded.
            Logger.Debug("A superseded read of the what moved ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }

    private void Show(ChangesView view)
    {
        Rows.Clear();
        foreach (var row in view.Rows) Rows.Add(new RiskChangeRowViewModel(row, OpenRow));
        RestRow = view.Rest is { } rest ? new RiskChangeRowViewModel(rest, OpenRow) : null;
        PeriodText = view.Period;
        ConfigurationNote = view.ConfigurationNote;
        ResidualNote = view.ResidualNote;
        Events.Clear();
        foreach (var e in view.Events) Events.Add(e);
        HasEvents = Events.Count > 0;
        IsEmpty = Rows.Count == 0;
        HasTable = Rows.Count > 0;
    }

    private void ShowNothing()
    {
        Rows.Clear();
        RestRow = null;
        PeriodText = "";
        ConfigurationNote = null;
        ResidualNote = null;
        Events.Clear();
        HasEvents = false;
        IsEmpty = false;
        HasTable = false;
    }

    private Task OpenRowAsync(RiskChangeRowViewModel? row) =>
        row?.Object is { } obj ? _openDetail(obj) : Task.CompletedTask;

    #endregion
}
