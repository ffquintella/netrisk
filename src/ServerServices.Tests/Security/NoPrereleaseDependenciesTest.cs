using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// No server project resolves a prerelease package unless it is on the allowlist below with a
/// reason. `FluentEmail.Razor` depends on `RazorLight 2.0.0-rc.3`, a 2019 release candidate, and
/// the only thing holding email rendering on a stable RazorLight is a direct reference that looks
/// removable. Deleting it silently downgrades the renderer. See CHANGELOG.md, [NEXT].
/// </summary>
public class NoPrereleaseDependenciesTest
{
    /// <summary>
    /// Prereleases that are deliberate. Each needs a reason; nothing goes in without one.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LiveChartsCore"] =
            "the only build that speaks Avalonia 12; stable 2.0.1 crashes the client on startup",
        ["LiveChartsCore.SkiaSharpView"] = "ships with LiveChartsCore",
        ["LiveChartsCore.SkiaSharpView.Avalonia"] = "ships with LiveChartsCore",
    };

    /// <summary>
    /// The server-side projects. The desktop client is excluded — its charting prerelease is
    /// covered by the allowlist above and by GUIClient.Tests.
    /// </summary>
    private static readonly string[] ServerProjects =
        ["API", "ServerServices", "BackgroundJobs", "ConsoleClient", "WebSite", "RiskPortal", "DAL"];

    [Fact]
    public void Server_projects_resolve_no_unapproved_prerelease()
    {
        var offenders = new List<string>();

        foreach (var project in ServerProjects)
        {
            var assets = Path.Combine(SrcRoot(), project, "obj", "project.assets.json");
            if (!File.Exists(assets)) continue;

            using var document = JsonDocument.Parse(File.ReadAllText(assets));

            foreach (var (id, version) in Resolved(document))
            {
                // A hyphen in a semantic version is the prerelease label.
                if (!version.Contains('-') || Allowed.ContainsKey(id)) continue;

                offenders.Add($"  {project}: {id} {version}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These server projects resolve a prerelease package. Pin it to a stable release, or "
            + "add it to the allowlist with a reason:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders.Distinct().Order()));
    }

    /// <summary>
    /// The specific case that motivated the guard, asserted by name so the failure message says
    /// what actually broke rather than leaving it to be inferred from a list.
    /// </summary>
    [Fact]
    public void RazorLight_stays_on_a_stable_release()
    {
        var assets = Path.Combine(SrcRoot(), "ServerServices", "obj", "project.assets.json");
        if (!File.Exists(assets)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var resolved = Resolved(document);

        Assert.True(resolved.TryGetValue("RazorLight", out var version),
            "RazorLight is absent from ServerServices' graph. It is the engine behind "
            + "FluentEmail.Razor's UsingTemplateFromFile, so this means email rendering changed.");

        Assert.False(version!.Contains('-'),
            $"RazorLight resolved to the prerelease {version}. FluentEmail.Razor asks for "
            + "2.0.0-rc.3; the direct PackageReference in ServerServices.csproj is what raises it "
            + "to a stable release. It is a pin, not a stray reference — do not remove it.");
    }

    private static Dictionary<string, string> Resolved(JsonDocument assets)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in assets.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                var separator = library.Name.LastIndexOf('/');
                if (separator <= 0) continue;

                resolved[library.Name[..separator]] = library.Name[(separator + 1)..];
            }
        }

        return resolved;
    }

    private static string SrcRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
