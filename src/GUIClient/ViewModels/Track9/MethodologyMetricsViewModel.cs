using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.Track9;
using Model.Monitoring;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class MethodologyMetricRow(MethodologyMetricDto item, string availabilityLabel)
{
    public string Stage => item.Stage;
    public string Name => $"{item.Code} — {item.Name}";
    public string AvailabilityLabel { get; } = availabilityLabel;
    public string ValueText => item.Value is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(item.Unit)
            ? item.Value.Value.ToString("G4", CultureInfo.CurrentCulture)
            : $"{item.Value.Value.ToString("G4", CultureInfo.CurrentCulture)} {item.Unit}";
    public string TermsText => item.Numerator is null && item.Denominator is null
        ? string.Empty
        : $"{item.Numerator?.ToString("G4", CultureInfo.CurrentCulture) ?? "—"} / "
          + $"{item.Denominator?.ToString("G4", CultureInfo.CurrentCulture) ?? "—"}";
    public string Detail => item.Detail;
    public string? Source => item.Source;
}

/// <summary>The ten Phase 7 methodology metrics, including M10, plus KRI/queue health (T306).</summary>
public sealed class MethodologyMetricsViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring = GetService<IMonitoringService>();
    private readonly Track9MonitoringLoadGate _gate = new();
    private bool _loaded;
    private string _loadError = string.Empty;
    private MethodologyMetricsDto? _panel;

    public string StrTitle { get; } = Localizer["Track9MethodologyMetricsTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrComputedAt { get; } = Localizer["Track9ComputedAt"];
    public string StrAvailability { get; } = Localizer["Track9Availability"];
    public string StrValue { get; } = Localizer["Track9MetricValue"];
    public string StrTerms { get; } = Localizer["Track9MetricTerms"];
    public string StrDetails { get; } = Localizer["Details"];
    public string StrSource { get; } = Localizer["Source"];
    public string StrStage { get; } = Localizer["Track9Stage"];
    public string StrKriHealth { get; } = Localizer["Track9KriHealth"];
    public string StrReassessmentHealth { get; } = Localizer["Track9ReassessmentHealth"];
    public string StrActive { get; } = Localizer["Track9Active"];
    public string StrWithin { get; } = Localizer["Track9WithinTolerance"];
    public string StrWarning { get; } = Localizer["Warning"];
    public string StrBreached { get; } = Localizer["Track9Breached"];
    public string StrStale { get; } = Localizer["Track9Stale"];
    public string StrNoReading { get; } = Localizer["Track9NoReading"];
    public string StrRetired { get; } = Localizer["Retired"];
    public string StrPending { get; } = Localizer["Track9Pending"];
    public string StrAnswered { get; } = Localizer["Track9Answered"];
    public string StrRiskClosed { get; } = Localizer["Track9RiskClosed"];
    public string StrMeanDays { get; } = Localizer["Track9MeanDaysToAnswer"];

    public MethodologyMetricsDto? Panel
    {
        get => _panel;
        private set
        {
            this.RaiseAndSetIfChanged(ref _panel, value);
            Metrics = new ObservableCollection<MethodologyMetricRow>((value?.Metrics ?? [])
                .Select(x => new MethodologyMetricRow(x, Localizer[AvailabilityKey(x.Availability)])));
            this.RaisePropertyChanged(nameof(HasPanel));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(ComputedAtText));
        }
    }

    public ObservableCollection<MethodologyMetricRow> Metrics { get; private set; } = [];
    public bool HasPanel => Panel is not null;
    public bool IsEmpty => _loaded && Metrics.Count == 0 && !HasLoadError;
    public string ComputedAtText => Panel?.ComputedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;

    public string LoadError
    {
        get => _loadError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _loadError, value);
            this.RaisePropertyChanged(nameof(HasLoadError));
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    public MethodologyMetricsViewModel()
    {
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
    }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : ReloadAsync();
    public Task ReloadAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var token = _gate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var result = await _monitoring.GetMetricsAsync();
                if (!_gate.IsCurrent(token)) return;
                Panel = result;
                this.RaisePropertyChanged(nameof(Metrics));
                LoadError = string.Empty;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            });
        }
        catch (Exception ex)
        {
            if (!_gate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading the Track 9 methodology metrics");
            LoadError = ExplainError(ex);
        }
    }

    private static string AvailabilityKey(MetricAvailability availability) => availability switch
    {
        MetricAvailability.Available => "Track9MetricAvailable",
        MetricAvailability.Partial => "Track9MetricPartial",
        MetricAvailability.NotAvailable => "Track9MetricNotAvailable",
        _ => "Track9MetricNotAvailable"
    };
}
