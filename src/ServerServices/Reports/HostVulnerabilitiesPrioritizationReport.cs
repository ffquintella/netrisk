using DAL.Entities;
using Microsoft.Extensions.Localization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ServerServices.Services;

namespace ServerServices.Reports;

public class HostVulnerabilitiesPrioritizationReport(Report report, IStringLocalizer localizer, IDalService dalService): TemplatedPdfReport(report, localizer, dalService)
{
    public int BodyFontSize { get; set; } = 12;

    private static readonly Color TableBlue = Color.FromHex("#6464C8");

    protected override void ComposeBody(ColumnDescriptor body)
    {
        using var dbContext = DalService.GetContext();

        // Read once for both the table and the narrative (it used to be queried twice), and grouped
        // sequentially: the PLINQ (.AsParallel()) this used to run blocked its thread-pool thread
        // waiting for its partitions, and under load that held the report — and starved unrelated
        // work on the pool — for around ten seconds. Sequential grouping also makes the order of
        // equally scored hosts stable.
        var vulnerabilitiesHosts = dbContext.Vulnerabilities
            .Where(v => v.Cvss3BaseScore > 5 && v.ExploitAvaliable == true && v.HostId != null && v.ExploitabilityEasy == "Exploits are available" && v.Cvss3TemporalScore > 5)
            .AsEnumerable()
            .GroupBy(v=> v.HostId)
            .OrderByDescending( g => g.Sum(v=> v.Cvss3TemporalScore))
            .ToList();

        // Hosts that no longer exist are skipped in both the table and the narrative, as before.
        var hosts = vulnerabilitiesHosts
            .Select(group => (Host: dbContext.Hosts.Find(group.Key), Vulnerabilities: group.ToList()))
            .Where(h => h.Host != null)
            .Select(h => (Host: h.Host!, h.Vulnerabilities))
            .ToList();

        body.Item().PaddingTop(10).Text(Localizer["Host Vulnerability Prioritization Report"].Value)
            .Bold().FontSize(BodyFontSize);

        // The explanatory box. It was a fixed 7 cm x 3 cm text frame; the height is a minimum here,
        // because a fixed height clips a longer translation and QuestPDF refuses content that does
        // not fit rather than clipping it.
        body.Item().PaddingTop(6)
            .Width(7, Unit.Centimetre)
            .MinHeight(3, Unit.Centimetre)
            .Text(Localizer["This report uses the Common Vulnerability Scoring System (CVSS) as well as other metrics to prioritize vulnerabilities on the host."].Value)
            .Bold().FontSize(BodyFontSize);

        body.Item().PaddingTop(12).Element(container => AddStatisticsTable(container, hosts));

        body.Item().PaddingTop(24).Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(BodyFontSize));

            text.Span(Localizer["Hosts"]).Bold();
            text.Span("\n");

            foreach (var (host, vulnerabilities) in hosts)
            {
                text.Span(host.HostName ?? string.Empty).Bold();
                text.Span("\n");
                text.Span("IP: " + host.Ip + "\n");
                text.Span("OS: " + host.Os + "\n");
                text.Span(Localizer["Score"] + ": " + vulnerabilities.Sum(v=> v.Cvss3TemporalScore) + "\n");

                text.Span(Localizer["Vulnerabilities"]).Italic();
                text.Span("\n");

                text.Span(Localizer["Critical Count"] + ": " + vulnerabilities.Count(v => v.Cvss3BaseScore > 8) + "\n");
                text.Span(Localizer["High Count"] + ": " + vulnerabilities.Count(v => v.Cvss3BaseScore <= 8 && v.Cvss3BaseScore > 6) + "\n");
                text.Span(Localizer["Medium Count"] + ": " + vulnerabilities.Count(v => v.Cvss3BaseScore <= 6 && v.Cvss3BaseScore > 4) + "\n");
                text.Span("\n");

                //Critical
                foreach (var vul in vulnerabilities.Where(v => v.Cvss3BaseScore > 8))
                {
                    text.Span("ID:" + vul.Id).Bold();
                    text.Span("\n");
                    text.Span(Localizer["Description"] + ": " + vul.Description + "\n");
                    text.Span(Localizer["Solution"] + ": " + vul.Solution + "\n");
                }
            }
        });
    }

    /// <summary>
    /// One row per host: its critical, high and medium counts and the total. The header row repeats
    /// on every page the table spans.
    /// </summary>
    private void AddStatisticsTable(IContainer container, IReadOnlyList<(Host Host, List<Vulnerability> Vulnerabilities)> hosts)
    {
        container.DefaultTextStyle(style => style.FontSize(9)).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(3, Unit.Centimetre);
                columns.ConstantColumn(2.5f, Unit.Centimetre);
                columns.ConstantColumn(2.5f, Unit.Centimetre);
                columns.ConstantColumn(2.5f, Unit.Centimetre);
                columns.ConstantColumn(2.5f, Unit.Centimetre);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).AlignLeft().Text(Localizer["Computer"].Value);
                header.Cell().Element(HeaderCell).AlignLeft().Text(Localizer["Critical"].Value).Bold();
                header.Cell().Element(HeaderCell).AlignLeft().Text(Localizer["High"].Value).Bold();
                header.Cell().Element(HeaderCell).AlignLeft().Text(Localizer["Medium"].Value).Bold();
                header.Cell().Element(HeaderCell).AlignLeft().Text(Localizer["Total"].Value).Bold();
            });

            foreach (var (host, vulnerabilities) in hosts)
            {
                table.Cell().Element(BodyCell).AlignCenter().Text(host.HostName ?? string.Empty);
                table.Cell().Element(BodyCell).AlignRight().Text(vulnerabilities.Count(v => v.Cvss3BaseScore > 8).ToString());
                table.Cell().Element(BodyCell).AlignRight().Text(vulnerabilities.Count(v => v.Cvss3BaseScore <= 8 && v.Cvss3BaseScore > 6).ToString());
                table.Cell().Element(BodyCell).AlignRight().Text(vulnerabilities.Count(v => v.Cvss3BaseScore <= 6 && v.Cvss3BaseScore > 4).ToString());
                table.Cell().Element(BodyCell).AlignRight().Text(vulnerabilities.Count.ToString());
            }
        });
    }

    private static IContainer BodyCell(IContainer cell) =>
        cell.Border(0.25f).BorderColor(TableBlue).PaddingHorizontal(3).PaddingVertical(1);

    private static IContainer HeaderCell(IContainer cell) =>
        cell.Border(0.25f).BorderColor(TableBlue).Background(TableBlue).PaddingHorizontal(3).PaddingVertical(1).AlignBottom();
}
