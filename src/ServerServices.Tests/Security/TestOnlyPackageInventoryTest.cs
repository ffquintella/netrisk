using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// Mocking frameworks belong to test projects. <c>ConsoleClient</c> and <c>BackgroundJobs</c> both
/// referenced Moq in shipped code to fake an <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>,
/// putting Moq and Castle.Core's dynamic proxy generator into two service binaries.
/// <see cref="ServerServices.Security.BackgroundServiceHttpContextAccessor"/> replaced them.
/// </summary>
public class TestOnlyPackageInventoryTest
{
    /// <summary>Packages that may only appear in a test project.</summary>
    private static readonly string[] TestOnlyPackages =
    [
        "Moq", "NSubstitute", "FakeItEasy", "xunit", "xunit.v3", "Testcontainers",
    ];

    private static readonly Regex PackageReference = new(
        @"<PackageReference\s+[^>]*Include\s*=\s*""(?<id>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void No_shipped_project_references_a_test_only_package()
    {
        var offenders = new List<string>();

        foreach (var project in Directory.EnumerateFiles(SrcRoot(), "*.csproj", SearchOption.AllDirectories))
        {
            // Covers both ".Tests" and "DAL.IntegrationTests".
            var name = Path.GetFileNameWithoutExtension(project);
            if (name.EndsWith("Tests", StringComparison.Ordinal)) continue;

            foreach (Match match in PackageReference.Matches(File.ReadAllText(project)))
            {
                var id = match.Groups["id"].Value;
                if (TestOnlyPackages.Any(p => id.Equals(p, StringComparison.OrdinalIgnoreCase)
                                              || id.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"  {name} references {id}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A mocking or test framework is referenced by a project that ships. Write a real "
            + "implementation instead — a dynamic proxy generator is avoidable attack surface in a "
            + "service host:" + Environment.NewLine + string.Join(Environment.NewLine, offenders.Order()));
    }

    private static string SrcRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
