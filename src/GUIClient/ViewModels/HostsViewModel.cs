using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Extensions;
using GUIClient.Models;
using GUIClient.Tools;
using GUIClient.Tools.Hosts;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model;
using Model.DTO;
using Model.Exceptions;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// The Hosts view (S38): a faceted, paged host list on the left, and on the right a header card
/// over five tabs — Vulnerabilities, Overview, Services, History, Comments.
///
/// Everything the view shows is assigned on the UI thread. The previous version loaded a host's
/// details from a fire-and-forget <c>Task.Run</c> inside the <see cref="SelectedHost"/> setter and
/// assigned the collections from the thread pool; it also let a slow response for a host the user
/// had already moved past overwrite the panes of the one now selected. Loads now run as async
/// methods started on the UI thread, marshal their results back with <see cref="Dispatcher.UIThread"/>,
/// and are discarded when a newer request has started (<see cref="_selectionVersion"/>,
/// <see cref="_listVersion"/>).
/// </summary>
public class HostsViewModel : ViewModelBase
{
    public const int PageSize = 100;

    #region LANGUAGE

    public string StrHosts { get; } = Localizer["Hosts"];
    public string StrSearchHosts { get; } = Localizer["HostsSearchWatermark"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrTeam { get; } = Localizer["Team"];
    public string StrCriticality { get; } = Localizer["Criticality"];
    public string StrEnvironment { get; } = Localizer["Environment"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrEdit { get; } = Localizer["Edit"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrExport { get; } = Localizer["Export"];
    public string StrPrevious { get; } = Localizer["Previous"];
    public string StrNext { get; } = Localizer["Next"];
    public string StrSelectHost { get; } = Localizer["HostsSelectHostMSG"];

    public string StrOpenVulnerabilities { get; } = Localizer["OpenVulnerabilities"];
    public string StrCritical { get; } = Localizer["Critical"];
    public string StrHigh { get; } = Localizer["High"];
    public string StrMedium { get; } = Localizer["Medium"];
    public string StrLow { get; } = Localizer["Low"];

    public string StrVulnerabilities { get; } = Localizer["Vulnerabilities"];
    public string StrOverview { get; } = Localizer["Overview"];
    public string StrServices { get; } = Localizer["Services"];
    public string StrHistory { get; } = Localizer["History"];
    public string StrComments { get; } = Localizer["Comments"];

    public string StrId { get; } = Localizer["Id"];
    public string StrTitle { get; } = Localizer["Title"];
    public string StrScore { get; } = Localizer["Score"];
    public string StrSeverity { get; } = Localizer["Severity"];
    public string StrFirstDetection { get; } = Localizer["FirstDetection"];
    public string StrLastDetection { get; } = Localizer["LastDetection"];
    public string StrDetectionCount { get; } = Localizer["DetectionCount"];
    public string StrFixTeam { get; } = Localizer["FixTeam"];
    public string StrAnalyst { get; } = Localizer["Analyst"];

    public string StrHostName { get; } = Localizer["HostName"];
    public string StrIp { get; } = Localizer["IP"];
    public string StrFqdn { get; } = Localizer["FQDN"];
    public string StrMacAddress { get; } = Localizer["MacAddress"];
    public string StrOperatingSystem { get; } = Localizer["OperatingSystem"];
    public string StrOsVersion { get; } = Localizer["OsVersion"];
    public string StrRegistrationDate { get; } = Localizer["RegistrationDate"];
    public string StrLastVerification { get; } = Localizer["LastVerificationDate"];
    public string StrSource { get; } = Localizer["Source"];
    public string StrExternalId { get; } = Localizer["ExternalId"];
    public string StrExternalProvider { get; } = Localizer["ExternalProvider"];
    public string StrResponsibleTeam { get; } = Localizer["ResponsibleTeam"];
    public string StrOwner { get; } = Localizer["Owner"];
    public string StrRiskScore { get; } = Localizer["RiskScore"];
    public string StrRiskScoreSource { get; } = Localizer["RiskScoreSource"];
    public string StrRiskScoreUpdatedAt { get; } = Localizer["RiskScoreUpdatedAt"];
    public string StrComment { get; } = Localizer["Comment"];

    public string StrName { get; } = Localizer["Name"];
    public string StrProtocol { get; } = Localizer["Protocol"];
    public string StrPort { get; } = Localizer["Port"];
    public string StrNoServices { get; } = Localizer["HostsNoServicesMSG"];

    public string StrField { get; } = Localizer["Field"];
    public string StrActor { get; } = Localizer["Actor"];
    public string StrSince { get; } = Localizer["Since"];
    public string StrClear { get; } = Localizer["Clear"];
    public string StrHistoryEmpty { get; } = Localizer["HostHistoryEmptyMSG"];

    public string StrSend { get; } = Localizer["Send"];

    #endregion

    #region SERVICES

    private IMainWindowProvider MainWindowProvider { get; } = GetService<IMainWindowProvider>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private ICommentsService CommentsService { get; } = GetService<ICommentsService>();
    private ITeamsService TeamsService { get; } = GetService<ITeamsService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IImpactsService ImpactsService { get; } = GetService<IImpactsService>();
    private IMutableConfigurationService MutableConfigurationService { get; } = GetService<IMutableConfigurationService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();
    private readonly IExportClientService _exportService;

    #endregion

    #region FIELDS

    private bool _initialized;
    private bool _initializing;

    /// <summary>
    /// False until the view is attached. Until then a SelectedIndex write is the TabControl picking
    /// its own first tab while it loads, not the user, and must neither move nor overwrite the
    /// stored tab.
    /// </summary>
    private bool _viewReady;

    /// <summary>Bumped by every host-list request; a response for an older one is dropped.</summary>
    private int _listVersion;

    /// <summary>Bumped by every selection; a detail response for an older one is dropped.</summary>
    private int _selectionVersion;

    private int _page = 1;
    private int _totalHosts;

    private Dictionary<int, string> _teamNames = new();
    private Dictionary<int, string> _analystNames = new();
    private Dictionary<int, string> _severityNames = new();
    private List<AuditLog> _historyEntries = new();

    #endregion

    public HostsViewModel()
    {
        _exportService = GetService<IExportClientService>();
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);

        StatusOptions =
        [
            new FilterOption(Localizer["FilterAll"]),
            new FilterOption(Localizer["Active"], (int)IntStatus.Active),
            new FilterOption(Localizer["Retired"], (int)IntStatus.Retired)
        ];
        _selectedStatusOption = StatusOptions[0];

        CriticalityOptions = [new FilterOption(Localizer["FilterAll"])];
        for (var level = CriticalityScale.Lowest; level <= CriticalityScale.Highest; level++)
            CriticalityOptions.Add(new FilterOption(
                level.ToString(CultureInfo.InvariantCulture) + HostSummaryLine.Separator +
                Localizer[CriticalityScale.LabelKey(level)], level));
        CriticalityOptions.Add(new FilterOption(Localizer["CriticalityNotSet"], HostsFilterComposer.CriticalityNotSet));
        _selectedCriticalityOption = CriticalityOptions[0];

        _teamOptions = [new FilterOption(Localizer["FilterAll"])];
        _selectedTeamOption = _teamOptions[0];
        _environmentOptions = [new FilterOption(Localizer["FilterAll"])];
        _selectedEnvironmentOption = _environmentOptions[0];
        _historyFieldOptions = [new FilterOption(Localizer["FilterAll"])];
        _selectedHistoryField = _historyFieldOptions[0];
        _historyActorOptions = [new FilterOption(Localizer["FilterAll"])];
        _selectedHistoryActor = _historyActorOptions[0];

        RestoreLayout();

        // S38 §3.1: the search box is permanent and debounced at 500 ms (it was 2 s behind a toggle).
        // Throttle emits on the thread pool, so the reload is posted back to the UI thread.
        this.WhenAnyValue(x => x.SearchText)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .DistinctUntilChanged()
            .Subscribe(_ => Dispatcher.UIThread.Post(() =>
            {
                if (_initialized) ReloadFirstPage();
            }));

        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            if (AuthenticationService.AuthenticatedUserInfo == null) return;
            if (AuthenticationService.AuthenticatedUserInfo.UserPermissions == null) return;
            if (AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("hosts"))
                Dispatcher.UIThread.Post(() => { _ = InitializeAsync(); });
        };
    }

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> ExportCommand { get; }

    #endregion

    #region PROPERTIES — host list

    private ObservableCollection<Host> _hostsList = new();
    public ObservableCollection<Host> HostsList
    {
        get => _hostsList;
        set => this.RaiseAndSetIfChanged(ref _hostsList, value);
    }

    private Host? _selectedHost;
    public Host? SelectedHost
    {
        get => _selectedHost;
        set
        {
            if (ReferenceEquals(_selectedHost, value)) return;

            this.RaiseAndSetIfChanged(ref _selectedHost, value);
            RaiseHeaderChanged();
            _ = LoadSelectedHostAsync(value);
        }
    }

    public bool HasSelectedHost => SelectedHost != null;
    public bool HasNoSelectedHost => SelectedHost == null;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value ?? string.Empty);
    }

    /// <summary>Bumped by Ctrl+F; the search box focuses itself when it changes (FocusOnSignal).</summary>
    private int _searchFocusSignal;
    public int SearchFocusSignal
    {
        get => _searchFocusSignal;
        set => this.RaiseAndSetIfChanged(ref _searchFocusSignal, value);
    }

    public List<FilterOption> StatusOptions { get; }

    private FilterOption _selectedStatusOption;
    public FilterOption? SelectedStatusOption
    {
        get => _selectedStatusOption;
        set => SetFacet(ref _selectedStatusOption, value);
    }

    private ObservableCollection<FilterOption> _teamOptions;
    public ObservableCollection<FilterOption> TeamOptions
    {
        get => _teamOptions;
        set => this.RaiseAndSetIfChanged(ref _teamOptions, value);
    }

    private FilterOption _selectedTeamOption;
    public FilterOption? SelectedTeamOption
    {
        get => _selectedTeamOption;
        set => SetFacet(ref _selectedTeamOption, value);
    }

    public List<FilterOption> CriticalityOptions { get; }

    private FilterOption _selectedCriticalityOption;
    public FilterOption? SelectedCriticalityOption
    {
        get => _selectedCriticalityOption;
        set => SetFacet(ref _selectedCriticalityOption, value);
    }

    private ObservableCollection<FilterOption> _environmentOptions;
    public ObservableCollection<FilterOption> EnvironmentOptions
    {
        get => _environmentOptions;
        set => this.RaiseAndSetIfChanged(ref _environmentOptions, value);
    }

    private FilterOption _selectedEnvironmentOption;
    public FilterOption? SelectedEnvironmentOption
    {
        get => _selectedEnvironmentOption;
        set => SetFacet(ref _selectedEnvironmentOption, value);
    }

    private string _strPageRange = string.Empty;
    public string StrPageRange
    {
        get => _strPageRange;
        set => this.RaiseAndSetIfChanged(ref _strPageRange, value);
    }

    private bool _isPreviousPageEnabled;
    public bool IsPreviousPageEnabled
    {
        get => _isPreviousPageEnabled;
        set => this.RaiseAndSetIfChanged(ref _isPreviousPageEnabled, value);
    }

    private bool _isPagingEnabled = true;
    /// <summary>False while a list load is in flight, so a double-click cannot skip a page.</summary>
    public bool IsPagingEnabled
    {
        get => _isPagingEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isPagingEnabled, value);
    }

    private bool _isNextPageEnabled;
    public bool IsNextPageEnabled
    {
        get => _isNextPageEnabled;
        set => this.RaiseAndSetIfChanged(ref _isNextPageEnabled, value);
    }

    #endregion

    #region PROPERTIES — layout

    private double _leftPaneWidth = HostsViewLayout.DefaultLeftPaneWidth;

    /// <summary>
    /// The host list's width. The view applies it to its first column and reports drags back
    /// through <see cref="RememberLeftPaneWidth"/>: a ColumnDefinition is not a control, so its
    /// Width cannot carry a binding to this property.
    /// </summary>
    public double LeftPaneWidth
    {
        get => _leftPaneWidth;
        private set => this.RaiseAndSetIfChanged(ref _leftPaneWidth, value);
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            var index = value is >= 0 and < HostsViewLayout.TabCount ? value : 0;
            if (index == _selectedTabIndex || !_viewReady) return;

            this.RaiseAndSetIfChanged(ref _selectedTabIndex, index);
            Persist(HostsViewLayout.SelectedTabKey, index.ToString(CultureInfo.InvariantCulture));
        }
    }

    #endregion

    #region PROPERTIES — header card

    private HostVulnerabilitySummaryDto? _selectedHostSummary;
    public HostVulnerabilitySummaryDto? SelectedHostSummary
    {
        get => _selectedHostSummary;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedHostSummary, value);
            this.RaisePropertyChanged(nameof(OpenCritical));
            this.RaisePropertyChanged(nameof(OpenHigh));
            this.RaisePropertyChanged(nameof(OpenMedium));
            this.RaisePropertyChanged(nameof(OpenLow));
        }
    }

    public int OpenCritical => SelectedHostSummary?.Open.Critical ?? 0;
    public int OpenHigh => SelectedHostSummary?.Open.High ?? 0;
    public int OpenMedium => SelectedHostSummary?.Open.Medium ?? 0;
    public int OpenLow => SelectedHostSummary?.Open.Low ?? 0;

    public string StrSelectedHostStatus => SelectedHost == null ? string.Empty : StatusLabel(SelectedHost.Status);
    public bool IsSelectedHostActive => SelectedHost?.Status == (short)IntStatus.Active;
    public bool IsSelectedHostRetired => SelectedHost?.Status == (short)IntStatus.Retired;
    public bool HasSelectedHostEnvironment => !string.IsNullOrWhiteSpace(SelectedHost?.Environment);
    public string StrSelectedHostTeam => TeamName(SelectedHost?.TeamId);

    /// <summary><c>IP · FQDN · OS</c> (S38 §3.2).</summary>
    public string StrSelectedHostAddressLine => SelectedHost == null
        ? string.Empty
        : HostSummaryLine.Join(SelectedHost.Ip, SelectedHost.Fqdn,
            HostSummaryLine.Join(SelectedHost.Os, SelectedHost.OsVersion));

    /// <summary><c>Team · Owner · Risk score</c> (S38 §3.2).</summary>
    public string StrSelectedHostOwnershipLine => SelectedHost == null
        ? string.Empty
        : HostSummaryLine.Join(
            HostSummaryLine.Labelled(StrTeam, StrSelectedHostTeam),
            HostSummaryLine.Labelled(StrOwner, SelectedHost.Owner),
            HostSummaryLine.Labelled(StrRiskScore, SelectedHost.RiskScore?.ToString(CultureInfo.CurrentCulture)));

    #endregion

    #region PROPERTIES — tabs

    private ObservableCollection<HostVulnerabilityRow> _selectedHostVulnerabilities = new();
    public ObservableCollection<HostVulnerabilityRow> SelectedHostVulnerabilities
    {
        get => _selectedHostVulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _selectedHostVulnerabilities, value);
    }

    private HostVulnerabilityRow? _selectedVulnerability;

    /// <summary>
    /// The highlighted finding. Double-click deliberately does nothing: the Vulnerabilities view
    /// can be navigated to but not opened on one finding (S38 §3.3), so the row stays selectable.
    /// </summary>
    public HostVulnerabilityRow? SelectedVulnerability
    {
        get => _selectedVulnerability;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerability, value);
    }

    private ObservableCollection<HostServiceRow> _selectedHostServices = new();
    public ObservableCollection<HostServiceRow> SelectedHostServices
    {
        get => _selectedHostServices;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedHostServices, value);
            this.RaisePropertyChanged(nameof(HasNoServices));
        }
    }

    public bool HasNoServices => SelectedHostServices.Count == 0;

    private ObservableCollection<HostHistoryEntry> _historyGroups = new();
    public ObservableCollection<HostHistoryEntry> HistoryGroups
    {
        get => _historyGroups;
        set
        {
            this.RaiseAndSetIfChanged(ref _historyGroups, value);
            this.RaisePropertyChanged(nameof(IsHistoryEmpty));
        }
    }

    public bool IsHistoryEmpty => HistoryGroups.Count == 0;

    private ObservableCollection<FilterOption> _historyFieldOptions;
    public ObservableCollection<FilterOption> HistoryFieldOptions
    {
        get => _historyFieldOptions;
        set => this.RaiseAndSetIfChanged(ref _historyFieldOptions, value);
    }

    private FilterOption _selectedHistoryField;
    public FilterOption? SelectedHistoryField
    {
        get => _selectedHistoryField;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedHistoryField)) return;
            _selectedHistoryField = value;
            this.RaisePropertyChanged();
            RebuildHistory();
        }
    }

    private ObservableCollection<FilterOption> _historyActorOptions;
    public ObservableCollection<FilterOption> HistoryActorOptions
    {
        get => _historyActorOptions;
        set => this.RaiseAndSetIfChanged(ref _historyActorOptions, value);
    }

    private FilterOption _selectedHistoryActor;
    public FilterOption? SelectedHistoryActor
    {
        get => _selectedHistoryActor;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedHistoryActor)) return;
            _selectedHistoryActor = value;
            this.RaisePropertyChanged();
            RebuildHistory();
        }
    }

    private DateTimeOffset? _historySince;
    public DateTimeOffset? HistorySince
    {
        get => _historySince;
        set
        {
            if (Nullable.Equals(value, _historySince)) return;
            this.RaiseAndSetIfChanged(ref _historySince, value);
            RebuildHistory();
        }
    }

    private ObservableCollection<Comment> _hostComments = new();
    public ObservableCollection<Comment> HostComments
    {
        get => _hostComments;
        set => this.RaiseAndSetIfChanged(ref _hostComments, value);
    }

    private string _newComment = "";
    public string NewComment
    {
        get => _newComment;
        set => this.RaiseAndSetIfChanged(ref _newComment, value);
    }

    #endregion

    #region METHODS — initialisation and layout

    private Task InitializeAsync() => WithBusyAsync(async () =>
    {
        if (_initialized || _initializing) return;
        _initializing = true;

        try
        {
            await LoadLookupsAsync();
            _initialized = true;
            await LoadHostsAsync(resetPage: true);
        }
        finally
        {
            _initializing = false;
        }
    });

    /// <summary>
    /// The listings the list and its facets are labelled from: teams (facet, header, fix-team
    /// column), analysts, the severity names and the environments facet. One request each, rather
    /// than the per-cell lookups the old grid made; each one failing leaves its labels as ids.
    /// </summary>
    private async Task LoadLookupsAsync()
    {
        var teamsTask = Fetch(() => TeamsService.GetAllAsync(), new List<Team>(), "teams");
        var usersTask = Fetch(() => UsersService.GetAllAsync(), new List<UserListing>(), "analysts");
        var impactsTask = Fetch(() => ImpactsService.GetAllAsync(), new List<Model.Globalization.LocalizableListItem>(), "severities");
        var environmentsTask = Fetch(() => HostsService.GetEnvironmentsAsync(), new List<string>(), "environments");

        await Task.WhenAll(teamsTask, usersTask, impactsTask, environmentsTask);

        var teams = teamsTask.Result;
        _teamNames = teams.GroupBy(t => t.Value).ToDictionary(g => g.Key, g => g.First().Name);
        _analystNames = usersTask.Result.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);
        _severityNames = impactsTask.Result.GroupBy(i => i.Key)
            .ToDictionary(g => g.Key, g => g.First().LocalizedValue ?? g.First().Value ?? string.Empty);

        var teamOptions = new ObservableCollection<FilterOption> { new(Localizer["FilterAll"]) };
        foreach (var team in teams.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            teamOptions.Add(new FilterOption(team.Name, team.Value));
        TeamOptions = teamOptions;
        Reselect(ref _selectedTeamOption, teamOptions, nameof(SelectedTeamOption));

        ApplyEnvironments(environmentsTask.Result);
    }

    private void ApplyEnvironments(IEnumerable<string> environments)
    {
        var options = new ObservableCollection<FilterOption> { new(Localizer["FilterAll"]) };
        foreach (var environment in environments.Where(e => !string.IsNullOrWhiteSpace(e)))
            options.Add(new FilterOption(environment, Text: environment));
        EnvironmentOptions = options;
        Reselect(ref _selectedEnvironmentOption, options, nameof(SelectedEnvironmentOption));
    }

    /// <summary>
    /// Points a facet at the refilled list's instance of the option it held (or "All" when that
    /// option is gone). The ComboBox nulls its selection when its items are replaced and the facet
    /// setter ignores that write, so without this the box would render blank over a live filter.
    /// </summary>
    private void Reselect(ref FilterOption field, IList<FilterOption> options, string propertyName)
    {
        var current = field;
        field = options.FirstOrDefault(o => o.Number == current.Number && o.Text == current.Text) ?? options[0];
        this.RaisePropertyChanged(propertyName);
    }

    private void RestoreLayout()
    {
        try
        {
            LeftPaneWidth = HostsViewLayout.ParseWidth(
                MutableConfigurationService.GetConfigurationValue(HostsViewLayout.LeftPaneWidthKey));
            _selectedTabIndex = HostsViewLayout.ParseTab(
                MutableConfigurationService.GetConfigurationValue(HostsViewLayout.SelectedTabKey));
            this.RaisePropertyChanged(nameof(SelectedTabIndex));
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not restore the Hosts view layout: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Called by the view once it is in the visual tree: from here on a tab change is the user's
    /// and is remembered. Re-announces the stored tab, so a TabControl that auto-selected its first
    /// item while loading moves to it.
    /// </summary>
    public void OnViewReady()
    {
        if (_viewReady) return;

        _viewReady = true;
        this.RaisePropertyChanged(nameof(SelectedTabIndex));
    }

    /// <summary>Called by the view when the host-list splitter moves (drag or arrow keys).</summary>
    public void RememberLeftPaneWidth(double width)
    {
        var clamped = HostsViewLayout.ClampWidth(width);
        if (Math.Abs(clamped - _leftPaneWidth) < 1) return;

        _leftPaneWidth = clamped;
        Persist(HostsViewLayout.LeftPaneWidthKey, HostsViewLayout.FormatWidth(clamped));
    }

    private void Persist(string key, string value)
    {
        try
        {
            MutableConfigurationService.SetConfigurationValue(key, value);
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not save {Key}: {Message}", key, ex.Message);
        }
    }

    #endregion

    #region METHODS — host list

    private void SetFacet(ref FilterOption field, FilterOption? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        // A ComboBox writes null into its binding whenever its items are refilled. That is the
        // control losing its selection, not the user choosing "All", so it is ignored.
        if (value is null || ReferenceEquals(value, field)) return;

        field = value;
        this.RaisePropertyChanged(propertyName);

        if (_initialized) _ = LoadHostsAsync(resetPage: true);
    }

    /// <summary>The filter the list is loaded with, from the search box and the four facets.</summary>
    public string CurrentFilter() => HostsFilterComposer.Compose(
        SearchText,
        _selectedStatusOption.Number,
        _selectedTeamOption.Number,
        _selectedCriticalityOption.Number,
        _selectedEnvironmentOption.Text);

    /// <param name="resetPage">Load page 1 (a filter changed).</param>
    /// <param name="targetPage">
    /// The page to load when not resetting; defaults to the page on screen. <c>_page</c> only moves
    /// to it when the response is applied, so a failed or superseded load leaves the page alone.
    /// </param>
    private async Task LoadHostsAsync(bool resetPage, int? targetPage = null)
    {
        var page = resetPage ? 1 : targetPage ?? _page;
        var version = ++_listVersion;
        var filter = CurrentFilter();
        IsPagingEnabled = false;

        try
        {
            var (items, total) = await HostsService.GetFilteredAsync(PageSize, page, filter, IHostsService.DefaultSort);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _listVersion) return;

                _page = page;
                var keepId = SelectedHost?.Id;
                HostsList = new ObservableCollection<Host>(items);
                _totalHosts = total;
                UpdatePaging();

                SelectedHost = keepId is { } id ? HostsList.FirstOrDefault(h => h.Id == id) : null;
            });
        }
        catch (BadFilterException ex)
        {
            Logger.Warning("Host filter refused: {Message}", ex.Message);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == _listVersion) Toasts.Warning(Localizer["InvalidFilter"] + ": " + ex.Message);
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Loading hosts failed: {Message}", ex.Message);
        }
        finally
        {
            // Only the newest request re-enables paging; an older one finishing must not.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == _listVersion) IsPagingEnabled = true;
            });
        }
    }

    private void UpdatePaging()
    {
        var range = PageRange.Of(_page, PageSize, _totalHosts, HostsList.Count);
        _totalHosts = range.Total;
        StrPageRange = string.Format(CultureInfo.CurrentCulture, Localizer["HostsPageRangeMSG"],
            range.First, range.Last, range.Total);
        IsPreviousPageEnabled = range.HasPrevious;
        IsNextPageEnabled = range.HasNext;
    }

    private void ReloadFirstPage() => _ = LoadHostsAsync(resetPage: true);

    public void BtPreviousPageClicked()
    {
        if (!IsPagingEnabled || _page <= 1) return;
        _ = LoadHostsAsync(resetPage: false, targetPage: _page - 1);
    }

    public void BtNextPageClicked()
    {
        if (!IsPagingEnabled || !IsNextPageEnabled) return;
        _ = LoadHostsAsync(resetPage: false, targetPage: _page + 1);
    }

    public void BtFocusSearchClicked() => SearchFocusSignal++;

    /// <summary>Reloads the current page with the current filters, and refreshes the environment facet.</summary>
    public async void BtReloadHostsClicked()
    {
        try
        {
            if (!_initialized)
            {
                await InitializeAsync();
                return;
            }

            ApplyEnvironments(await Fetch(() => HostsService.GetEnvironmentsAsync(), new List<string>(), "environments"));
            await LoadHostsAsync(resetPage: false);
        }
        catch (Exception ex) { Logger.Error("BtReloadHostsClicked failed: {Message}", ex.Message); }
    }

    public async void BtAddHostClicked()
    {
        try
        {
            var parameter = new HostDialogParameter { Operation = OperationType.Create };
            var result = await DialogService.ShowDialogAsync<HostDialogResult, HostDialogParameter>(
                nameof(EditHostDialogViewModel), parameter);

            if (result is not { Action: ResultActions.Ok } || result.ResultingHost == null) return;

            HostsList.Add(result.ResultingHost);
            _totalHosts++;
            UpdatePaging();
            SelectedHost = result.ResultingHost;
        }
        catch (Exception ex) { Logger.Error("BtAddHostClicked failed: {Message}", ex.Message); }
    }

    public async void BtEditHostClicked()
    {
        try
        {
            if (SelectedHost == null) return;

            var parameter = new HostDialogParameter { Operation = OperationType.Edit, Host = SelectedHost };
            var result = await DialogService.ShowDialogAsync<HostDialogResult, HostDialogParameter>(
                nameof(EditHostDialogViewModel), parameter);

            if (result is not { Action: ResultActions.Ok } || result.ResultingHost == null) return;

            var idx = HostsList.IndexOf(SelectedHost);
            if (idx >= 0) HostsList[idx] = result.ResultingHost;

            // A new instance, so the header, Overview and History reload against what was saved.
            _selectedHost = null;
            SelectedHost = result.ResultingHost;
        }
        catch (Exception ex) { Logger.Error("BtEditHostClicked failed: {Message}", ex.Message); }
    }

    public async void BtDeleteHostClicked()
    {
        try
        {
            if (SelectedHost == null) return;

            if (!await ConfirmationDialog.ConfirmDeleteAsync(SelectedHost.HostName,
                    Localizer["DeleteHostCascadeMSG"])) return;

            var host = SelectedHost;
            HostsService.Delete(host.Id);

            HostsList.Remove(host);
            SelectedHost = null;
            _totalHosts = Math.Max(0, _totalHosts - 1);
            UpdatePaging();
        }
        catch (Exception ex) { Logger.Error("BtDeleteHostClicked failed: {Message}", ex.Message); }
    }

    private async Task ExportAsync()
    {
        var owner = MainWindowProvider.GetActiveWindow();

        var format = await ExportFileSaver.PickFormatAsync(
            owner,
            Localizer["Export"],
            Localizer["Choose the export format"]);

        if (format is null) return;

        // The export carries the same filter as the list on screen, facets included.
        var data = await _exportService.ExportAsync("Host", format.Value, CurrentFilter());

        await ExportFileSaver.SaveAsync(owner, format.Value, data);
    }

    #endregion

    #region METHODS — selected host

    private void RaiseHeaderChanged()
    {
        this.RaisePropertyChanged(nameof(HasSelectedHost));
        this.RaisePropertyChanged(nameof(HasNoSelectedHost));
        this.RaisePropertyChanged(nameof(StrSelectedHostStatus));
        this.RaisePropertyChanged(nameof(IsSelectedHostActive));
        this.RaisePropertyChanged(nameof(IsSelectedHostRetired));
        this.RaisePropertyChanged(nameof(HasSelectedHostEnvironment));
        this.RaisePropertyChanged(nameof(StrSelectedHostTeam));
        this.RaisePropertyChanged(nameof(StrSelectedHostAddressLine));
        this.RaisePropertyChanged(nameof(StrSelectedHostOwnershipLine));
    }

    private void ClearDetail()
    {
        SelectedHostSummary = null;
        SelectedHostVulnerabilities = new ObservableCollection<HostVulnerabilityRow>();
        SelectedHostServices = new ObservableCollection<HostServiceRow>();
        HostComments = new ObservableCollection<Comment>();
        _historyEntries = new List<AuditLog>();
        ResetHistoryFilters();
        RebuildHistory();
    }

    /// <summary>
    /// Loads everything the detail panes show for <paramref name="host"/>: five requests in
    /// parallel, each failing on its own (a 404 on the summary must not blank the comments), then
    /// one assignment pass on the UI thread — skipped entirely if the user selected another host
    /// in the meantime.
    /// </summary>
    private async Task LoadSelectedHostAsync(Host? host)
    {
        var version = ++_selectionVersion;

        if (host == null)
        {
            ClearDetail();
            return;
        }

        var servicesTask = Fetch(() => HostsService.GetAllHostServiceAsync(host.Id), new List<HostsService>(), "services");
        var vulnerabilitiesTask = Fetch(() => HostsService.GetAllHostVulnerabilitiesAsync(host.Id), new List<Vulnerability>(), "vulnerabilities");
        var summaryTask = Fetch<HostVulnerabilitySummaryDto?>(async () => await HostsService.GetVulnerabilitySummaryAsync(host.Id), null, "vulnerability summary");
        var commentsTask = Fetch(() => CommentsService.GetHostCommentsAsync(host.Id), new List<Comment>(), "comments");
        var historyTask = Fetch(() => HostsService.GetHistoryAsync(host.Id), new List<AuditLog>(), "history");

        await Task.WhenAll(servicesTask, vulnerabilitiesTask, summaryTask, commentsTask, historyTask);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != _selectionVersion) return;

            var vulnerabilities = vulnerabilitiesTask.Result;

            SelectedHostSummary = summaryTask.Result;
            SelectedHostVulnerabilities = new ObservableCollection<HostVulnerabilityRow>(
                HostVulnerabilityRows.Build(vulnerabilities, SeverityLabel, TeamName, AnalystName));
            SelectedHostServices = new ObservableCollection<HostServiceRow>(
                HostServiceRows.Build(servicesTask.Result, vulnerabilities));
            HostComments = new ObservableCollection<Comment>(commentsTask.Result);

            _historyEntries = historyTask.Result;
            ResetHistoryFilters();
            RebuildHistory();
        });
    }

    private async Task<T> Fetch<T>(Func<Task<T>> call, T fallback, string what)
    {
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            Logger.Warning("Loading host {What} failed: {Message}", what, ex.Message);
            return fallback;
        }
    }

    private string TeamName(int? teamId) =>
        teamId is { } id ? _teamNames.GetValueOrDefault(id) ?? id.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private string AnalystName(int? analystId) =>
        analystId is { } id ? _analystNames.GetValueOrDefault(id) ?? id.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private string SeverityLabel(string? severity) =>
        _severityNames.GetValueOrDefault(SeverityScale.Rank(severity)) ?? severity ?? string.Empty;

    private static string StatusLabel(short status) => status switch
    {
        (short)IntStatus.Active => Localizer["Active"],
        (short)IntStatus.Retired => Localizer["Retired"],
        _ => ((IntStatus)status).StatusString()
    };

    #endregion

    #region METHODS — history

    private void ResetHistoryFilters()
    {
        var fields = new ObservableCollection<FilterOption> { new(Localizer["FilterAll"]) };
        foreach (var field in HostHistory.Fields(_historyEntries))
            fields.Add(new FilterOption(HostHistory.FieldLabelKey(field) is { } key ? Localizer[key] : field, Text: field));

        var actors = new ObservableCollection<FilterOption> { new(Localizer["FilterAll"]) };
        foreach (var actor in HostHistory.Actors(_historyEntries))
            actors.Add(new FilterOption(actor, Text: actor));

        HistoryFieldOptions = fields;
        _selectedHistoryField = fields[0];
        this.RaisePropertyChanged(nameof(SelectedHistoryField));

        HistoryActorOptions = actors;
        _selectedHistoryActor = actors[0];
        this.RaisePropertyChanged(nameof(SelectedHistoryActor));

        _historySince = null;
        this.RaisePropertyChanged(nameof(HistorySince));
    }

    private void RebuildHistory()
    {
        DateTime? sinceUtc = HistorySince is { } since
            ? new DateTime(since.Year, since.Month, since.Day, 0, 0, 0, DateTimeKind.Local).ToUniversalTime()
            : null;

        var groups = HostHistory.Group(_historyEntries, _selectedHistoryField.Text, _selectedHistoryActor.Text, sinceUtc);

        HistoryGroups = new ObservableCollection<HostHistoryEntry>(HostHistory.Present(
            groups,
            key => Localizer[key],
            FormatHistoryValue,
            utc => utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
    }

    /// <summary>A stored value as the user would recognise it: a team id as the team, a level as its name.</summary>
    private string? FormatHistoryValue(string field, string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        return field switch
        {
            nameof(Host.TeamId) when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var team)
                => TeamName(team),
            nameof(Host.Criticality) when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
                => CriticalityScale.LongLabel(level, Localizer[CriticalityScale.LabelKey(level)]),
            nameof(Host.Status) when short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var status)
                => StatusLabel(status),
            _ => value
        };
    }

    public void BtClearHistorySinceClicked() => HistorySince = null;

    #endregion

    #region METHODS — comments

    public async void BtSendCommentClicked()
    {
        try
        {
            var text = NewComment?.Trim();
            if (SelectedHost == null || SelectedHost.Id == 0 || string.IsNullOrEmpty(text)) return;

            var user = AuthenticationService.AuthenticatedUserInfo;

            var comment = await CommentsService.CreateCommentAsync(new Comment
            {
                Date = DateTime.Now,
                UserId = user?.UserId,
                CommenterName = user?.UserName,
                Type = "Host",
                HostId = SelectedHost.Id,
                Text = text,
                IsAnonymous = false
            });

            HostComments.Add(comment);
            NewComment = "";
        }
        catch (Exception ex) { Logger.Error("BtSendCommentClicked failed: {Message}", ex.Message); }
    }

    #endregion
}
