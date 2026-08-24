using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports.Graphs;

/// <summary>
/// The SLA-compliance-by-severity widget (Track 3 milestone 3.4.2).
///
/// A table rather than a chart. The question this answers is "how many criticals are past their
/// deadline", and four rows of numbers answer it faster and more precisely than a bar chart of four
/// bars — the percentage alone hides whether it is two findings or two hundred.
/// </summary>
public class SlaComplianceViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrSlaCompliance { get; } = Localizer["SlaCompliance"];
    public string StrSeverity { get; } = Localizer["Severity"];
    public string StrWithinSla { get; } = Localizer["WithinSla"];
    public string StrBreached { get; } = Localizer["Breached"];

    #endregion

    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();

    public ObservableCollection<SlaComplianceRow> Rows { get; } = new();

    private string _summary = "";

    /// <summary>The one-line headline: how many open findings are past their deadline overall.</summary>
    public string Summary
    {
        get => _summary;
        set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            var buckets = await VulnerabilitiesService.GetSlaComplianceAsync();

            Rows.Clear();

            // Descending, so Critical is the first thing read.
            foreach (var bucket in buckets.OrderByDescending(b => b.Severity))
                Rows.Add(new SlaComplianceRow
                {
                    SeverityName = SeverityName(bucket.Severity),
                    Total = bucket.Total,
                    WithinSla = bucket.WithinSla,
                    Breached = bucket.Breached,
                    // An empty band shows a dash rather than 100%: no findings is an absence of
                    // data, not a perfect score.
                    Compliance = bucket.CompliancePercent == null
                        ? "—"
                        : $"{bucket.CompliancePercent.Value:0.#}%"
                });

            var breached = buckets.Sum(b => b.Breached);
            var total = buckets.Sum(b => b.Total);

            Summary = total == 0
                ? Localizer["NoDataMSG"]
                : $"{breached} / {total}";
        }
        catch (Exception ex)
        {
            // A server that predates Track 3 has no compliance endpoint; the widget stays empty
            // rather than breaking the dashboard.
            Logger.Warning("Could not load SLA compliance: {Message}", ex.Message);
        }
    }

    private static string SeverityName(int severity) => severity switch
    {
        4 => "Critical",
        3 => "High",
        2 => "Medium",
        1 => "Low",
        _ => "None"
    };
}

/// <summary>One severity band's row in the widget.</summary>
public class SlaComplianceRow
{
    public string SeverityName { get; set; } = "";

    public int Total { get; set; }

    public int WithinSla { get; set; }

    public int Breached { get; set; }

    public string Compliance { get; set; } = "";
}
