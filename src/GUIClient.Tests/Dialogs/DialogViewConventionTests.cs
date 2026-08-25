using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.ViewModels.Dialogs;
using Xunit;

namespace GUIClient.Tests.Dialogs;

/// <summary>
/// Guards the <see cref="DialogService"/> naming convention against the failure that shipped in
/// "Track 1 Milestone 1.5": routing a dialog through <c>ShowDialogAsync(nameof(SomeViewModel))</c>
/// whose view class the convention cannot name. The mismatch is invisible at compile time —
/// resolution is reflective — and the miss aborted the process rather than reporting itself, so
/// the only place to catch it early is a test.
///
/// This scans source text rather than reflecting over the GUIClient assembly on purpose:
/// GUIClient.Tests does not reference GUIClient, because that would drag all of Avalonia into a
/// headless run.
/// </summary>
public class DialogViewConventionTests
{
    /// <summary>Dialog view classes: <c>class Foo : DialogWindowBase&lt;FooResult&gt;</c>.</summary>
    private static readonly Regex DialogViewDeclaration =
        new(@"class\s+(?<view>\w+)\s*:\s*DialogWindowBase\s*<", RegexOptions.Compiled);

    /// <summary>Call sites naming the view model: <c>ShowDialogAsync&lt;…&gt;(nameof(FooViewModel)</c>.</summary>
    private static readonly Regex NameofCallSite =
        new(@"ShowDialogAsync[^(;]*\(\s*nameof\(\s*(?<vm>\w+ViewModel)\s*\)",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Call sites naming the view model as a type argument:
    /// <c>ShowDialogAsync&lt;FooResult, FooParameter, FooViewModel&gt;(</c>.
    /// </summary>
    private static readonly Regex TypeArgumentCallSite =
        new(@"ShowDialogAsync\s*<(?<args>[^<>(;]*)>\s*\(", RegexOptions.Compiled);

    [Fact]
    public void EveryDialogTheAppOpensResolvesToADeclaredView()
    {
        var views = DeclaredDialogViewNames();
        var callSites = DialogCallSites();

        Assert.NotEmpty(views);
        Assert.NotEmpty(callSites);

        var unresolved = callSites
            .Where(site => !DialogViewNaming.GetCandidateViewNames(site.Key).Any(views.Contains))
            .Select(site => $"  {site.Key} (tried {string.Join(", ", DialogViewNaming.GetCandidateViewNames(site.Key))})" +
                            $"\n      opened from {string.Join(", ", site.Value.Order())}")
            .Order()
            .ToList();

        Assert.True(
            unresolved.Count == 0,
            $"{unresolved.Count} dialog(s) cannot be resolved to a view class and will throw "
            + "\"View for … was not found!\" when opened:\n"
            + string.Join('\n', unresolved)
            + "\n\nEither name the view so DialogViewNaming can find it, or teach "
            + "DialogViewNaming the new suffix.");
    }

    [Fact]
    public void TheDialogsWithWindowSuffixedViewsAreCovered()
    {
        // A canary for the scan itself: if these stop being found as call sites, the regexes have
        // drifted and the test above would pass by looking at nothing.
        var callSites = DialogCallSites();

        Assert.Contains("EditRiskViewModel", callSites.Keys);
        Assert.Contains("CloseRiskViewModel", callSites.Keys);
        Assert.Contains("VulnerabilityImportViewModel", callSites.Keys);
    }

    /// <summary>
    /// Concrete dialog views only. Scoped to <c>Views/</c> so the framework's own
    /// <c>DialogWindowBase : DialogWindowBase&lt;DialogResultBase&gt;</c> is not mistaken for one.
    /// </summary>
    private static HashSet<string> DeclaredDialogViewNames() =>
        SourceFiles()
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}",
                                         StringComparison.Ordinal))
            .SelectMany(file => DialogViewDeclaration.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups["view"].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>View model name → the files that open it as a dialog.</summary>
    private static Dictionary<string, List<string>> DialogCallSites()
    {
        var sites = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            // DialogService/IDialogService declare the generic ShowDialogAsync overloads; their
            // `TViewModel` type parameter is not a call site.
            if (Path.GetFileName(file) is "DialogService.cs" or "IDialogService.cs")
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var names = NameofCallSite.Matches(text)
                .Select(match => match.Groups["vm"].Value)
                .Concat(TypeArgumentCallSite.Matches(text)
                    .SelectMany(match => match.Groups["args"].Value.Split(','))
                    .Select(argument => argument.Trim())
                    .Where(argument => argument.EndsWith("ViewModel", StringComparison.Ordinal)));

            foreach (var name in names)
            {
                if (!sites.TryGetValue(name, out var files))
                {
                    sites[name] = files = [];
                }

                var relative = Path.GetFileName(file);
                if (!files.Contains(relative))
                {
                    files.Add(relative);
                }
            }
        }

        return sites;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(GuiClientSourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                          StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                             StringComparison.Ordinal));

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project))
            {
                return Path.GetDirectoryName(project)!;
            }
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
