using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Dependencies;

/// <summary>
/// SkiaSharp, SkiaSharp.HarfBuzz and HarfBuzzSharp load into one process and share types, so they
/// must agree on a major. They did not: LiveChartsCore.SkiaSharpView asks for SkiaSharp.HarfBuzz
/// 2.88.9 while Avalonia 12.1.x forces SkiaSharp 3.119.4, leaving the bridge assembly two majors
/// behind — a TypeLoadException on the first chart to shape text, invisible to a headless suite
/// that renders none. Rationale and the pin that fixes it: CHANGELOG.md, [NEXT].
/// </summary>
public class SkiaHarfBuzzUnificationTest
{
    /// <summary>The Skia-family packages that must share a major version at runtime.</summary>
    private static readonly string[] SkiaFamily = ["SkiaSharp", "SkiaSharp.HarfBuzz"];

    private static readonly Regex PackageReference = new(
        @"<PackageReference\s+[^>]*Include\s*=\s*""(?<id>[^""]+)""[^>]*Version\s*=\s*""(?<version>[^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Deleting the explicit pin reintroduces the defect, and nothing else in the build says so.
    /// </summary>
    [Fact]
    public void GuiClient_pins_SkiaSharp_HarfBuzz_explicitly()
    {
        var declared = DeclaredPackages(Path.Combine(SrcRoot(), "GUIClient", "GUIClient.csproj"));

        Assert.True(declared.ContainsKey("SkiaSharp.HarfBuzz"),
            "GUIClient must pin SkiaSharp.HarfBuzz explicitly. Without the pin, NuGet resolves the "
            + "2.88.9 that LiveChartsCore.SkiaSharpView asks for, against the SkiaSharp 3.x that "
            + "Avalonia.Skia forces — a TypeLoadException the moment a chart shapes text.");
    }

    /// <summary>
    /// The pin is only worth having if it names the version the rest of the solution resolved.
    /// </summary>
    [Fact]
    public void Skia_family_versions_agree_across_the_solution()
    {
        var versions = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var project in Directory.EnumerateFiles(SrcRoot(), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (var (id, version) in DeclaredPackages(project))
            {
                if (!IsSkiaFamily(id)) continue;

                (versions.TryGetValue(version, out var users) ? users : versions[version] = [])
                    .Add(Path.GetFileName(project));
            }
        }

        Assert.True(versions.Count > 0,
            $"No SkiaSharp-family PackageReference found under {SrcRoot()}. The scan is looking in "
            + "the wrong place — fix the test, do not delete the assertion.");

        Assert.True(versions.Count == 1,
            "The SkiaSharp family must be on a single version across src/. Found:"
            + Environment.NewLine
            + string.Join(Environment.NewLine,
                versions.Select(v => $"  {v.Key} — {string.Join(", ", v.Value.Distinct())}")));
    }

    /// <summary>
    /// What restore actually produced. Catches the <em>next</em> drift: Avalonia moving to
    /// SkiaSharp 4 leaves the pin internally consistent but a major behind the real graph.
    /// </summary>
    [Fact]
    public void Resolved_graph_puts_the_Skia_family_on_one_major()
    {
        var assets = Path.Combine(SrcRoot(), "GUIClient", "obj", "project.assets.json");

        // Restore is the caller's job and a solution build has already done it. When it has not,
        // the two manifest assertions above still ran; neither depends on restore.
        if (!File.Exists(assets)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var resolved = ResolvedVersions(document);

        var skia = Require(resolved, "SkiaSharp");
        var skiaHarfBuzz = Require(resolved, "SkiaSharp.HarfBuzz");

        Assert.True(Major(skia) == Major(skiaHarfBuzz),
            $"SkiaSharp resolved to {skia} but SkiaSharp.HarfBuzz resolved to {skiaHarfBuzz}. "
            + "These share types; a major apart is a TypeLoadException as soon as a chart shapes "
            + "text. Update the SkiaSharp.HarfBuzz pin in GUIClient.csproj to match.");
    }

    /// <summary>
    /// Managed HarfBuzzSharp and the Linux native assets are the same build. Raising the managed
    /// side without the natives leaves a shipped Linux client P/Invoking a different binary.
    /// </summary>
    [Fact]
    public void Resolved_graph_matches_HarfBuzz_managed_and_Linux_natives()
    {
        var assets = Path.Combine(SrcRoot(), "GUIClient", "obj", "project.assets.json");
        if (!File.Exists(assets)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var resolved = ResolvedVersions(document);

        var managed = Require(resolved, "HarfBuzzSharp");
        var linux = Require(resolved, "HarfBuzzSharp.NativeAssets.Linux");

        Assert.True(managed == linux,
            $"HarfBuzzSharp resolved to {managed} but HarfBuzzSharp.NativeAssets.Linux to {linux}. "
            + "The Linux packages ship the native library the managed one P/Invokes; they must be "
            + "the same build. Update the native-assets pin in GUIClient.csproj.");
    }

    private static bool IsSkiaFamily(string id) =>
        // NativeAssets.* version in lockstep with SkiaSharp itself, so they join the comparison.
        SkiaFamily.Contains(id, StringComparer.OrdinalIgnoreCase)
        || id.StartsWith("SkiaSharp.NativeAssets.", StringComparison.OrdinalIgnoreCase);

    private static string Require(IReadOnlyDictionary<string, string> resolved, string id)
    {
        Assert.True(resolved.ContainsKey(id),
            $"{id} is absent from GUIClient's resolved graph. If the charting stack genuinely no "
            + "longer needs it, remove the pin and this assertion together — deliberately, rather "
            + "than by letting a guard go quiet.");
        return resolved[id];
    }

    /// <summary>Package id → resolved version, read from the restore output's targets.</summary>
    private static Dictionary<string, string> ResolvedVersions(JsonDocument assets)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in assets.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                // Keys are "Id/Version".
                var separator = library.Name.LastIndexOf('/');
                if (separator <= 0) continue;

                resolved[library.Name[..separator]] = library.Name[(separator + 1)..];
            }
        }

        return resolved;
    }

    private static int Major(string version) => int.Parse(version.Split('.', '-')[0]);

    private static Dictionary<string, string> DeclaredPackages(string projectFile)
    {
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in PackageReference.Matches(File.ReadAllText(projectFile)))
        {
            declared[match.Groups["id"].Value] = match.Groups["version"].Value;
        }

        return declared;
    }

    private static string SrcRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
