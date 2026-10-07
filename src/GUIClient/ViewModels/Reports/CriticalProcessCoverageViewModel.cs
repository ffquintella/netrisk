using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using Model.Risks.Chain;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Reports;

/// <summary>
/// Report 7, "Critical process coverage" (Stage 9.1, S41 §7; Phase 7 of the methodology): of the
/// active processes declared critical (4 or 5), how many have at least one open risk linked to them
/// directly or by inference, and which risks those are.
///
/// "Not computable" is a state of its own, not 0 % — no process has been marked critical yet — and
/// "covered" is text, never colour. The counts are the caller's scope; a restricted caller is told the
/// result is partial.
/// </summary>
public class CriticalProcessCoverageViewModel : ReportsViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["Critical process coverage"];
    public string StrCoverageScopeRestricted { get; } = Localizer["CoverageScopeRestricted"];
    public string StrProcess { get; } = Localizer["ChainLevelProcess"];
    public string StrCriticality { get; } = Localizer["Criticality"];
    public string StrCriticalitySource { get; } = Localizer["CriticalitySource"];
    public string StrDirectRisks { get; } = Localizer["DirectRisks"];
    public string StrInferredRisks { get; } = Localizer["InferredRisks"];
    public string StrCovered { get; } = Localizer["Covered"];
    public string StrYes { get; } = Localizer["Yes"];
    public string StrNo { get; } = Localizer["No"];
    public string StrChainVia { get; } = Localizer["ChainVia"];
    public string StrRisksOfSelectedProcess { get; } = Localizer["RisksOfSelectedProcess"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrId { get; } = Localizer["Id"];
    public string StrSubject { get; } = Localizer["Subject"];
    public string StrStatus { get; } = Localizer["Status"];

    private string StrSummaryFormat { get; } = Localizer["CriticalProcessCoverageSummary"];
    private string StrNotComputable { get; } = Localizer["CriticalProcessCoverageNotComputable"];
    private string StrWithoutCriticalityFormat { get; } = Localizer["ProcessesWithoutCriticality"];

    #endregion

    #region SERVICES

    private IRiskChainService ChainService { get; } = GetService<IRiskChainService>();

    #endregion

    #region PROPERTIES

    private bool _loaded;

    private CriticalProcessCoverageDto? _coverage;
    public CriticalProcessCoverageDto? Coverage
    {
        get => _coverage;
        set
        {
            this.RaiseAndSetIfChanged(ref _coverage, value);
            this.RaisePropertyChanged(nameof(SummaryText));
            this.RaisePropertyChanged(nameof(WithoutCriticalityText));
            this.RaisePropertyChanged(nameof(HasProcessesWithoutCriticality));
            this.RaisePropertyChanged(nameof(IsScopeRestricted));
        }
    }

    /// <summary>"N of M critical processes covered (P%)", or "not computable".</summary>
    public string SummaryText => RiskChainSummary.Coverage(Coverage, StrSummaryFormat, StrNotComputable);

    public bool HasProcessesWithoutCriticality => Coverage?.ProcessesWithoutCriticality > 0;

    public string WithoutCriticalityText => string.Format(CultureInfo.CurrentCulture, StrWithoutCriticalityFormat,
        Coverage?.ProcessesWithoutCriticality ?? 0);

    public bool IsScopeRestricted => Coverage?.IsScopeRestricted == true;

    public ObservableCollection<CriticalProcessCoverageRow> Rows { get; } = [];

    private CriticalProcessCoverageRow? _selectedRow;
    public CriticalProcessCoverageRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            _ = LoadSelectedProcessRisksAsync(value);
        }
    }

    public ObservableCollection<RiskChainMatchRow> SelectedProcessRisks { get; } = [];

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    #endregion

    public CriticalProcessCoverageViewModel()
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
                var coverage = await ChainService.GetCriticalProcessCoverageAsync();

                Rows.Clear();
                foreach (var row in coverage.Rows)
                    Rows.Add(new CriticalProcessCoverageRow(row, StrYes, StrNo,
                        Localizer[ContinuitySummary.SourceKey(row.CriticalitySource)]));

                Coverage = coverage;
                SelectedRow = null;
                _loaded = true;
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading the critical-process coverage");
            Toasts.Error(ExplainError(ex));
        }
    }

    /// <summary>The risks of the selected process, inferred ones included, each with the node it came via.</summary>
    private async Task LoadSelectedProcessRisksAsync(CriticalProcessCoverageRow? row)
    {
        SelectedProcessRisks.Clear();
        if (row is null) return;

        try
        {
            var matches = await ChainService.GetRisksByEntityAsync(row.ProcessId, inferred: true);

            // A later selection wins over a slow earlier answer.
            if (!ReferenceEquals(SelectedRow, row)) return;

            foreach (var match in matches.OrderBy(m => m.Inferred).ThenBy(m => m.RiskId))
                SelectedProcessRisks.Add(new RiskChainMatchRow(match));
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the risks of process {Id}: {Message}", row.ProcessId, ex.Message);
            Toasts.Error(ExplainError(ex));
        }
    }
}
