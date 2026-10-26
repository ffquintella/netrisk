using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using GUIClient.Tests.Tools.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.Navigation;

/// <summary>
/// T294 (S40 §13, criterion 45): the Master Dashboard is retired. The navigation bar no longer offers it, and no source
/// file in <c>src</c> — production, test or view — names its service, view, view model, client service, DTOs or button.
/// The route itself is pinned on the API side (<c>ControllerAuthorizationInventoryTest.TheRetiredMasterDashboardRouteIsGone</c>).
///
/// Scanned as text because this project deliberately does not reference <c>GUIClient</c> or the API.
/// </summary>
public class MasterDashboardRetirementTest
{
    /// <summary>Identifiers that existed only for the Master Dashboard.</summary>
    private static readonly string[] RetiredIdentifiers =
    [
        "MasterDashboardService",       // and IMasterDashboardService
        "MasterDashboardView",          // and MasterDashboardViewModel
        "IDashboardService",
        "DashboardRestService",
        "BtnMasterDashboard",
        "HasMasterDashboardPermission",
        "AvaliableViews.MasterDashboard",
        "Model.Dashboard",
        "EntityPostureSummary"
    ];

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.Ordinal) { "bin", "obj", "TestResults" };

    private static readonly HashSet<string> ScannedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".axaml", ".csproj", ".resx" };

    [Fact]
    public void TestTheNavigationBarNoLongerOffersTheMasterDashboard()
    {
        var client = RiskIndexTestText.ClientDirectory();
        var bar = File.ReadAllText(Path.Combine(client, "Views", "NavigationBar.axaml"));
        var viewModel = File.ReadAllText(Path.Combine(client, "ViewModels", "NavigationBarViewModel.cs"));

        // Anchors: the files are the real bar, which still carries the Cyber Risk Overview's button.
        Assert.Contains("BtnRiskOverview", bar);
        Assert.Contains("BtRiskOverviewClicked", viewModel);

        Assert.DoesNotContain("MasterDashboard", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("MasterDashboard", viewModel, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(client, "Views", "MasterDashboardView.axaml")));
        Assert.False(File.Exists(Path.Combine(client, "ViewModels", "MasterDashboardViewModel.cs")));
    }

    [Fact]
    public void TestNoSourceFileNamesTheRetiredMasterDashboard()
    {
        var src = Path.GetDirectoryName(RiskIndexTestText.ClientDirectory())!;
        var self = Path.GetFullPath(ThisFile());

        var files = SourceFiles(src).Where(f => !string.Equals(Path.GetFullPath(f), self, StringComparison.Ordinal)).ToList();

        // Anchor: the walk reaches every tier, so an empty or misdirected scan cannot pass.
        Assert.Contains(files, f => f.EndsWith(Path.Combine("API", "ServicesBootstrapper.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("GUIClient", "GeneralServicesBootstrapper.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("ServerServices.Tests", "ServiceTests", "InMemoryServiceTestBase.cs"),
            StringComparison.Ordinal));

        var hits = files
            .SelectMany(f =>
            {
                var text = File.ReadAllText(f);
                return RetiredIdentifiers.Where(id => text.Contains(id, StringComparison.Ordinal))
                    .Select(id => $"{Path.GetRelativePath(src, f)}: {id}");
            })
            .ToList();

        Assert.True(hits.Count == 0, "The Master Dashboard was retired by T294, but these files still name it:"
                                     + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            if (ScannedExtensions.Contains(Path.GetExtension(file)))
                yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (SkippedDirectories.Contains(name) || name.StartsWith('.')) continue;
            foreach (var file in SourceFiles(child))
                yield return file;
        }
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
