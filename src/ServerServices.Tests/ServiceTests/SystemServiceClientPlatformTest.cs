using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DAL;
using JetBrains.Annotations;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Services;
using Xunit;
using ILogger = Serilog.ILogger;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// macOS on Intel (osx-x64) is no longer built (T341). Every Intel client already deployed still
/// reports itself — truthfully — as <c>mac-x64</c>, so the server must recognise that name in order
/// to refuse it, and must never hand such a client the update script or installer of the Apple
/// Silicon build as if it were its own.
///
/// These run against the real <c>ClientInformation.yaml</c> and <c>Scripts/Update-Mac.sh</c> the API
/// ships: this project references API, whose content files are copied beside the test binaries,
/// which is exactly where <see cref="SystemService"/> reads them from.
/// </summary>
[TestSubject(typeof(SystemService))]
public class SystemServiceClientPlatformTest
{
    private static SystemService NewService() =>
        new(Substitute.For<ILogger>(), Substitute.For<IDalService>());

    // ---------- GetUpdateScriptAsync ----------

    [Theory]
    [InlineData("mac")]
    [InlineData("mac-a64")]
    [InlineData("MAC-A64")]
    public async Task TheAppleSiliconNamesStillGetTheMacUpdateScript(string osFamily)
    {
        var script = await NewService().GetUpdateScriptAsync(osFamily);

        Assert.Equal(await ReadShippedFileAsync("Scripts/Update-Mac.sh"), script);
    }

    [Theory]
    [InlineData("mac-x64")]
    [InlineData("MAC-X64")]
    public async Task AnIntelMacIsRefusedTheUpdateScriptAsAnUnsupportedPlatform(string osFamily)
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => NewService().GetUpdateScriptAsync(osFamily));

        Assert.Equal("osFamily", ex.ParameterName);
        Assert.Contains("Intel", ex.Message);
        Assert.Contains("no longer supported", ex.Message);
    }

    // ---------- ClientInformation.yaml ----------

    [Fact]
    public async Task TheShippedClientInformationOffersNoIntelMacInstaller()
    {
        var information = await NewService().GetClientInformation("mac");

        Assert.DoesNotContain("mac-x64", information.DownloadLocation.Keys);
        Assert.DoesNotContain(information.DownloadLocation.Values,
            location => location.Contains("x64.dmg", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("mac")]
    [InlineData("mac-a64")]
    public async Task EveryMacKeyOfTheShippedClientInformationPointsAtTheAppleSiliconDmg(string key)
    {
        var information = await NewService().GetClientInformation(key);

        Assert.EndsWith("/installers/GUIClient-Mac-a64.dmg", information.DownloadLocation[key]);
    }

    /// <summary>
    /// Production servers do not run the checked-in <c>ClientInformation.yaml</c>: Puppet renders
    /// theirs from <c>ClientInformation.yaml.epp</c>. The two must offer the same platforms and the
    /// same installer for each, or a fix made to one (as this one was) never reaches the other.
    /// </summary>
    [Fact]
    public void ThePuppetTemplateOffersTheSamePlatformsAndInstallersAsTheShippedFile()
    {
        var shipped = InstallerByPlatform(File.ReadAllText(RepoFile("src", "API", "ClientInformation.yaml")));
        var template = InstallerByPlatform(File.ReadAllText(
            RepoFile("build", "puppet", "modules", "netrisk", "templates", "api", "ClientInformation.yaml.epp")));

        Assert.NotEmpty(shipped);
        Assert.Equal(shipped.OrderBy(p => p.Key, StringComparer.Ordinal),
            template.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    private static readonly Regex DownloadLine =
        new(@"^[ \t]+(?<platform>[a-z0-9-]+):.*/installers/(?<installer>[^\s/]+)\s*$", RegexOptions.Multiline);

    private static Dictionary<string, string> InstallerByPlatform(string yaml) =>
        DownloadLine.Matches(yaml).ToDictionary(m => m.Groups["platform"].Value, m => m.Groups["installer"].Value);

    private static Task<string> ReadShippedFileAsync(string relativePath) =>
        File.ReadAllTextAsync(Path.Combine(
            Path.GetDirectoryName(typeof(SystemService).Assembly.Location)!, relativePath));

    private static string RepoFile(string first, params string[] rest) =>
        Path.Combine([RepoRoot(), first, .. rest]);

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
