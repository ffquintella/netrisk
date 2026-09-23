using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// Versions live in <c>src/Directory.Packages.props</c>, not in project files. Restating a version
/// per project is how the solution ended up resolving two LiveChartsCore builds at once, with the
/// lower one declared by the projects that shipped it. See CHANGELOG.md, [NEXT].
/// </summary>
public class CentralPackageManagementTest
{
    private static readonly Regex InlineVersion = new(
        @"<PackageReference\s+[^>]*Include\s*=\s*""(?<id>[^""]+)""[^>]*\sVersion\s*=\s*""(?<version>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex DeclaredReference = new(
        @"<PackageReference\s+[^>]*Include\s*=\s*""(?<id>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void Central_management_is_switched_on()
    {
        var props = XDocument.Load(PackagesProps());

        var enabled = props.Descendants()
            .Where(e => e.Name.LocalName == "ManagePackageVersionsCentrally")
            .Select(e => e.Value.Trim())
            .SingleOrDefault();

        Assert.Equal("true", enabled);
    }

    [Fact]
    public void No_project_declares_its_own_version()
    {
        var offenders = new List<string>();

        foreach (var project in Projects())
        {
            foreach (Match match in InlineVersion.Matches(File.ReadAllText(project)))
            {
                offenders.Add(
                    $"  {Path.GetFileName(project)} pins {match.Groups["id"].Value} "
                    + $"to {match.Groups["version"].Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These projects declare a package version inline. Move it to "
            + "src/Directory.Packages.props, or use VersionOverride and say why:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders.Order()));
    }

    /// <summary>
    /// Every package a project references has a version to resolve. A missing entry is an NU1008
    /// at restore, but only for whoever restores that project.
    /// </summary>
    [Fact]
    public void Every_referenced_package_has_a_central_version()
    {
        var known = XDocument.Load(PackagesProps()).Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();

        foreach (var project in Projects())
        {
            foreach (Match match in DeclaredReference.Matches(File.ReadAllText(project)))
            {
                var id = match.Groups["id"].Value;
                if (!known.Contains(id))
                {
                    missing.Add($"  {Path.GetFileName(project)} references {id}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "These references have no PackageVersion in src/Directory.Packages.props:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing.Distinct().Order()));
    }

    /// <summary>
    /// One package, one entry — case-insensitively, since NuGet ids are. Two spellings of
    /// LiveChartsCore is how the duplicate slipped past review in the first place.
    /// </summary>
    [Fact]
    public void Central_versions_have_no_duplicate_ids()
    {
        var ids = XDocument.Load(PackagesProps()).Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => e.Attribute("Include")!.Value)
            .ToList();

        var duplicates = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"  {string.Join(" / ", g)}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Duplicate PackageVersion ids:" + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
    }

    private static IEnumerable<string> Projects() =>
        Directory.EnumerateFiles(SrcRoot(), "*.csproj", SearchOption.AllDirectories);

    private static string PackagesProps() => Path.Combine(SrcRoot(), "Directory.Packages.props");

    private static string SrcRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
