using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using DAL.Context;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.RiskIndex;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M53, T247 (S39 §1.2–§1.3 rule R-1; S40 §11.5): the index is informational. No service on the decision path — workflow,
/// acceptance, campaign, SLA, notification, KRI, gates, the RiskPortal — may read the index's tables or services. This is
/// an allowlist over the IL of every product assembly of <c>src/netrisk.sln</c> that could name an index type.
/// </summary>
[TestSubject(typeof(RiskIndexSnapshotService))]
public class IcrIsNotADecisionInputTest
{
    /// <summary>
    /// The readers S39 §1.3 names. Index namespaces; the DAL's own model of it; the composition roots; and, reserved for
    /// wave 4 by name, the dashboard and profile controllers, the PDF report and the ClientServices REST layer.
    /// </summary>
    private static bool Allowed(string assembly, string owner)
    {
        var dot = owner.LastIndexOf('.');
        var ns = dot < 0 ? "" : owner[..dot];
        var name = owner[(dot + 1)..];

        if (IcrReferenceScanner.IndexNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal)))
            return true;

        return assembly switch
        {
            "DAL" => owner == "DAL.Context.NRDbContext" || ns == "DAL.Migrations" ||
                     (ns is "DAL.Entities" or "DAL.Enums" && name.StartsWith("RiskIndex", StringComparison.Ordinal)),
            "API" => owner is "API.ServicesBootstrapper" or "API.Controllers.RiskIndexController"
                         or "API.Controllers.RiskIndexProfilesController" ||
                     ns.StartsWith("API.Controllers.RiskIndex", StringComparison.Ordinal),
            "BackgroundJobs" => owner is "BackgroundJobs.ConfigurationManager" or "BackgroundJobs.JobsManager",
            "ServerServices" => owner == "ServerServices.Reports.RiskIndexPdfReport",
            "ClientServices" => name is "IRiskIndexService" or "RiskIndexRestService" or "IRiskIndexConfigService"
                or "RiskIndexConfigRestService",
            _ => false
        };
    }

    /// <summary>The desktop client is covered by a source scan in GUIClient.Tests (S40 §11.5): loading it pulls Avalonia.</summary>
    private static readonly string[] SourceScanned = ["GUIClient"];

    private static string SrcRoot()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Assert.True(File.Exists(Path.Combine(root, "netrisk.sln")), root);
        return root;
    }

    private sealed record SolutionProject(string Name, string Path);

    /// <summary>The projects of the solution, read from the <c>.sln</c> rather than from a list kept beside it.</summary>
    private static List<SolutionProject> SolutionProjects(string root)
    {
        var line = new Regex(@"^Project\(""[^""]+""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+\.csproj)""", RegexOptions.Multiline);
        return line.Matches(File.ReadAllText(Path.Combine(root, "netrisk.sln")))
            .Select(m => new SolutionProject(m.Groups["name"].Value,
                Path.GetFullPath(Path.Combine(root, m.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))))
            .ToList();
    }

    private static IEnumerable<string> References(string csproj) =>
        XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include"))
            .Where(i => i is not null)
            .Select(i => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, i!.Replace('\\', Path.DirectorySeparatorChar))));

    /// <summary>Every project <paramref name="csproj"/> reaches through <c>ProjectReference</c>, itself included.</summary>
    private static HashSet<string> Closure(string csproj)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([csproj]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (!seen.Add(next) || !File.Exists(next)) continue;
            foreach (var reference in References(next)) pending.Push(reference);
        }

        return seen;
    }

    private static string AssemblyName(string csproj) =>
        XDocument.Load(csproj).Descendants("AssemblyName").Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0)
        ?? Path.GetFileNameWithoutExtension(csproj);

    /// <summary>
    /// The product projects that can name an index type: not a test project, not source-scanned, and reaching DAL or
    /// Model — every index type lives in, or is built on, one of the two. A project reaching neither cannot name one.
    /// </summary>
    private static List<SolutionProject> ScannedProjects(string root)
    {
        var projects = SolutionProjects(root);
        var anchors = projects.Where(p => p.Name is "DAL" or "Model").Select(p => p.Path).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2, anchors.Count);

        return projects
            .Where(p => !p.Name.EndsWith("Tests", StringComparison.Ordinal) && !SourceScanned.Contains(p.Name))
            .Where(p => Closure(p.Path).Overlaps(anchors))
            .ToList();
    }

    [Fact]
    public void TestNoAssemblyOutsideTheAllowlistReadsTheIndex()
    {
        var root = SrcRoot();
        var scanned = ScannedProjects(root);

        var missing = new List<string>();
        var violations = new List<IcrReferenceScanner.Violation>();
        foreach (var project in scanned)
        {
            var dll = Path.Combine(AppContext.BaseDirectory, AssemblyName(project.Path) + ".dll");
            if (!File.Exists(dll))
            {
                missing.Add($"{project.Name} ({Path.GetFileName(dll)})");
                continue;
            }

            violations.AddRange(IcrReferenceScanner.Scan(dll, Allowed));
        }

        Assert.True(missing.Count == 0,
            "These solution projects can name an index type but are not in the test directory, so the scan would be " +
            "narrower than it claims; add a ProjectReference to ServerServices.Tests:\n  " + string.Join("\n  ", missing));
        Assert.True(violations.Count == 0,
            "The Consolidated Risk Index is not a decision input (S39 §1.3): these types read it outside the allowlist:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>The scan covers what it should: the job host, the API, the DAL, the RiskPortal and the REST clients.</summary>
    [Fact]
    public void TestTheScanCoversEveryProductAssemblyThatCanNameTheIndex()
    {
        var names = ScannedProjects(SrcRoot()).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "DAL", "Model", "ServerServices", "API", "BackgroundJobs", "ConsoleClient", "ClientServices",
                     "SharedServices", "RiskPortal", "WebSite", "WebSiteData", "Tools"
                 })
            Assert.Contains(expected, names);
        Assert.DoesNotContain("GUIClient", names);
    }

    /// <summary>The allowlist is not vacuous: the job, its service and the composition roots do reference the index.</summary>
    [Fact]
    public void TestTheAllowedReadersReallyReferenceTheIndex()
    {
        var jobs = IcrReferenceScanner.Scan(Path.Combine(AppContext.BaseDirectory, "BackgroundJobs.dll"), (_, _) => false);

        Assert.Contains(jobs, v => v.Owner == "BackgroundJobs.ConfigurationManager");
        Assert.Contains(jobs, v => v.Owner == "BackgroundJobs.JobsManager" && v.Reference.EndsWith("RiskIndexSnapshotJob", StringComparison.Ordinal));
        Assert.Contains(jobs, v => v.Owner == "BackgroundJobs.Jobs.RiskIndex.RiskIndexSnapshotJob");
        Assert.Contains(jobs, v => v.Owner == "BackgroundJobs.Jobs.RiskIndex.RiskIndexPreviewJob");

        // Wave 4 (T249, T250): the two controllers and their error mapping are the API's only readers.
        var api = IcrReferenceScanner.Scan(Path.Combine(AppContext.BaseDirectory, "API.dll"), (_, _) => false);
        Assert.Contains(api, v => v.Owner == "API.Controllers.RiskIndexController");
        Assert.Contains(api, v => v.Owner == "API.Controllers.RiskIndexProfilesController");
        Assert.All(api, v => Assert.True(Allowed("API", v.Owner), v.ToString()));
    }

    // --- the checker checks: types of this test assembly that read the index, each in a way a signature does not show ---

    /// <summary>The fixtures below the class, each a would-be decision service that reads the index, by name prefix.</summary>
    private const string FixturePrefix = "ServerServices.Tests.RiskIndex.DecisionFixture";

    private static IReadOnlyList<IcrReferenceScanner.Violation> FixtureViolations() =>
        IcrReferenceScanner.Scan(typeof(IcrIsNotADecisionInputTest).Assembly.Location, Allowed)
            .Where(v => v.Owner.StartsWith(FixturePrefix, StringComparison.Ordinal))
            .ToList();

    /// <summary>S40 §11.5's negative case: the only access is <c>db.RiskIndexSnapshots</c> inside a method body.</summary>
    [Fact]
    public void TestASnapshotReadInsideAMethodBodyIsCaught()
    {
        var violations = FixtureViolations();

        Assert.Contains(violations, v => v.Owner.EndsWith(nameof(DecisionFixtureCampaignEscalationReadingSnapshots), StringComparison.Ordinal)
                                         && v.Reference == "DAL.Entities.RiskIndexSnapshot");
    }

    /// <summary>A service resolved by type inside an async method lives in a compiler-generated state machine.</summary>
    [Fact]
    public void TestAServiceReferenceInsideAnAsyncStateMachineIsCaught()
    {
        Assert.Contains(FixtureViolations(), v =>
            v.Owner.EndsWith(nameof(DecisionFixtureSlaReminderAwaitingTheCalculator), StringComparison.Ordinal) &&
            v.Reference == typeof(IRiskIndexDayCalculator).FullName);
    }

    /// <summary>A lambda filtering on an index enum compiles to a closure or an expression tree — still the owner's.</summary>
    [Fact]
    public void TestAnIndexTypeInsideALambdaIsCaught()
    {
        Assert.Contains(FixtureViolations(), v =>
            v.Owner.EndsWith(nameof(DecisionFixtureAcceptanceGateFilteringObjectRows), StringComparison.Ordinal) &&
            v.Reference == "DAL.Entities.RiskIndexObjectDay");
    }

    [Fact]
    public void TestAServiceThatDoesNotReadTheIndexIsNotFlagged()
    {
        Assert.DoesNotContain(FixtureViolations(), v =>
            v.Owner.EndsWith(nameof(DecisionFixtureCampaignWithoutTheIndex), StringComparison.Ordinal));
    }
}

/// <summary>Reads the snapshots only inside the body: no field, parameter or return type names the index.</summary>
internal sealed class DecisionFixtureCampaignEscalationReadingSnapshots
{
    public bool ShouldEscalate(NRDbContext db) => db.RiskIndexSnapshots.Count() > 70;
}

/// <summary>Resolves an index service by type inside an async method: the reference lives in the state machine.</summary>
internal sealed class DecisionFixtureSlaReminderAwaitingTheCalculator
{
    public async Task<bool> RemindAsync(IServiceProvider services)
    {
        await Task.Yield();
        return services.GetService(typeof(IRiskIndexDayCalculator)) is not null;
    }
}

/// <summary>Filters object rows in a lambda.</summary>
internal sealed class DecisionFixtureAcceptanceGateFilteringObjectRows
{
    public int Count(NRDbContext db) => db.RiskIndexObjectDays.Count(o => o.Category == RiskIndexCategory.Reg);
}

/// <summary>The control: a decision service that never touches the index.</summary>
internal sealed class DecisionFixtureCampaignWithoutTheIndex
{
    public int Count(NRDbContext db) => db.Risks.Count();
}
