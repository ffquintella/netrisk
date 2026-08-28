using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Packaging.Tests;

/// <summary>
/// Track 7 milestones 7.2.1 and 7.5.1 — the continuous-security configuration, checked here because
/// nothing else can check it.
///
/// A malformed <c>.github/workflows/security.yml</c> does not fail a build; GitHub silently declines
/// to run the workflow, and the repository then has a security gate that exists on disk and nowhere
/// else. That is the same class of failure as a documented-but-absent control, which is what this
/// whole track is about — so the workflow, the Dependabot configuration and the suppression file are
/// parsed and asserted on the same way the installer manifests are.
/// </summary>
public class ContinuousSecurityConfigurationTest
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "netrisk.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(segments).ToArray()));

    private static YamlMappingNode Parse(params string[] segments)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(Read(segments));
        stream.Load(reader);

        Assert.Single(stream.Documents);
        return Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    // ---- The security workflow -------------------------------------------------------------

    [Fact]
    public void TheSecurityWorkflowParses() => Assert.NotNull(Parse(".github", "workflows", "security.yml"));

    /// <summary>
    /// The four gates the milestone asks for. Named individually so a deletion says which one went.
    /// </summary>
    [Theory]
    [InlineData("dependency-scan")]
    [InlineData("secret-scan")]
    [InlineData("codeql")]
    [InlineData("submodule-review")]
    public void TheWorkflowDefinesEveryGate(string job)
    {
        var jobs = Assert.IsType<YamlMappingNode>(Child(Parse(".github", "workflows", "security.yml"), "jobs"));

        Assert.Contains(new YamlScalarNode(job), jobs.Children.Keys);
    }

    /// <summary>
    /// A scheduled run matters as much as the pull-request run: a new advisory published against an
    /// already-pinned version appears in no diff at all.
    /// </summary>
    [Fact]
    public void TheWorkflowRunsOnASchedule()
    {
        var workflow = Read(".github", "workflows", "security.yml");

        Assert.Contains("schedule:", workflow, StringComparison.Ordinal);
        Assert.Contains("cron:", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The secret scan needs the whole history: a credential deleted in a later commit was still
    /// published, and a shallow clone is how that goes unnoticed.
    /// </summary>
    [Fact]
    public void TheSecretScanFetchesTheFullHistory()
    {
        var workflow = Read(".github", "workflows", "security.yml");

        Assert.Contains("fetch-depth: 0", workflow, StringComparison.Ordinal);
        Assert.Contains("gitleaks", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// CodeQL needs <c>security-events: write</c> to upload results, and the top-level grant is
    /// deliberately read-only, so the job has to raise it for itself.
    /// </summary>
    [Fact]
    public void TheCodeQlJobRequestsTheSecurityEventsScope()
    {
        var workflow = Read(".github", "workflows", "security.yml");

        Assert.Contains("security-events: write", workflow, StringComparison.Ordinal);
        Assert.Contains("permissions:\n  contents: read", workflow.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The workflow-injection guard: a pull-request body is attacker-controlled text, so it must
    /// reach the script through the environment and never be interpolated into a <c>run:</c> line
    /// where the shell would parse it.
    /// </summary>
    [Fact]
    public void TheUntrustedPullRequestBodyIsPassedThroughTheEnvironment()
    {
        var workflow = Read(".github", "workflows", "security.yml").Replace("\r\n", "\n");

        Assert.Contains("PR_BODY: ${{ github.event.pull_request.body }}", workflow, StringComparison.Ordinal);

        // No `run:` line may contain a github.event expression at all.
        var offenders = workflow.Split('\n')
            .Where(line => line.TrimStart().StartsWith("run:", StringComparison.Ordinal))
            .Where(line => line.Contains("${{", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "A run: line interpolates a workflow expression directly:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>The gates invoke committed scripts, so a developer can run exactly what CI runs.</summary>
    [Theory]
    [InlineData("scan-dependencies.sh")]
    [InlineData("check-submodule-bump.sh")]
    public void TheGateScriptsExistAndAreReferenced(string script)
    {
        var path = Path.Combine(RepositoryRoot(), "scripts", "security", script);

        Assert.True(File.Exists(path), $"{path} is missing");
        Assert.Contains(script, Read(".github", "workflows", "security.yml"), StringComparison.Ordinal);
    }

    // ---- Dependabot -------------------------------------------------------------------------

    [Fact]
    public void TheDependabotConfigurationParses() => Assert.NotNull(Parse(".github", "dependabot.yml"));

    /// <summary>
    /// The three ecosystems the milestone names: the shipped packages, the workflow's own actions,
    /// and the vendored submodules.
    /// </summary>
    [Theory]
    [InlineData("nuget")]
    [InlineData("github-actions")]
    [InlineData("gitsubmodule")]
    public void DependabotWatchesEveryEcosystem(string ecosystem)
    {
        var updates = Assert.IsType<YamlSequenceNode>(Child(Parse(".github", "dependabot.yml"), "updates"));

        var ecosystems = updates.Children
            .OfType<YamlMappingNode>()
            .Select(u => (Child(u, "package-ecosystem") as YamlScalarNode)?.Value)
            .ToList();

        Assert.Contains(ecosystem, ecosystems);
    }

    /// <summary>
    /// Both manifest directories are watched. <c>/src</c> alone would leave the Nuke build's own
    /// dependencies unscanned, and the build is what produces the artifacts.
    /// </summary>
    [Fact]
    public void DependabotWatchesBothTheSolutionAndTheBuildProject()
    {
        var updates = Assert.IsType<YamlSequenceNode>(Child(Parse(".github", "dependabot.yml"), "updates"));

        var nugetDirectories = updates.Children
            .OfType<YamlMappingNode>()
            .Where(u => (Child(u, "package-ecosystem") as YamlScalarNode)?.Value == "nuget")
            .Select(u => (Child(u, "directory") as YamlScalarNode)?.Value)
            .ToList();

        Assert.Contains("/src", nugetDirectories);
        Assert.Contains("/build", nugetDirectories);
    }

    // ---- The CodeQL configuration ------------------------------------------------------------

    [Fact]
    public void TheCodeQlConfigurationParses() => Assert.NotNull(Parse(".github", "codeql", "codeql-config.yml"));

    /// <summary>
    /// The submodules are somebody else's code: an alert there cannot be fixed in a NetRisk pull
    /// request, and a permanent unfixable alert is how a team learns to ignore the alert list.
    /// </summary>
    [Fact]
    public void CodeQlExcludesTheVendoredSubmodules()
    {
        var ignored = Assert.IsType<YamlSequenceNode>(
            Child(Parse(".github", "codeql", "codeql-config.yml"), "paths-ignore"));

        Assert.Contains("libs", ignored.Children.OfType<YamlScalarNode>().Select(n => n.Value));
    }

    // ---- The dependency suppression file -----------------------------------------------------

    [Fact]
    public void TheSuppressionFileParses() => Assert.NotNull(Parse("security", "dependency-suppressions.yml"));

    /// <summary>
    /// The schema the gate script enforces, checked here with a real YAML parser so that a shape the
    /// script's bash parser would misread fails in a test rather than in CI.
    ///
    /// Every entry needs an expiry, and the expiry has to be in the future and no more than 180 days
    /// out. That is the property that stops the file from silently becoming a permanent list.
    /// </summary>
    [Fact]
    public void EverySuppressionHasARealExpiryAnOwnerAndAReason()
    {
        var root = Parse("security", "dependency-suppressions.yml");
        var suppressions = Child(root, "suppressions");

        // An empty list is the expected state; a null (key present, no value) is not, because the
        // script would then read the file as having no suppressions block at all.
        Assert.NotNull(suppressions);

        if (suppressions is not YamlSequenceNode sequence) return;

        foreach (var entry in sequence.Children.OfType<YamlMappingNode>())
        {
            var package = (Child(entry, "package") as YamlScalarNode)?.Value ?? "(unnamed)";

            Assert.False(string.IsNullOrWhiteSpace((Child(entry, "reason") as YamlScalarNode)?.Value),
                $"{package} has no reason");
            Assert.False(string.IsNullOrWhiteSpace((Child(entry, "owner") as YamlScalarNode)?.Value),
                $"{package} has no owner");
            Assert.False(string.IsNullOrWhiteSpace((Child(entry, "advisory") as YamlScalarNode)?.Value),
                $"{package} names no advisory");

            var expires = (Child(entry, "expires") as YamlScalarNode)?.Value;
            Assert.False(string.IsNullOrWhiteSpace(expires), $"{package} has no expiry");

            Assert.True(
                DateTime.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expiry),
                $"{package} has an unparseable expiry '{expires}'");

            Assert.True(expiry > DateTime.UtcNow.Date,
                $"the suppression for {package} expired on {expires}");
            Assert.True(expiry <= DateTime.UtcNow.Date.AddDays(180),
                $"the suppression for {package} expires more than 180 days out; that is a remediation "
                + "plan, not a suppression");
        }
    }

    /// <summary>
    /// The rules the file documents have to match the ones the gate enforces, or the header becomes
    /// folklore.
    /// </summary>
    [Fact]
    public void TheSuppressionFileAndTheGateAgreeOnTheMaximumWindow()
    {
        Assert.Contains("180", Read("security", "dependency-suppressions.yml"), StringComparison.Ordinal);
        Assert.Contains("MAX_SUPPRESSION_DAYS=180",
            Read("scripts", "security", "scan-dependencies.sh"), StringComparison.Ordinal);
    }

    // ---- gitleaks ---------------------------------------------------------------------------

    /// <summary>
    /// The allowlist has to name the committed development certificates file by file. A blanket
    /// <c>*.pfx</c> exclusion would silence the scanner for the next key somebody adds — which is the
    /// finding (NR-2026-003) this configuration exists to stop recurring.
    /// </summary>
    [Fact]
    public void TheGitleaksAllowlistNamesCertificatesIndividuallyRatherThanByExtension()
    {
        var config = Read(".gitleaks.toml");

        Assert.Contains("src/API/Certificates/certificate", config, StringComparison.Ordinal);
        Assert.DoesNotContain("'''.*\\.pfx$'''", config, StringComparison.Ordinal);
        Assert.DoesNotContain("**/*.pfx", config, StringComparison.Ordinal);
    }

    /// <summary>The NetRisk-specific credential shapes the default rule set does not know about.</summary>
    [Theory]
    [InlineData("netrisk-api-token")]
    [InlineData("netrisk-scim-token")]
    [InlineData("netrisk-db-connection-password")]
    public void TheGitleaksConfigurationCarriesTheNetRiskRules(string ruleId) =>
        Assert.Contains($"id = \"{ruleId}\"", Read(".gitleaks.toml"), StringComparison.Ordinal);

    /// <summary>
    /// The token prefixes in the rules have to be the ones the code actually issues, or the rule
    /// matches nothing.
    /// </summary>
    [Fact]
    public void TheTokenPrefixRulesMatchThePrefixesTheCodeIssues()
    {
        var config = Read(".gitleaks.toml");

        Assert.Contains("nrk_", config, StringComparison.Ordinal);
        Assert.Contains("scim_", config, StringComparison.Ordinal);
        Assert.Contains("nrk_", Read("src", "DAL", "Entities", "ApiToken.cs"), StringComparison.Ordinal);
        Assert.Contains("scim_", Read("src", "DAL", "Entities", "ScimToken.cs"), StringComparison.Ordinal);
    }

    // ---- The gitleaks rules, executed rather than grepped ------------------------------------
    //
    // Everything above this line asserts that a string appears in .gitleaks.toml. That is what let
    // the secret-scan gate ship broken from the day it was added: the config contained a negative
    // lookahead, gitleaks compiles its patterns with RE2, and the job panicked in config
    // translation on every run without ever scanning a commit. A substring assertion cannot see
    // that. These tests compile and run the patterns instead.

    /// <summary>Every <c>'''…'''</c> literal in the config — rule regexes, allowlist regexes, paths.</summary>
    private static IEnumerable<(int Line, string Pattern)> GitleaksPatterns()
    {
        var lines = Read(".gitleaks.toml").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], "'''(.*?)'''");
            if (match.Success) yield return (i + 1, match.Groups[1].Value);
        }
    }

    /// <summary>
    /// RE2 — which is what gitleaks compiles with, through wasilibs/go-re2 — has no lookaround and
    /// no atomic groups. A pattern using one does not degrade to a missed finding: gitleaks panics
    /// while reading the config, so the whole gate reports nothing at all.
    /// </summary>
    [Fact]
    public void EveryGitleaksPatternIsRe2Compatible()
    {
        string[] unsupported = ["(?=", "(?!", "(?<=", "(?<!", "(?>"];
        var patterns = GitleaksPatterns().ToList();

        Assert.NotEmpty(patterns);

        foreach (var (line, pattern) in patterns)
        {
            foreach (var construct in unsupported)
            {
                Assert.False(
                    pattern.Contains(construct, StringComparison.Ordinal),
                    $".gitleaks.toml line {line} uses '{construct}', which RE2 cannot compile, so " +
                    $"gitleaks panics before it scans anything: {pattern}");
            }

            // Malformed in any other way is just as fatal, and just as invisible from a substring test.
            Assert.NotNull(new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(2)));
        }
    }

    /// <summary>The regex belonging to one rule id, read out of the config the gate actually uses.</summary>
    private static Regex RuleRegex(string ruleId)
    {
        var lines = Read(".gitleaks.toml").Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains($"id = \"{ruleId}\"", StringComparison.Ordinal));

        Assert.True(start >= 0, $"Rule '{ruleId}' is not in .gitleaks.toml.");

        for (var i = start; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], "^regex = '''(.*)'''$");
            if (match.Success) return new Regex(match.Groups[1].Value, RegexOptions.None, TimeSpan.FromSeconds(2));
        }

        throw new Xunit.Sdk.XunitException($"Rule '{ruleId}' has no regex.");
    }

    /// <summary>
    /// A token as <c>ApiTokensService.Compose</c> and <c>ScimService.Compose</c> build one: the
    /// prefix, a key id that is 8 CSPRNG bytes as lowercase hex, an underscore, and a secret that is
    /// 32 CSPRNG bytes as unpadded base64url.
    /// </summary>
    private static string IssuedToken(string prefix)
    {
        var keyId = Convert.ToHexString(Enumerable.Range(0, 8).Select(i => (byte)(i * 17)).ToArray())
            .ToLowerInvariant();
        var secret = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{prefix}{keyId}_{secret}";
    }

    [Theory]
    [InlineData("netrisk-api-token", "nrk_")]
    [InlineData("netrisk-scim-token", "scim_")]
    public void TheTokenRulesMatchATokenTheServicesWouldActuallyIssue(string ruleId, string prefix) =>
        Assert.Matches(RuleRegex(ruleId), IssuedToken(prefix));

    /// <summary>
    /// And do not match the schema. The first form of these rules was "the prefix plus 20 word
    /// characters", which matched every table, index and foreign key the SCIM feature owns — 72
    /// findings, none of them a credential. A gate that cries wolf 72 times gets switched off.
    /// </summary>
    [Theory]
    [InlineData("scim_tokens")]
    [InlineData("scim_request_logs")]
    [InlineData("idx_scim_request_logs_occurred_at")]
    [InlineData("fk_scim_tokens_identity_provider_id")]
    [InlineData("scim_tokens_identity_provider_id")]
    public void TheScimTokenRuleDoesNotMatchTheScimSchema(string identifier) =>
        Assert.DoesNotMatch(RuleRegex("netrisk-scim-token"), identifier);

    [Theory]
    [InlineData("server=db;uid=netrisk;pwd=Tr0ub4dor3xample;database=netrisk")]
    [InlineData("Database__ConnectionString=server=db;PASSWORD=Tr0ub4dor3xample")]
    public void TheConnectionStringRuleMatchesAConnectionStringPassword(string line) =>
        Assert.Matches(RuleRegex("netrisk-db-connection-password"), line);

    /// <summary>
    /// A connection string writes <c>pwd=value</c>; <c>password = expression</c> with spaces around
    /// the equals sign is an assignment in C#, Puppet or shell. Matching those was the rest of this
    /// rule's noise.
    /// </summary>
    [Theory]
    [InlineData("$db_password = String(file('/etc/netrisk/db.pwd'))")]
    [InlineData("password = HttpUtility.UrlEncode(password)")]
    [InlineData("password=<%= $netrisk::db_password %>")]
    public void TheConnectionStringRuleDoesNotMatchAnAssignment(string line) =>
        Assert.DoesNotMatch(RuleRegex("netrisk-db-connection-password"), line);

    [Fact]
    public void TheCertificatePasswordRuleIgnoresAnEmptyOrBlankValue()
    {
        var rule = RuleRegex("netrisk-certificate-password");

        Assert.Matches(rule, "\"Password\": \"N0tThePlaceholder\"");
        Assert.DoesNotMatch(rule, "\"Password\": \"\"");
        Assert.DoesNotMatch(rule, "\"Password\": \"   \"");
    }

    // ---- The dependency gate can actually reach every project ---------------------------------

    /// <summary>
    /// <c>dotnet list package</c> enumerates every project in the solution and needs an assets file
    /// for each, but <c>dotnet restore &lt;solution&gt;</c> does not produce one for every project the
    /// solution contains. A project mapped with <c>ActiveCfg</c> and no <c>Build.0</c> is skipped —
    /// which is exactly how Nuke registers <c>build/build.csproj</c>, so that building the solution
    /// does not build the build script. The scan then dies with "No assets file was found", and it
    /// did, on this gate's first run that ever got far enough to try.
    ///
    /// So every project the solution declines to build has to be named for restore in the scan
    /// script. Adding another such project without doing so breaks the gate again, and fails here.
    /// </summary>
    [Fact]
    public void TheDependencyScanRestoresEveryProjectTheSolutionWillNotBuild()
    {
        var solution = Read("src", "netrisk.sln");
        var script = Read("scripts", "security", "scan-dependencies.sh");

        var paths = Regex.Matches(solution, @"= ""[^""]+"", ""([^""]+)"", ""\{([^}]+)\}""")
            .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);

        var active = Regex.Matches(solution, @"\{([^}]+)\}\.Debug\|Any CPU\.ActiveCfg")
            .Select(m => m.Groups[1].Value).ToHashSet();
        var built = Regex.Matches(solution, @"\{([^}]+)\}\.Debug\|Any CPU\.Build\.0")
            .Select(m => m.Groups[1].Value).ToHashSet();

        var skipped = active.Except(built).ToList();

        // If this is ever empty the invariant is vacuous and the test protects nothing.
        Assert.NotEmpty(skipped);

        foreach (var guid in skipped)
        {
            // Solution paths are relative to the solution file, and use backslashes.
            var relative = Path.GetRelativePath(
                    RepositoryRoot(),
                    Path.GetFullPath(Path.Combine(RepositoryRoot(), "src", paths[guid].Replace('\\', '/'))))
                .Replace('\\', '/');

            Assert.True(
                script.Contains(relative, StringComparison.Ordinal),
                $"'{relative}' is in netrisk.sln but the solution will not build it, so " +
                "`dotnet restore <solution>` leaves it without an assets file and " +
                "`dotnet list package` fails on it. scan-dependencies.sh has to restore it by name.");
        }
    }

    /// <summary>The restore is only useful before the listing that needs it.</summary>
    [Fact]
    public void TheDependencyScanRestoresBeforeItLists()
    {
        var script = Read("scripts", "security", "scan-dependencies.sh");

        var restore = script.IndexOf(@"dotnet restore ""${project}""", StringComparison.Ordinal);
        var list = script.IndexOf(@"dotnet list ""${SOLUTION}"" package", StringComparison.Ordinal);

        Assert.True(restore >= 0, "scan-dependencies.sh no longer restores the unbuilt projects.");
        Assert.True(list >= 0, "scan-dependencies.sh no longer lists packages.");
        Assert.True(restore < list, "The restore has to run before the listing that depends on it.");
    }

    // ---- The baseline -------------------------------------------------------------------------

    /// <summary>
    /// A gitleaks fingerprint is <c>commit:path:rule:line</c>. A typo in one is silent — the entry
    /// suppresses nothing and the gate stays red — so the shape is checked here, along with the
    /// rule id, which has to name a rule that still exists.
    /// </summary>
    [Fact]
    public void EveryBaselineEntryIsAWellFormedFingerprint()
    {
        var config = Read(".gitleaks.toml");
        var entries = Read(".gitleaksignore")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            var parts = entry.Split(':');

            Assert.True(parts.Length == 4, $"Not a commit:path:rule:line fingerprint: {entry}");
            Assert.Matches("^[0-9a-f]{40}$", parts[0]);
            Assert.True(int.TryParse(parts[3], out _), $"Line number is not a number: {entry}");

            // Either a NetRisk rule, which must still be declared, or one of the upstream defaults.
            if (parts[2].StartsWith("netrisk-", StringComparison.Ordinal))
                Assert.Contains($"id = \"{parts[2]}\"", config, StringComparison.Ordinal);
        }
    }
}
