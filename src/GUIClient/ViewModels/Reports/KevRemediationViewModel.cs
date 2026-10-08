using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using Model.ExploitationSignals;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Reports;

/// <summary>One feed's synchronization state, in the words report 10 shows.</summary>
public sealed class SignalFeedStatusRow(string name, string lastRun, string error, string lastApplied)
{
    public string Name { get; } = name;
    public string LastRun { get; } = lastRun;
    public string Error { get; } = error;
    public bool HasError { get; } = !string.IsNullOrWhiteSpace(error);
    public string LastApplied { get; } = lastApplied;
}

/// <summary>
/// Report 10, "Time to remediate KEV items" (Stage 9.4, T302, S45 §7.5; the Phase 7 metric): open KEV
/// findings, those past the CISA due date, mitigated ones and the median days to mitigate — "not computable"
/// when no mitigated finding has a date, never 0 — with the state of both synchronizations, so a feed that
/// failed is visible without SQL.
/// </summary>
public class KevRemediationViewModel : ReportsViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["KevRemediationReport"];
    public string StrOpenFindings { get; } = Localizer["KevOpenFindings"];
    public string StrOpenPastDue { get; } = Localizer["KevOpenPastDue"];
    public string StrMitigated { get; } = Localizer["KevMitigatedFindings"];
    public string StrMitigatedWithoutDate { get; } = Localizer["KevMitigatedWithoutDate"];
    public string StrMedian { get; } = Localizer["KevMedianDaysToMitigate"];
    public string StrSyncStatus { get; } = Localizer["SignalSyncStatus"];
    public string StrReload { get; } = Localizer["Reload"];

    private string StrMedianFormat { get; } = Localizer["KevMedianDays"];
    private string StrMedianNotComputable { get; } = Localizer["KevMedianNotComputable"];
    private string StrComputedAtFormat { get; } = Localizer["MetricComputedAt"];
    private string StrKevAppliedAtFormat { get; } = Localizer["KevAppliedAt"];
    private string StrKevNeverApplied { get; } = Localizer["KevNeverApplied"];
    private string StrLastRunFormat { get; } = Localizer["SignalFeedLastRun"];
    private string StrNeverRunFormat { get; } = Localizer["SignalFeedNeverRun"];
    private string StrLastAppliedFormat { get; } = Localizer["SignalFeedLastApplied"];
    private string StrNeverApplied { get; } = Localizer["SignalFeedNeverApplied"];
    private string StrErrorFormat { get; } = Localizer["SignalFeedError"];

    #endregion

    private IExploitationSignalsService SignalsService { get; } = GetService<IExploitationSignalsService>();

    #region PROPERTIES

    private bool _loaded;

    private KevRemediationMetricDto? _metric;
    public KevRemediationMetricDto? Metric
    {
        get => _metric;
        private set
        {
            this.RaiseAndSetIfChanged(ref _metric, value);
            foreach (var name in new[] { nameof(HasMetric), nameof(OpenText), nameof(OpenPastDueText),
                         nameof(MitigatedText), nameof(MitigatedWithoutDateText), nameof(MedianText),
                         nameof(ComputedAtText), nameof(CatalogueText) })
                this.RaisePropertyChanged(name);
        }
    }

    public bool HasMetric => Metric is not null;

    // Counts of findings: a 0 the server sent is a real zero.
    public string OpenText => Metric?.OpenKevFindings.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    public string OpenPastDueText => Metric?.OpenPastCisaDueDate.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    public string MitigatedText => Metric?.MitigatedKevFindings.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    public string MitigatedWithoutDateText => Metric?.MitigatedWithoutDate.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    public string MedianText => Metric is null
        ? string.Empty
        : ExploitationSignalsSummary.MedianDays(Metric.MedianDaysToMitigate, StrMedianNotComputable, StrMedianFormat);

    public string ComputedAtText => Metric is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, StrComputedAtFormat,
            ExploitationSignalsSummary.Instant(Metric.ComputedAt, string.Empty));

    public string CatalogueText => Metric is null
        ? string.Empty
        : Metric.KevCatalogueAppliedAt is { } at
            ? string.Format(CultureInfo.CurrentCulture, StrKevAppliedAtFormat, ExploitationSignalsSummary.Instant(at, string.Empty))
            : StrKevNeverApplied;

    public ObservableCollection<SignalFeedStatusRow> Feeds { get; } = [];

    #endregion

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    public KevRemediationViewModel()
    {
        BtReloadClicked = ReactiveCommand.CreateFromTask(LoadAsync);
    }

    /// <summary>Loads the report the first time it is shown; Reload is what refreshes it.</summary>
    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : LoadAsync();

    public async Task LoadAsync()
    {
        try
        {
            await WithBusyAsync(async () =>
            {
                var metric = await SignalsService.GetKevRemediationMetricAsync();
                var status = await SignalsService.GetStatusAsync();

                Feeds.Clear();
                Feeds.Add(FeedRow(status.Kev));
                Feeds.Add(FeedRow(status.Epss));

                Metric = metric;
                _loaded = true;
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading the KEV remediation metric");
            Toasts.Error(ExplainError(ex));
        }
    }

    private SignalFeedStatusRow FeedRow(SignalFeedStatusDto feed)
    {
        string name = Localizer[ExploitationSignalsSummary.FeedKey(feed.Feed)];

        var lastRun = feed.LastRun is { } run
            ? string.Format(CultureInfo.CurrentCulture, StrLastRunFormat, name,
                ExploitationSignalsSummary.Instant(run.FinishedAt, string.Empty),
                Localizer[ExploitationSignalsSummary.OutcomeKey(run.Outcome)])
            : string.Format(CultureInfo.CurrentCulture, StrNeverRunFormat, name);

        // The reason is the one NetRisk wrote for the run (S45 §4.4) — never the remote body.
        var error = string.IsNullOrWhiteSpace(feed.LastRun?.Error)
            ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, StrErrorFormat, feed.LastRun!.Error);

        var lastApplied = feed.LastApplied is { } applied
            ? string.Format(CultureInfo.CurrentCulture, StrLastAppliedFormat,
                ExploitationSignalsSummary.Instant(applied.FinishedAt, string.Empty), applied.Received, applied.Added,
                applied.Updated, applied.Delisted, applied.DelistingsHeld, applied.FindingsUpdated)
            : StrNeverApplied;

        return new SignalFeedStatusRow(name, lastRun, error, lastApplied);
    }
}
