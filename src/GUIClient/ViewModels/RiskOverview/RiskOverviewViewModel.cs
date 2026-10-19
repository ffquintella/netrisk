using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Navigation;
using GUIClient.Tools.RiskIndex;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>A clickable breadcrumb segment, or the current node as text (§5.1).</summary>
public sealed class BreadcrumbItem(BreadcrumbSegment segment, ReactiveCommand<string, RxVoid> open)
{
    public string Key { get; } = segment.Key;
    public string Label { get; } = segment.Label;
    public bool IsCurrent { get; } = segment.IsCurrent;
    public bool IsLink => !IsCurrent;
    public ReactiveCommand<string, RxVoid> Open { get; } = open;
}

/// <summary>
/// The Cyber Risk Overview module (S40 §5.0–§5.2, §5.6, §5.13, §10.2; T251–T254) — read-only, its own archetype outside
/// IX-5 A–E. It reads the snapshot the server computed (never recomputes), resolves the root by the caller's scope, and
/// holds the page's one <see cref="DrillState"/> (§5.6.2): every panel — card, trend, categories, Explore, tabs, objects —
/// derives the key it reads from it. Panel reads queue on one gate per page (§8.1: two derived computations per user).
/// The data-quality and governance strips (T256) are always visible and follow the node like every panel.
/// </summary>
public class RiskOverviewViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskOverviewTitle"];
    public string StrSnapshot { get; } = Localizer["RiskOverviewSnapshot"];
    public string StrBackToLatest { get; } = Localizer["RiskOverviewBackToLatest"];
    public string StrProfileChipHint { get; } = Localizer["RiskOverviewProfileChipHint"];
    public string StrPilotChip { get; } = Localizer["RiskOverviewPilotChip"];
    public string StrMyScopeHint { get; } = Localizer["RiskState_MyScopeNoHeadline"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrRefreshIdle { get; } = Localizer["RiskOverviewRefresh"];
    private string StrLoading { get; } = Localizer["RiskOverviewLoading"];
    private string StrOrganization { get; } = Localizer["RiskOverviewOrganization"];
    private string StrMyScope { get; } = Localizer["RiskOverviewMyScope"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];
    private string StrDateWithoutSnapshot { get; } = Localizer["RiskOverviewDateWithoutSnapshot"];
    private string StrUnassigned { get; } = Localizer["RiskExploreUnassigned"];
    public string StrObjectsTitle { get; } = Localizer["RiskObjectsTitle"];
    public string StrChangesTitle { get; } = Localizer["RiskChangesTitle"];
    public string StrContributionsTitle { get; } = Localizer["RiskContributionsTitle"];
    public string StrQualityPanelTitle { get; } = Localizer["RiskQualityPanelTitle"];
    public string StrMonetaryTitle { get; } = Localizer["RiskMonetaryTitle"];
    public string StrTopRisksTitle { get; } = Localizer["RiskTopRisksTitle"];

    /// <summary>"Atualizar", or "Carregando…" while a load runs (§5.1: refresh disabled while busy).</summary>
    public string StrRefresh => IsBusy ? StrLoading : StrRefreshIdle;

    #endregion

    #region SERVICES

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();
    private INavigationService Navigation { get; } = GetService<INavigationService>();

    #endregion

    #region STATE

    /// <summary>
    /// The page's single drill-down state (§5.6.2): the node shown, the Explore tree's dimension and chips, the tab. A
    /// restricted scope settles the organization on its entity or on "scope". No other view-model keeps any of it.
    /// </summary>
    private DrillState _drill = DrillState.Start(null);

    /// <summary>The day shown; null is the latest snapshot.</summary>
    private DateOnly? _date;

    private DateOnly? _latestDate;

    /// <summary>A restricted scope with several entities: "Meu escopo" heads the breadcrumb (§5.1).</summary>
    private bool _myScopeRoot;
    private HashSet<DateOnly> _snapshotDays = [];
    private RiskIndexOverviewDto? _overview;
    private int _generation;
    private bool _applyingDate;
    private readonly Dictionary<int, RiskIndexProfileDto> _profiles = new();

    /// <summary>The last visited (node, day) overviews, so going back along the breadcrumb needs no request (§10.9).</summary>
    private readonly RecentCache<(string Node, DateOnly Day), RiskIndexOverviewDto> _overviews = new();

    /// <summary>Cancels the panels of the page shown when another is loaded.</summary>
    private CancellationTokenSource? _page;

    /// <summary>The page's request gate (§8.1), shared by Explore, the tabs and the object list.</summary>
    private SemaphoreSlim _pageGate = new(1, 1);

    #endregion

    public RiskOverviewViewModel()
    {
        ObjectList = new RiskObjectListViewModel { OpenDetail = row => OpenObjectDetailAsync(row.Object) };
        Headline = new RiskHeadlineViewModel(() => _ = LoadAsync(),
            link => ObjectList.ShowPresetAsync(link.ObjectFilter!, link.Text, null));
        Trend = new RiskTrendViewModel(day => _ = SelectDayAsync(day));
        Changes = new RiskChangesViewModel(OpenObjectDetailAsync);
        Contributions = new RiskContributionsViewModel(OpenObjectDetailAsync);
        Categories = new RiskCategoriesViewModel(ShowCategoryDetail);
        AssetTabs = new RiskAssetClassTabsViewModel(tab => _drill = _drill.SelectTab(tab),
            (tab, filter, label) => _ = ObjectList.ShowPresetAsync(filter, label, tab));
        Explore = new RiskExploreViewModel(ChooseDimension, AddFilter, RemoveFilter, key => _ = OpenChildAsync(key), GoUp);
        Explore.DimensionsLoaded += ObjectList.SetDimensions;
        Quality = new RiskQualityStripViewModel(OpenChildAsync, (filter, label) => ObjectList.ShowPresetAsync(filter, label, null));
        Governance = new RiskGovernanceIndicatorsViewModel(view => Navigation.NavigateTo(view));
        BtRefreshClicked = ReactiveCommand.CreateFromTask(RefreshAsync);
        BtBackToLatestClicked = ReactiveCommand.CreateFromTask(BackToLatestAsync);
        BtBreadcrumbClicked = ReactiveCommand.CreateFromTask<string>(OpenBreadcrumbAsync);
        BtScopeEntityClicked = ReactiveCommand.CreateFromTask<string>(OpenChildAsync);
        BtProfileInfoClicked = ReactiveCommand.CreateFromTask(ShowProfileInfoAsync);

        this.WhenAnyValue(vm => vm.IsBusy).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(StrRefresh));
            this.RaisePropertyChanged(nameof(IsRefreshEnabled));
        });
    }

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtRefreshClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtBackToLatestClicked { get; }
    public ReactiveCommand<string, RxVoid> BtBreadcrumbClicked { get; }

    /// <summary>An entity of "Meu escopo" (§5.1): opened as a child of the scope root, keeping the dimension.</summary>
    public ReactiveCommand<string, RxVoid> BtScopeEntityClicked { get; }

    public ReactiveCommand<RxVoid, RxVoid> BtProfileInfoClicked { get; }

    #endregion

    #region PROPERTIES

    public RiskHeadlineViewModel Headline { get; }

    /// <summary>The trend beside the card (§5.3; T252).</summary>
    public RiskTrendViewModel Trend { get; }

    /// <summary>The contributing categories (§5.4; T253).</summary>
    public RiskCategoriesViewModel Categories { get; }

    /// <summary>The asset-class tabs (§5.5; T253).</summary>
    public RiskAssetClassTabsViewModel AssetTabs { get; }

    /// <summary>The always-visible Explore panel (§5.6.1; T254).</summary>
    public RiskExploreViewModel Explore { get; }

    /// <summary>The object list of the node (§5.9.1; T254).</summary>
    public RiskObjectListViewModel ObjectList { get; }

    /// <summary>"O que mudou" of the node (§5.7; T255).</summary>
    public RiskChangesViewModel Changes { get; }

    /// <summary>The contributions and indicator sensitivity of the node (§5.8; T255).</summary>
    public RiskContributionsViewModel Contributions { get; }

    /// <summary>The always-visible data-quality strip and its detail (§5.12; T256).</summary>
    public RiskQualityStripViewModel Quality { get; }

    /// <summary>The always-visible governance and review indicators (§5.2.5; T256).</summary>
    public RiskGovernanceIndicatorsViewModel Governance { get; }

    /// <summary>The monetary panel in its interim, unavailable state (§5.10; T256).</summary>
    public RiskMonetaryPanelViewModel Monetary { get; } = new();

    /// <summary>The Top Risks placeholder (§5.11; T256).</summary>
    public RiskTopRisksPlaceholderViewModel TopRisks { get; } = new();

    /// <summary>The page's drill-down state, read-only to the outside (§5.6.2).</summary>
    public DrillState Drill => _drill;

    private string? _typeSeal;
    /// <summary>The type seal beside the title (§5.6.3): "Processo", "Classe de ativo: Dispositivos".</summary>
    public string? TypeSeal
    {
        get => _typeSeal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _typeSeal, value);
            this.RaisePropertyChanged(nameof(HasTypeSeal));
        }
    }

    public bool HasTypeSeal => !string.IsNullOrEmpty(TypeSeal);

    /// <summary>Re-reads the snapshot (never recomputes); disabled while a load runs (§5.1).</summary>
    public bool IsRefreshEnabled => !IsBusy;

    public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = [];

    /// <summary>"Meu escopo": several claimed entities, no headline, one link per entity (§5.1, S39 §10.4).</summary>
    public ObservableCollection<BreadcrumbItem> ScopeEntities { get; } = [];

    private bool _isMyScope;
    public bool IsMyScope { get => _isMyScope; private set => this.RaiseAndSetIfChanged(ref _isMyScope, value); }

    private bool _hasOverview;
    /// <summary>A snapshot was read: the top bar's chips and date picker apply.</summary>
    public bool HasOverview { get => _hasOverview; private set => this.RaiseAndSetIfChanged(ref _hasOverview, value); }

    private DateTime? _snapshotDate;
    /// <summary>The date picker's value. Picking a day without a snapshot is refused and the picker returns to the shown day.</summary>
    public DateTime? SnapshotDate
    {
        get => _snapshotDate;
        set
        {
            var previous = _snapshotDate;
            this.RaiseAndSetIfChanged(ref _snapshotDate, value);
            if (!_applyingDate && value != previous) _ = PickDateAsync(value);
        }
    }

    private DateTime? _firstSnapshotDate;
    public DateTime? FirstSnapshotDate { get => _firstSnapshotDate; private set => this.RaiseAndSetIfChanged(ref _firstSnapshotDate, value); }

    private DateTime? _lastSnapshotDate;
    public DateTime? LastSnapshotDate { get => _lastSnapshotDate; private set => this.RaiseAndSetIfChanged(ref _lastSnapshotDate, value); }

    private IReadOnlyList<(DateOnly From, DateOnly To)> _blackoutRanges = [];
    /// <summary>The days without a snapshot between the first and the last; the view applies them to the picker.</summary>
    public IReadOnlyList<(DateOnly From, DateOnly To)> BlackoutRanges
    {
        get => _blackoutRanges;
        private set => this.RaiseAndSetIfChanged(ref _blackoutRanges, value);
    }

    private string _computedAtText = string.Empty;
    public string ComputedAtText { get => _computedAtText; private set => this.RaiseAndSetIfChanged(ref _computedAtText, value); }

    private string _profileChipText = string.Empty;
    public string ProfileChipText { get => _profileChipText; private set => this.RaiseAndSetIfChanged(ref _profileChipText, value); }

    private string _modeText = string.Empty;
    public string ModeText { get => _modeText; private set => this.RaiseAndSetIfChanged(ref _modeText, value); }

    private string _modeHint = string.Empty;
    public string ModeHint { get => _modeHint; private set => this.RaiseAndSetIfChanged(ref _modeHint, value); }

    private string _provenanceText = string.Empty;
    public string ProvenanceText { get => _provenanceText; private set => this.RaiseAndSetIfChanged(ref _provenanceText, value); }

    private bool _isProvenanceWarning;
    public bool IsProvenanceWarning { get => _isProvenanceWarning; private set => this.RaiseAndSetIfChanged(ref _isProvenanceWarning, value); }

    private bool _showsPilotChip;
    public bool ShowsPilotChip { get => _showsPilotChip; private set => this.RaiseAndSetIfChanged(ref _showsPilotChip, value); }

    private bool _isHistory;
    public bool IsHistory { get => _isHistory; private set => this.RaiseAndSetIfChanged(ref _isHistory, value); }

    private string _historyChipText = string.Empty;
    public string HistoryChipText { get => _historyChipText; private set => this.RaiseAndSetIfChanged(ref _historyChipText, value); }

    private string? _suspendedText;
    /// <summary>"Publicação suspensa em dd/mm (perfil vN aposentado)" over the last snapshot (§5.13).</summary>
    public string? SuspendedText
    {
        get => _suspendedText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _suspendedText, value);
            this.RaisePropertyChanged(nameof(IsSuspended));
        }
    }

    public bool IsSuspended => !string.IsNullOrEmpty(SuspendedText);

    private string? _emptyStateText;
    /// <summary>The §5.13 state that replaces the card: shadow pilot, no approved profile, awaiting the first snapshot.</summary>
    public string? EmptyStateText
    {
        get => _emptyStateText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _emptyStateText, value);
            this.RaisePropertyChanged(nameof(HasEmptyState));
        }
    }

    public bool HasEmptyState => !string.IsNullOrEmpty(EmptyStateText);

    #endregion

    #region LOADING

    /// <summary>The first load, from the shell's first navigation. Never throws: failures become the panel's error state.</summary>
    public Task InitializeAsync() => LoadAsync(readLatest: true);

    /// <summary>"Atualizar" (§5.1): re-reads the latest snapshot and drops the cache of visited nodes (§10.9).</summary>
    private Task RefreshAsync()
    {
        _overviews.Clear();
        Explore.ClearCache();
        return LoadAsync(readLatest: true);
    }

    /// <summary>
    /// Loads the page of the drill state's node on the day shown. The latest snapshot is read on the first load and on
    /// "Atualizar"; moving along the drill-down reuses it, and an overview seen in the last 20 pages comes from the cache.
    /// </summary>
    private async Task LoadAsync(bool readLatest = false)
    {
        var generation = ++_generation;
        IsBusy = true;
        Headline.IsBusy = true;
        try
        {
            if (readLatest || _latestDate is null)
            {
                var latest = await RiskIndexService.GetLatestAsync();
                if (generation != _generation) return;

                ShowsPilotChip = RiskOverviewTopBar.ShowsPilotChip(latest?.Publication);
                if (latest is null)
                {
                    var profiles = await RiskIndexService.GetProfilesAsync();
                    if (generation != _generation) return;
                    ShowEmptyState(RiskOverviewTopBar.WithoutSnapshot(profiles, DateTime.UtcNow));
                    return;
                }

                _latestDate = latest.Date;
                await LoadSnapshotDaysAsync(latest.Date);
                if (generation != _generation) return;
            }

            var overview = await ReadOverviewAsync();
            if (generation != _generation || overview is null) return;

            var profile = await ProfileAsync(overview.Profile.Id);
            SuspendedText = await SuspensionAsync(overview);
            if (generation != _generation) return;

            Apply(overview, profile);
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            Logger.Error(ex, "Error loading the Cyber Risk Overview");
            Headline.ErrorMessage = Explain(ex);
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
                Headline.IsBusy = false;
            }
        }
    }

    /// <summary>
    /// The overview of the node shown, from the cache of visited nodes when that (node, day) was seen (§10.9). The
    /// organization needs global scope (S39 §10.4): refused, the root becomes the claimed entity when there is one, or "Meu
    /// escopo" when there are several. The shadow pilot is a state, not an error.
    /// </summary>
    private async Task<RiskIndexOverviewDto?> ReadOverviewAsync()
    {
        var day = _date ?? _latestDate;
        if (day is { } d && _overviews.TryGet((_drill.NodeText, d), out var cached)) return cached;

        try
        {
            return Remember(await RiskIndexService.GetOverviewAsync(NodeArgument, _date));
        }
        catch (InvalidHttpRequestException ex) when (ex.ErrorCode == RiskApiCodes.InShadow)
        {
            ShowEmptyState(RiskOverviewEmptyState.Shadow);
            return null;
        }
        catch (InvalidHttpRequestException ex) when (ex.ErrorCode == RiskApiCodes.OrgRequiresGlobalScope && _drill.Node.IsOrganization)
        {
            var scope = await RiskIndexService.GetOverviewAsync(RiskIndexNodeKey.Scope, _date);
            if (scope.ScopeEntities.Count != 1)
            {
                _myScopeRoot = true;
                _drill = _drill.OpenBreadcrumb(RiskIndexNodeKey.Scope);
                return Remember(scope);
            }

            _drill = _drill.OpenBreadcrumb(scope.ScopeEntities[0].Key);
            return Remember(await RiskIndexService.GetOverviewAsync(NodeArgument, _date));
        }
    }

    /// <summary>The <c>node</c> argument: null for the organization, so the server applies its default.</summary>
    private string? NodeArgument => _drill.Node.IsOrganization ? null : _drill.NodeText;

    private RiskIndexOverviewDto Remember(RiskIndexOverviewDto overview)
    {
        _overviews.Put((_drill.NodeText, overview.SnapshotDate), overview);
        return overview;
    }

    private async Task LoadSnapshotDaysAsync(DateOnly latest)
    {
        if (_snapshotDays.Contains(latest)) return;

        var days = await RiskIndexService.GetSnapshotDatesAsync();
        _snapshotDays = days.ToHashSet();
        _snapshotDays.Add(latest);
        var ordered = _snapshotDays.Order().ToList();
        FirstSnapshotDate = ordered[0].ToDateTime(TimeOnly.MinValue);
        LastSnapshotDate = ordered[^1].ToDateTime(TimeOnly.MinValue);
        BlackoutRanges = RiskOverviewTopBar.BlackoutRanges(ordered);
    }

    /// <summary>The snapshot's profile, for the arrow threshold and the indicative coverage; a failure only loses the numbers.</summary>
    private async Task<RiskIndexProfileDto?> ProfileAsync(int id)
    {
        if (_profiles.TryGetValue(id, out var cached)) return cached;
        try
        {
            var profile = await RiskIndexService.GetProfileAsync(id);
            _profiles[id] = profile;
            return profile;
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not read risk index profile {Id}: {Message}", id, ex.Message);
            return null;
        }
    }

    /// <summary>The suspension banner's text, or null; failing to read the versions only loses the banner, not the value.</summary>
    private async Task<string?> SuspensionAsync(RiskIndexOverviewDto overview)
    {
        if (overview.Profile.Status != RiskIndexProfileStatus.Retired) return null;

        try
        {
            var profiles = await RiskIndexService.GetProfilesAsync();
            return RiskOverviewTopBar.Suspended(overview.Profile, profiles, DateTime.UtcNow) is { } suspended
                ? string.Format(CultureInfo.CurrentCulture, Localizer["RiskState_PublicationSuspendedFMT"],
                    suspended.RetiredAtUtc?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "—", suspended.Version)
                : null;
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not read the risk index profiles for the suspension banner: {Message}", ex.Message);
            return null;
        }
    }

    private NodeLabels Labels => new(StrOrganization, StrMyScope, StrUnassigned);

    private void Apply(RiskIndexOverviewDto overview, RiskIndexProfileDto? profile)
    {
        var culture = CultureInfo.CurrentCulture;
        _overview = overview;
        EmptyStateText = null;
        HasOverview = true;

        ProfileChipText = RiskOverviewTopBar.ProfileChip(overview.Profile, RiskHeadlineViewModel.Text, culture);
        ModeText = Localizer[RiskOverviewTopBar.ModeKey(overview.Mode)];
        ModeHint = Localizer[RiskOverviewTopBar.ModeHintKey(overview.Mode)];
        ProvenanceText = Localizer[RiskOverviewTopBar.ProvenanceKey(overview.Provenance)];
        IsProvenanceWarning = RiskOverviewTopBar.ProvenanceIsWarning(overview.Provenance);
        ComputedAtText = string.Format(culture, Localizer["RiskOverviewComputedAtFMT"],
            overview.ComputedAtUtc.ToString("HH:mm", CultureInfo.InvariantCulture));

        IsHistory = RiskOverviewTopBar.IsHistory(overview.SnapshotDate, _latestDate);
        HistoryChipText = string.Format(culture, Localizer["RiskOverviewHistoryChipFMT"], overview.SnapshotDate.ToString("d", culture));
        SetPickerDate(overview.SnapshotDate);

        // The breadcrumb names the node's entities and then each of its filter terms (§5.1, §5.6.2).
        Replace(Breadcrumb, NodeKeyDisplay.Breadcrumb(overview, Labels, _myScopeRoot, RiskHeadlineViewModel.Text, culture)
            .Select(s => new BreadcrumbItem(s, BtBreadcrumbClicked)));
        TypeSeal = NodeKeyDisplay.TypeSeal(overview.Node, RiskHeadlineViewModel.Text, culture);

        IsMyScope = overview.Headline is null && overview.Node.Key == RiskIndexNodeKey.Scope;
        Replace(ScopeEntities, IsMyScope
            ? overview.ScopeEntities.Select(e => new BreadcrumbItem(new BreadcrumbSegment(e.Key, e.Label, false), BtScopeEntityClicked))
            : []);

        Headline.Apply(overview, profile?.Parameters.ArrowDelta, profile?.Parameters.CoverageIndicative);

        // A new page: the panels of the previous one stop, and the new ones queue on one gate (§8.1), each failing alone.
        _page?.Cancel();
        _page = new CancellationTokenSource();
        _pageGate = new SemaphoreSlim(1, 1);
        var token = _page.Token;
        Monetary.IsActive = TopRisks.IsActive = !IsMyScope;
        if (IsMyScope)
        {
            Quality.ShowMyScope();
            Governance.ShowMyScope();
        }
        else
        {
            // The strips sit right under the card (§5.0): their reads queue first.
            _ = Quality.LoadAsync(overview, profile?.Parameters.UnassignedThreshold, _pageGate, token);
            _ = Governance.LoadAsync(overview, _pageGate, token);
        }

        _ = Explore.LoadAsync(_drill, overview.SnapshotDate, overview, Labels, _pageGate, token);

        if (IsMyScope)
        {
            Trend.Clear();
            Categories.Clear();
            AssetTabs.Clear();
            ObjectList.Clear();
            Changes.Clear();
            Contributions.Clear();
            return;
        }

        Categories.Apply(overview);
        _ = Trend.LoadAsync(overview.Node.Key, overview.SnapshotDate, overview);
        _ = AssetTabs.LoadAsync(overview.Node.Key, overview.SnapshotDate, overview, _drill.ClassTab, _pageGate);
        _ = Changes.LoadAsync(overview.Node.Key, overview.SnapshotDate, overview.Provenance, _pageGate, token);
        _ = Contributions.LoadAsync(overview.Node.Key, overview.SnapshotDate, _pageGate, token);
        _ = ObjectList.LoadAsync(_drill.Node, overview.SnapshotDate, _pageGate, token);
    }

    private void ShowEmptyState(RiskOverviewEmptyState state)
    {
        EmptyStateText = state switch
        {
            RiskOverviewEmptyState.Shadow => Localizer["RiskState_Shadow"].Value,
            RiskOverviewEmptyState.NoApprovedProfile => Localizer["RiskState_NoApprovedProfile"].Value,
            RiskOverviewEmptyState.AwaitingFirstSnapshot => Localizer["RiskState_AwaitingFirstSnapshot"].Value,
            _ => null
        };
        _overview = null;
        HasOverview = false;
        IsMyScope = false;
        TypeSeal = null;
        Breadcrumb.Clear();
        ScopeEntities.Clear();
        _page?.Cancel();
        Headline.Clear();
        Trend.Clear();
        Categories.Clear();
        AssetTabs.Clear();
        Explore.Clear();
        ObjectList.Clear();
        Changes.Clear();
        Contributions.Clear();
        Quality.Clear();
        Governance.Clear();
        Monetary.IsActive = TopRisks.IsActive = false;
    }

    #endregion

    #region NAVIGATION

    /// <summary>A breadcrumb segment (§5.1, §5.6.2): an entity or a filter prefix of the node becomes the node; the chips clear.</summary>
    private Task OpenBreadcrumbAsync(string key) => Navigate(_drill.OpenBreadcrumb(key));

    /// <summary>A child of the tree or an entity of "Meu escopo" (§5.6.1): the whole module shows it (§5.6.3).</summary>
    private Task OpenChildAsync(string key) => Navigate(_drill.OpenChild(key));

    /// <summary>Alt+← (§5.6.1, §10.8): one breadcrumb level up; nothing above the root.</summary>
    private void GoUp()
    {
        if (NodeKeyDisplay.ParentKey(Breadcrumb.Select(b => new BreadcrumbSegment(b.Key, b.Label, b.IsCurrent)).ToList()) is { } parent)
            _ = OpenBreadcrumbAsync(parent);
    }

    /// <summary>The dimension selector (§5.6.2): only the tree is read again; a chip of the same kind leaves with a toast.</summary>
    private void ChooseDimension(RiskIndexDimension dimension)
    {
        var change = _drill.WithDimension(dimension);
        if (change.RemovedFilter is { } removed) Toasts.Info(Explore.FilterRemovedText(removed));
        ApplyDrill(change.State);
    }

    private void AddFilter(DrillFilter filter) => ApplyDrill(_drill.AddFilter(filter));

    private void RemoveFilter(DrillFilterKind kind) => ApplyDrill(_drill.RemoveFilter(kind));

    private void ApplyDrill(DrillState next) => _ = Navigate(next);

    /// <summary>A new state; what it reads again follows the precedence table of §5.6.2 (a tab reads its own content).</summary>
    private Task Navigate(DrillState next)
    {
        var reload = DrillState.Reload(_drill, next);
        _drill = next;
        if (reload.HasFlag(DrillReload.Page)) return LoadAsync();
        return reload.HasFlag(DrillReload.Tree) ? Explore.ApplyTreeAsync(next) : Task.CompletedTask;
    }

    /// <summary>A category's "›" (§5.4): the same node with the trend in its series and the object list filtered by it.</summary>
    private void ShowCategoryDetail(RiskIndexCategory category)
    {
        Trend.ShowSeries(category);
        _ = ObjectList.ShowCategoryAsync(category);
    }

    private Task BackToLatestAsync()
    {
        _date = null;
        return LoadAsync();
    }

    private Task PickDateAsync(DateTime? picked) =>
        picked is null ? Task.CompletedTask : SelectDayAsync(DateOnly.FromDateTime(picked.Value));

    /// <summary>
    /// Shows the dashboard as it was on <paramref name="day"/> (§5.1): from the date picker, or a click on a day of the
    /// trend (§10.4). A day without a snapshot is refused and the picker returns to the day shown.
    /// </summary>
    private Task SelectDayAsync(DateOnly day)
    {
        if (_overview is not null && day == _overview.SnapshotDate) return Task.CompletedTask;
        if (!_snapshotDays.Contains(day))
        {
            Toasts.Error(StrDateWithoutSnapshot);
            if (_overview is not null) SetPickerDate(_overview.SnapshotDate);
            return Task.CompletedTask;
        }

        _date = day == _latestDate ? null : day;
        return LoadAsync();
    }

    private void SetPickerDate(DateOnly day)
    {
        _applyingDate = true;
        try
        {
            SnapshotDate = day.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            _applyingDate = false;
        }
    }

    /// <summary>
    /// The object detail (§5.9.2; T255) from a row of "O que mudou", the contributions or the object list. The dialog closes
    /// with the destination chosen and the module navigates (IX-2) — the service refuses a module the user may not open.
    /// </summary>
    private async Task OpenObjectDetailAsync(RiskIndexObjectLabelDto obj)
    {
        if (_overview is null) return;
        var result = await DialogService.ShowDialogAsync<RiskIndexObjectDetailDialogResult, RiskIndexObjectDetailDialogParameter>(
            nameof(RiskIndexObjectDetailDialogViewModel),
            new RiskIndexObjectDetailDialogParameter
            {
                Kind = obj.Object.Kind, Reference = obj.Object.Ref, Node = _overview.Node.Key, Date = _overview.SnapshotDate
            });
        if (result?.Target is { } target) Navigation.NavigateTo(target);
    }

    private async Task ShowProfileInfoAsync()
    {
        if (_overview is null) return;
        await DialogService.ShowDialogAsync<DialogResultBase, RiskProfileInfoDialogParameter>(
            nameof(RiskProfileInfoDialogViewModel),
            new RiskProfileInfoDialogParameter { ProfileId = _overview.Profile.Id });
    }

    #endregion

    /// <summary>
    /// A refusal reads as its <c>RiskApi_‹code›MSG</c>, else the server's sentence (§10.5); a transport failure as the
    /// module's own sentence — the exception text is logged, never shown (NRM §5.3.5).
    /// </summary>
    private string Explain(Exception ex) => ex switch
    {
        InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal =>
            RiskApiErrorText.Translate(code, refusal.Message, RiskHeadlineViewModel.Text, StrLoadError),
        DataNotFoundException => ExplainError(ex),
        _ => StrLoadError
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
