using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace WebSite.Tests.Views;

/// <summary>
/// The download buttons on the home page link to files under <c>wwwroot/installers</c>, which do
/// not exist in the repository: the Nuke target that builds the website image copies them there from
/// the packaging output. A button for an installer the build no longer produces is a 404 that nothing
/// reports — which is what removing the Intel macOS build (T341) would have left behind.
/// </summary>
public class DownloadPageInstallersTest
{
    private static readonly Regex InstallerLink =
        new(@"href\s*=\s*""installers/(?<file>[^""]+)""", RegexOptions.Compiled);

    // `installersDir / "GUIClient-Mac-a64.dmg"` in CreateDockerImageWebSite.
    private static readonly Regex InstallerCopiedByTheBuild =
        new(@"installersDir\s*/\s*""(?<file>[^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_installer_the_home_page_offers_is_one_the_website_build_copies()
    {
        var copied = InstallerCopiedByTheBuild.Matches(File.ReadAllText(RepoFile("build", "Build.cs")))
            .Select(m => m.Groups["file"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(copied);
        Assert.All(LinkedInstallers(), file => Assert.Contains(file, copied));
    }

    [Fact]
    public void The_only_macOS_installer_offered_is_the_Apple_Silicon_one()
    {
        var mac = LinkedInstallers().Where(f => f.Contains("Mac", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Equal(new[] { "GUIClient-Mac-a64.dmg" }, mac);
    }

    private static string[] LinkedInstallers() =>
        InstallerLink.Matches(File.ReadAllText(RepoFile("src", "WebSite", "Views", "Home", "Index.cshtml")))
            .Select(m => m.Groups["file"].Value)
            .ToArray();

    private static string RepoFile(string first, params string[] rest) =>
        Path.Combine([RepoRoot(), first, .. rest]);

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
