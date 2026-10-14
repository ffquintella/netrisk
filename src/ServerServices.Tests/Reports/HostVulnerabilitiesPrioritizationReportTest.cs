using System;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Reports;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Reports;

/// <summary>
/// The host vulnerability prioritization report, rendered for real and read back. It had no test
/// before the move to QuestPDF; these pin what it says — the explanatory box, the per-host statistics
/// table, and the per-host narrative with each critical finding — and that a vulnerability outside
/// the report's filter, or one whose host is gone, stays out of it.
/// </summary>
[TestSubject(typeof(HostVulnerabilitiesPrioritizationReport))]
public class HostVulnerabilitiesPrioritizationReportTest : InMemoryServiceTestBase
{
    private static Report NewReport() => new()
    {
        Id = 1, Name = "Hosts", Type = 1, CreatorId = 1,
        CreationDate = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Host NewHost(int id, string name) => new()
    {
        Id = id, HostName = name, Ip = $"10.0.0.{id}", Os = "Linux", Source = "test",
        RegistrationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>A vulnerability that passes the report's filter, with the given base score.</summary>
    private static Vulnerability Exploitable(int id, int? hostId, float baseScore) => new()
    {
        Id = id, HostId = hostId, Title = $"Vuln {id}", Description = $"Description {id}",
        Solution = $"Solution {id}", Cvss3BaseScore = baseScore, Cvss3TemporalScore = 6.0f,
        ExploitAvaliable = true, ExploitabilityEasy = "Exploits are available",
        FirstDetection = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastDetection = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
    };

    private async Task<PdfText> RenderAsync()
    {
        var report = new HostVulnerabilitiesPrioritizationReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>());

        var data = await report.GenerateReportAsync("Host Vulnerabilities Prioritization Report");

        Assert.True(PdfText.IsPdf(data));
        return PdfText.Read(data);
    }

    [Fact]
    public async Task TestTheReportCarriesTheTableAndTheCriticalFindingsPerHost()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Hosts.Add(NewHost(901, "web-01"));

            ctx.Vulnerabilities.Add(Exploitable(9001, 901, 9.5f)); // critical
            ctx.Vulnerabilities.Add(Exploitable(9002, 901, 7.0f)); // high
            ctx.Vulnerabilities.Add(Exploitable(9003, 901, 5.5f)); // medium

            // Outside the filter: no exploit available.
            var quiet = Exploitable(9004, 901, 9.9f);
            quiet.ExploitAvaliable = false;
            quiet.Description = "Should not appear";
            ctx.Vulnerabilities.Add(quiet);

            // A host id with no host row: skipped in the table and in the narrative.
            ctx.Vulnerabilities.Add(Exploitable(9005, 999, 9.0f));
        });

        var pdf = await RenderAsync();

        foreach (var text in new[]
                 {
                     "Host Vulnerability Prioritization Report",
                     "This report uses the Common Vulnerability Scoring System (CVSS) as well as other metrics to prioritize vulnerabilities on the host.",
                     // The statistics table: header, then web-01 with 1 critical, 1 high, 1 medium, 3 in all.
                     "Computer Critical High Medium Total",
                     "web-01 1 1 1 3",
                     // The narrative.
                     "Hosts",
                     "IP: 10.0.0.901", "OS: Linux",
                     "Vulnerabilities",
                     "Critical Count: 1", "High Count: 1", "Medium Count: 1",
                     "ID:9001", "Description: Description 9001", "Solution: Solution 9001"
                 })
            Assert.True(pdf.Contains(text), $"the PDF does not contain \"{text}\"");

        // Only critical findings are listed in the narrative, and filtered ones nowhere.
        Assert.False(pdf.Contains("ID:9002"));
        Assert.False(pdf.Contains("Should not appear"));
        Assert.False(pdf.Contains("ID:9005"));
        Assert.True(pdf.IndexOf("web-01 1 1 1 3") < pdf.IndexOf("IP: 10.0.0.901"), "the table is not before the narrative");
    }

    /// <summary>An instance with no exploitable findings still produces a report: the box, an empty table, no hosts.</summary>
    [Fact]
    public async Task TestAnEmptyRegisterStillProducesAReport()
    {
        var pdf = await RenderAsync();

        Assert.True(pdf.Contains("Computer Critical High Medium Total"));
        Assert.True(pdf.Contains("Hosts"));
        Assert.False(pdf.Contains("IP:"));
    }
}
