using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Importers;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using ServerServices.Tests.Track3.Fixtures;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// Importer discovery and auto-detection (Track 3 milestone 3.1.4).
///
/// The property that matters: built-ins and plugins are indistinguishable to a caller, and an
/// unknown name fails with the list of names that would have worked rather than a bare 404.
/// </summary>
[TestSubject(typeof(ImporterRegistry))]
public class ImporterRegistryTest : InMemoryServiceTestBase
{
    private readonly IImporterRegistry _registry;

    public ImporterRegistryTest()
    {
        _registry = GetService<IImporterRegistry>();
    }

    private static Stream Report(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task TestEveryBuiltInImporterIsDiscoverable()
    {
        var importers = await _registry.GetImportersAsync();

        var names = importers.Select(i => i.Name).ToList();

        // The eight the spec names, plus the generic SARIF importer and the refactored Nessus one.
        Assert.Contains("nessus", names);
        Assert.Contains("sarif", names);
        Assert.Contains("zap", names);
        Assert.Contains("trivy", names);
        Assert.Contains("semgrep", names);
        Assert.Contains("openvas", names);
        Assert.Contains("burp", names);
        Assert.Contains("snyk", names);
        Assert.Contains("grype", names);
        Assert.Contains("dependabot", names);
    }

    [Fact]
    public async Task TestDescriptorsCarryWhatAPickerNeeds()
    {
        var nessus = (await _registry.GetImportersAsync()).Single(i => i.Name == "nessus");

        Assert.Equal("Tenable Nessus", nessus.DisplayName);
        Assert.Contains(".nessus", nessus.SupportedFileExtensions);
        Assert.False(nessus.IsPlugin);
        // The chain travels with the descriptor so a client can show what a re-import will do
        // without a second round trip.
        Assert.NotNull(nessus.DedupStrategyChain);
    }

    [Fact]
    public async Task TestResolveIsCaseInsensitive()
    {
        Assert.Equal("nessus", (await _registry.ResolveAsync("NESSUS")).Name);
    }

    [Fact]
    public async Task TestAnUnknownImporterNamesTheAlternatives()
    {
        var ex = await Assert.ThrowsAsync<DataNotFoundException>(() => _registry.ResolveAsync("nmap"));

        // A 404 that only says "unknown importer" makes the caller guess, and the guess is usually
        // a second failed request.
        Assert.Contains("nessus", ex.InnerException!.Message);
        Assert.Contains("trivy", ex.InnerException.Message);
    }

    [Theory]
    [InlineData(nameof(ImporterFixtures.Nessus), "scan.nessus", "nessus")]
    [InlineData(nameof(ImporterFixtures.Trivy), "trivy.json", "trivy")]
    [InlineData(nameof(ImporterFixtures.Zap), "zap.json", "zap")]
    [InlineData(nameof(ImporterFixtures.Grype), "grype.json", "grype")]
    [InlineData(nameof(ImporterFixtures.Snyk), "snyk.json", "snyk")]
    [InlineData(nameof(ImporterFixtures.OpenVas), "gvm.xml", "openvas")]
    [InlineData(nameof(ImporterFixtures.BurpXml), "burp.xml", "burp")]
    [InlineData(nameof(ImporterFixtures.Dependabot), "alerts.json", "dependabot")]
    public async Task TestAutoDetectionPicksTheRightImporter(string fixtureName, string fileName, string expected)
    {
        var fixture = (string)typeof(ImporterFixtures).GetField(fixtureName)!.GetValue(null)!;

        await using var report = Report(fixture);

        var detected = await _registry.DetectAsync(report, fileName);

        Assert.Equal(expected, detected?.Name);
    }

    [Fact]
    public async Task TestAutoDetectionWorksWithoutAUsefulFileName()
    {
        await using var report = Report(ImporterFixtures.Trivy);

        // A scan report saved as .txt is still a scan report.
        Assert.Equal("trivy", (await _registry.DetectAsync(report, "results.txt"))?.Name);
    }

    [Fact]
    public async Task TestDetectionLeavesTheStreamReadyToParse()
    {
        await using var report = Report(ImporterFixtures.Nessus);

        await _registry.DetectAsync(report, "scan.nessus");

        Assert.Equal(0, report.Position);
    }

    [Fact]
    public async Task TestUnrecognisedContentDetectsNothing()
    {
        await using var report = Report("this is a shopping list, not a scan");

        Assert.Null(await _registry.DetectAsync(report, "list.txt"));
    }

    [Fact]
    public async Task TestAutoNameTriggersDetection()
    {
        await using var report = Report(ImporterFixtures.Zap);

        var resolved = await _registry.ResolveOrDetectAsync(ImporterRegistry.AutoDetectName, report, "zap.json");

        Assert.Equal("zap", resolved.Name);
    }

    [Fact]
    public async Task TestAutoOnAnUnrecognisableReportFailsHelpfully()
    {
        await using var report = Report("nothing recognisable at all");

        var ex = await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _registry.ResolveOrDetectAsync(ImporterRegistry.AutoDetectName, report, "mystery.bin"));

        Assert.Contains("Name one explicitly", ex.InnerException!.Message);
    }

    [Fact]
    public async Task TestANamedImporterIsNotSecondGuessedByDetection()
    {
        await using var report = Report(ImporterFixtures.Trivy);

        // The caller said sarif. Detection is for "auto" only — silently overriding an explicit
        // choice would make a mis-import impossible to diagnose.
        Assert.Equal("sarif", (await _registry.ResolveOrDetectAsync("sarif", report, "trivy.json")).Name);
    }
}
