using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Tests.Tools.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// S40 §11.5, §12.4 (T251): the index is informational (S39 §1.3, rule R-1). In the desktop client only the module, its
/// pure helpers, its dialogs and the composition root may name the index's contracts, and every command the module's views
/// bind reads or navigates — none decides anything. Source scans, because this project does not load Avalonia.
/// </summary>
public class RiskOverviewSourceScanTest
{
    /// <summary>Where the index may be read (§11.5): the module, the configuration (M55), the helpers and the bootstrapper.</summary>
    private static readonly string[] AllowedPrefixes =
    [
        "ViewModels/RiskOverview/", "ViewModels/Admin/RiskIndex/", "Views/RiskOverview/", "Views/Admin/RiskIndex/",
        "Tools/RiskIndex/", "Views/Dialogs/Risk", "GeneralServicesBootstrapper.cs"
    ];

    private static readonly Regex IndexReference = new(@"\bModel\.RiskIndex\b|\bIRiskIndexService\b|\bRiskIndexRestService\b",
        RegexOptions.Compiled);

    /// <summary>
    /// The commands the module's views may bind: refresh, history, navigation, the profile dialog, retry and close; the
    /// chart/table toggles of the trend and the monthly summary (T252, T253); a click on a day of the trend, which shows
    /// the dashboard as it was that day (§5.1, §10.4); and the drill-down's own (T254) — the Explore chips, which cut the
    /// children listed, the object list's paging and preset chip, and Ctrl+F's search focus (IX-8). None writes anything.
    /// </summary>
    private static readonly HashSet<string> ReadAndNavigateCommands = new(StringComparer.Ordinal)
    {
        "BtRefreshClicked", "BtBackToLatestClicked", "Open", "BtProfileInfoClicked", "BtRetryClicked", "BtCloseClicked",
        "BtToggleTableClicked", "BtToggleMonthlyTableClicked", "BtTrendPointClicked",
        "BtAddFilterClicked", "BtRemoveFilterClicked", "BtClearPresetClicked", "BtPreviousPageClicked", "BtNextPageClicked",
        "BtFocusSearchClicked"
    };

    [Fact]
    public void TestOnlyTheModuleNamesTheIndexContracts()
    {
        var client = RiskIndexTestText.ClientDirectory();
        var offenders = Directory.EnumerateFiles(client, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(client, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !relative.StartsWith("bin/", StringComparison.Ordinal) &&
                               !relative.StartsWith("obj/", StringComparison.Ordinal))
            .Where(relative => !AllowedPrefixes.Any(p => relative.StartsWith(p, StringComparison.Ordinal)))
            .Where(relative => IndexReference.IsMatch(File.ReadAllText(Path.Combine(client, relative))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "The index is not a decision input (S39 §1.3): these files read it outside the module:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void TestTheModuleViewsBindOnlyReadAndNavigationCommands()
    {
        var client = RiskIndexTestText.ClientDirectory();
        var views = Directory.EnumerateFiles(Path.Combine(client, "Views", "RiskOverview"), "*.axaml")
            .Concat(Directory.EnumerateFiles(Path.Combine(client, "Views", "Dialogs"), "Risk*.axaml"))
            .ToList();
        Assert.NotEmpty(views);

        var commands = views.SelectMany(view => Regex.Matches(File.ReadAllText(view), @"Command=""\{Binding (?<name>\w+)")
                .Select(m => (View: Path.GetFileName(view), Name: m.Groups["name"].Value)))
            .ToList();
        Assert.NotEmpty(commands);

        var decisions = commands.Where(c => !ReadAndNavigateCommands.Contains(c.Name)).Select(c => $"{c.View}: {c.Name}").ToList();
        Assert.True(decisions.Count == 0, "The Cyber Risk Overview is read-only; these commands are not on the allowlist:\n  " +
                                          string.Join("\n  ", decisions));
    }

    /// <summary>
    /// A catch-all with no <c>when</c> also takes the failures of a superseded read: every one in the module records what it
    /// took, at least at Debug, as the sibling panels do — never an empty block that swallows it silently.
    /// </summary>
    [Fact]
    public void TestNoModuleViewModelSwallowsAFailureSilently()
    {
        var folder = Path.Combine(RiskIndexTestText.ClientDirectory(), "ViewModels", "RiskOverview");
        var silent = Directory.EnumerateFiles(folder, "*.cs")
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"catch \(Exception(?: \w+)?\)\s*\{(?<body>[^{}]*)\}")
                .Where(m => !m.Groups["body"].Value.Contains("Logger.", StringComparison.Ordinal))
                .Select(m => Path.GetFileName(file)))
            .ToList();

        Assert.True(silent.Count == 0, "These catch-alls swallow a failure without logging it:\n  " + string.Join("\n  ", silent));
    }
}
