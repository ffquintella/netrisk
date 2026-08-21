using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Dashboard;
using Model.Exceptions;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// Administrator Master Dashboard (Track 2 milestone 2.3.3): one posture card per business
/// entity plus organisation-wide totals.
///
/// The whole payload arrives in a single call — the milestone spec rules out a request per
/// entity — so this view-model does no aggregation of its own beyond formatting.
/// </summary>
public class MasterDashboardViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle => Localizer["Master Dashboard"];
    public string StrSubtitle => Localizer["Cross-entity posture overview"];
    public string StrRefresh => Localizer["Refresh"];
    public string StrEntity => Localizer["Entity"];
    public string StrOpenRisks => Localizer["Open Risks"];
    public string StrOpenVulnerabilities => Localizer["Open Vulnerabilities"];
    public string StrOpenIncidents => Localizer["Open Incidents"];
    public string StrAverageRiskScore => Localizer["Average Risk Score"];
    public string StrPosture => Localizer["Posture"];
    public string StrHigh => Localizer["High"];
    public string StrMedium => Localizer["Medium"];
    public string StrLow => Localizer["Low"];
    public string StrCritical => Localizer["Critical"];
    public string StrTotals => Localizer["All entities"];
    public string StrNoData => Localizer["No entity data available"];
    public string StrNotAuthorized => Localizer["You need administrator rights to view this dashboard"];
    public string StrLoadError => Localizer["The master dashboard could not be loaded"];

    #endregion

    #region PROPERTIES

    private IDashboardService DashboardService { get; } = GetService<IDashboardService>();

    private ObservableCollection<EntityPostureSummary> _entities = new();
    public ObservableCollection<EntityPostureSummary> Entities
    {
        get => _entities;
        set => this.RaiseAndSetIfChanged(ref _entities, value);
    }

    private EntityPostureSummary? _totals;
    public EntityPostureSummary? Totals
    {
        get => _totals;
        set => this.RaiseAndSetIfChanged(ref _totals, value);
    }

    private EntityPostureSummary? _selectedEntity;
    public EntityPostureSummary? SelectedEntity
    {
        get => _selectedEntity;
        set => this.RaiseAndSetIfChanged(ref _selectedEntity, value);
    }

    private string? _lastUpdated;
    public string? LastUpdated
    {
        get => _lastUpdated;
        set => this.RaiseAndSetIfChanged(ref _lastUpdated, value);
    }

    /// <summary>
    /// Set when the load failed. Shown in place of the cards rather than as a MessageBox: an
    /// unreachable dashboard is a state of the view, not an event needing acknowledgement (IX-4).
    /// </summary>
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private bool _hasLoaded;
    public bool HasLoaded
    {
        get => _hasLoaded;
        set => this.RaiseAndSetIfChanged(ref _hasLoaded, value);
    }

    public bool IsEmpty => HasLoaded && !HasError && Entities.Count == 0;

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }

    #endregion

    public MasterDashboardViewModel()
    {
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
    }

    /// <summary>
    /// First load. Cheap to call repeatedly — the shell calls it every time the view is shown,
    /// and the server's own cache absorbs the repeat within its window.
    /// </summary>
    public async Task InitializeAsync() => await LoadAsync(refresh: false);

    private async Task RefreshAsync() => await LoadAsync(refresh: true);

    private async Task LoadAsync(bool refresh)
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var dashboard = await DashboardService.GetMasterDashboardAsync(refresh);

                Entities = new ObservableCollection<EntityPostureSummary>(dashboard.Entities);
                Totals = dashboard.Totals;
                ErrorMessage = null;

                // GeneratedAt is UTC on the wire; show it in the operator's own clock.
                LastUpdated = dashboard.GeneratedAt.ToLocalTime().ToString("g");

                this.RaisePropertyChanged(nameof(IsEmpty));
            }
            catch (InvalidHttpRequestException ex)
            {
                // The endpoint is admin-only, so this is the expected outcome for a non-admin
                // rather than a fault worth alarming about.
                Logger.Warning("Master dashboard refused: {Message}", ex.Message);
                ErrorMessage = StrNotAuthorized;
                Clear();
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading the master dashboard: {Message}", ex.Message);
                ErrorMessage = StrLoadError;
                Clear();
            }
            finally
            {
                HasLoaded = true;
            }
        });
    }

    private void Clear()
    {
        Entities = new ObservableCollection<EntityPostureSummary>();
        Totals = null;
        this.RaisePropertyChanged(nameof(IsEmpty));
    }
}
