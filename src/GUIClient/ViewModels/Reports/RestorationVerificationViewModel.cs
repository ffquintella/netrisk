using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using Model.Continuity;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Reports;

/// <summary>One active process or service as report 8 lists it, in the words the user reads.</summary>
public sealed class RestorationVerificationRow
{
    public RestorationVerificationRow(ContinuitySubjectDto subject, Func<string, string> localize, Func<int?, string> duration)
    {
        Name = subject.Name;
        Type = localize(subject.DefinitionName);
        Criticality = subject.EffectiveCriticality?.ToString(CultureInfo.CurrentCulture) ?? localize("CriticalityNotDeclared");
        Rto = duration(subject.RtoMinutes);
        RtoStatus = localize(ContinuitySummary.StatusKey(subject.RtoStatus));
        Rpo = duration(subject.RpoMinutes);
        RpoStatus = localize(ContinuitySummary.StatusKey(subject.RpoStatus));
        Conflicts = subject.CascadeConflictCount;
        Threat = ContinuitySummary.Weight(subject.ThreatWeight);
    }

    public string Name { get; }
    public string Type { get; }
    public string Criticality { get; }
    public string Rto { get; }
    public string RtoStatus { get; }
    public string Rpo { get; }
    public string RpoStatus { get; }
    public int Conflicts { get; }
    public string Threat { get; }
}

/// <summary>
/// Report 8, "Restoration tested vs declared RTO/RPO" (Stage 9.3, S43 §7; the Phase 7 metric): of the
/// declared objectives, how many a valid restoration test met, for everything and for critical
/// processes, with the weighted count of threatened critical processes.
///
/// "Not computable" is its own state, not 0 %; a subject without a BIA is counted apart, never in a
/// denominator; statuses are words, never colours.
/// </summary>
public class RestorationVerificationViewModel : ReportsViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["Restoration tested vs declared RTO/RPO"];
    public string StrAllSubjects { get; } = Localizer["AllSubjects"];
    public string StrCriticalProcesses { get; } = Localizer["CriticalProcesses"];
    public string StrName { get; } = Localizer["Name"];
    public string StrSubjectType { get; } = Localizer["SubjectType"];
    public string StrEffectiveCriticality { get; } = Localizer["EffectiveCriticality"];
    public string StrRto { get; } = Localizer["Rto"];
    public string StrRpo { get; } = Localizer["Rpo"];
    public string StrRtoVerification { get; } = Localizer["RtoVerification"];
    public string StrRpoVerification { get; } = Localizer["RpoVerification"];
    public string StrCascadeConflicts { get; } = Localizer["CascadeConflicts"];
    public string StrContinuityThreat { get; } = Localizer["ContinuityThreat"];
    public string StrReload { get; } = Localizer["Reload"];

    private string StrSummaryFormat { get; } = Localizer["RestorationVerificationSummary"];
    private string StrNotComputable { get; } = Localizer["RestorationVerificationNotComputable"];
    private string StrWithoutBiaFormat { get; } = Localizer["SubjectsWithoutBia"];
    private string StrThreatenedFormat { get; } = Localizer["ThreatenedCriticalProcessesSummary"];
    private string StrAbsent { get; } = Localizer["BiaAbsent"];

    #endregion

    private IContinuityService ContinuityService { get; } = GetService<IContinuityService>();

    #region PROPERTIES

    private bool _loaded;

    private RestorationVerificationMetricDto? _metric;
    public RestorationVerificationMetricDto? Metric
    {
        get => _metric;
        set
        {
            this.RaiseAndSetIfChanged(ref _metric, value);
            foreach (var name in new[] { nameof(AllRtoText), nameof(AllRpoText), nameof(CriticalRtoText),
                         nameof(CriticalRpoText), nameof(WithoutBiaText), nameof(ThreatenedText) })
                this.RaisePropertyChanged(name);
        }
    }

    private string Summary(string objective, ObjectiveVerificationSummaryDto? summary) =>
        summary?.MetRatio is { } ratio
            ? string.Format(CultureInfo.CurrentCulture, StrSummaryFormat, objective, summary.Met, summary.Declared,
                (ratio * 100m).ToString("0.0", CultureInfo.CurrentCulture))
            : string.Format(CultureInfo.CurrentCulture, StrNotComputable, objective);

    public string AllRtoText => Summary(StrRto, Metric?.All.Rto);
    public string AllRpoText => Summary(StrRpo, Metric?.All.Rpo);
    public string CriticalRtoText => Summary(StrRto, Metric?.CriticalProcesses.Rto);
    public string CriticalRpoText => Summary(StrRpo, Metric?.CriticalProcesses.Rpo);

    public string WithoutBiaText => string.Format(CultureInfo.CurrentCulture, StrWithoutBiaFormat,
        Metric?.All.SubjectsWithoutBia ?? 0);

    public string ThreatenedText => string.Format(CultureInfo.CurrentCulture, StrThreatenedFormat,
        Metric?.ThreatenedCriticalProcessesConfirmed ?? 0, Metric?.ThreatenedCriticalProcessesUnverifiedOnly ?? 0,
        (Metric?.ThreatenedCriticalProcessesWeighted ?? 0m).ToString("0.00", CultureInfo.CurrentCulture));

    public ObservableCollection<RestorationVerificationRow> Rows { get; } = [];

    #endregion

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    public RestorationVerificationViewModel()
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
                var metric = await ContinuityService.GetRestorationVerificationMetricAsync();

                Rows.Clear();
                foreach (var row in metric.Rows)
                    Rows.Add(new RestorationVerificationRow(row, key => Localizer[key],
                        minutes => ContinuitySummary.Duration(minutes, StrAbsent)));

                Metric = metric;
                _loaded = true;
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading the restoration verification metric");
            Toasts.Error(ExplainError(ex));
        }
    }
}
