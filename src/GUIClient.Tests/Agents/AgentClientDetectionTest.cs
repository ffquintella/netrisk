using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GUIClient.Agents;
using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T363, T365, T366 (S55 "Detect, then show", "Paths from the OS only"): the per-platform path table, every path built
/// from a known folder and contained in it, detection by file, folder or executable without starting anything, the four
/// states, undetected clients listed with a reason and still writable through a picked file, and ChatGPT as a guided row.
/// </summary>
[TestSubject(typeof(AgentClientRegistrar))]
public class AgentClientDetectionTest : IDisposable
{
    private static readonly AgentKnownFolders Fake = new(
        Rooted("home", "u"), Rooted("roaming"), Rooted("local"), Rooted("home", "u", "Library", "Application Support"),
        Rooted("xdg"), Rooted("Applications"));

    private readonly FakeAgentMachine _machine = new();

    public void Dispose() => _machine.Dispose();

    public static TheoryData<string, AgentPlatform, string> PathTable => new()
    {
        { "claude-code", AgentPlatform.MacOS, Path.Combine(Fake.Home, ".claude.json") },
        { "claude-code", AgentPlatform.Windows, Path.Combine(Fake.Home, ".claude.json") },
        { "claude-code", AgentPlatform.Linux, Path.Combine(Fake.Home, ".claude.json") },
        { "claude-desktop", AgentPlatform.MacOS, Path.Combine(Fake.MacApplicationSupport, "Claude", "claude_desktop_config.json") },
        { "claude-desktop", AgentPlatform.Windows, Path.Combine(Fake.RoamingAppData, "Claude", "claude_desktop_config.json") },
        { "claude-desktop", AgentPlatform.Linux, Path.Combine(Fake.XdgConfig, "Claude", "claude_desktop_config.json") },
        { "vscode", AgentPlatform.MacOS, Path.Combine(Fake.MacApplicationSupport, "Code", "User", "mcp.json") },
        { "vscode", AgentPlatform.Windows, Path.Combine(Fake.RoamingAppData, "Code", "User", "mcp.json") },
        { "vscode", AgentPlatform.Linux, Path.Combine(Fake.XdgConfig, "Code", "User", "mcp.json") },
        { "vscode-insiders", AgentPlatform.MacOS, Path.Combine(Fake.MacApplicationSupport, "Code - Insiders", "User", "mcp.json") },
        { "vscode-insiders", AgentPlatform.Windows, Path.Combine(Fake.RoamingAppData, "Code - Insiders", "User", "mcp.json") },
        { "vscode-insiders", AgentPlatform.Linux, Path.Combine(Fake.XdgConfig, "Code - Insiders", "User", "mcp.json") },
        { "copilot", AgentPlatform.MacOS, Path.Combine(Fake.Home, ".copilot", "mcp-config.json") },
        { "copilot", AgentPlatform.Windows, Path.Combine(Fake.Home, ".copilot", "mcp-config.json") },
        { "copilot", AgentPlatform.Linux, Path.Combine(Fake.Home, ".copilot", "mcp-config.json") }
    };

    [Theory]
    [MemberData(nameof(PathTable))]
    public void TestEachClientsConfigPathPerPlatform(string clientId, AgentPlatform platform, string expected)
    {
        var registrar = new AgentClientRegistrar(Fake, platform, null, TimeProvider.System);

        Assert.Equal(expected, registrar.DefaultConfigPath(AgentClientCatalog.All.Single(c => c.Id == clientId)));
    }

    [Fact]
    public void TestEveryPathComesFromAKnownFolderAndStaysInsideIt()
    {
        foreach (var client in AgentClientCatalog.All)
        foreach (var platform in Enum.GetValues<AgentPlatform>())
        {
            var probes = client.Probes.TryGetValue(platform, out var declared) ? declared : [];
            var specs = probes.Select(p => p.Path).OfType<AgentPathSpec>().ToList();
            if (client.Locations.TryGetValue(platform, out var location))
            {
                specs.Add(location.Directory);
                Assert.True(SafePathTool.IsSafeFileName(location.FileName), $"{client.Id}: {location.FileName}");
            }

            foreach (var spec in specs)
            {
                Assert.All(spec.Segments, s => Assert.True(
                    s.Length > 0 && s != "." && s != ".." && !s.Contains('/') && !s.Contains('\\') && !Path.IsPathRooted(s),
                    $"{client.Id} {platform}: '{s}'"));
                var folder = Fake.Of(spec.Folder);
                var resolved = Path.GetFullPath(spec.Resolve(Fake));
                Assert.True(resolved == Path.GetFullPath(folder) || resolved.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal), $"{client.Id} {platform}: {resolved}");
            }
        }
    }

    [Fact]
    public void TestTheClientTableIsDataWithADetectorAndAWriterPerRow()
    {
        Assert.Equal(["claude-code", "claude-desktop", "vscode", "vscode-insiders", "copilot", "chatgpt"],
            AgentClientCatalog.All.Select(c => c.Id).ToArray());
        foreach (var client in AgentClientCatalog.All)
        foreach (var platform in Enum.GetValues<AgentPlatform>())
        {
            Assert.NotEmpty(client.Probes[platform]);
            if (client.Support == AgentClientSupport.Automatic)
            {
                Assert.NotNull(client.Format);
                Assert.True(client.Locations.ContainsKey(platform), $"{client.Id} {platform}");
            }
            else
            {
                Assert.Null(client.Format);
                Assert.Empty(client.Locations);
                Assert.NotNull(client.GuideKey);
            }
        }
    }

    [Fact]
    public void TestAClientIsDetectedByItsFileItsFolderOrItsExecutableOnPath()
    {
        var code = AgentClientCatalog.ClaudeCode;
        Assert.False(_machine.Registrar().DetectInstalled(code).Detected);

        var bin = Path.Combine(_machine.Root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, FakeAgentMachine.Platform == AgentPlatform.Windows ? "claude.exe" : "claude"), "");
        var byPath = _machine.Registrar("relative-dir" + (OperatingSystem.IsWindows() ? ";" : ":") + bin).DetectInstalled(code);
        Assert.True(byPath.Detected);
        Assert.Equal(AgentProbeKind.Executable, byPath.By!.Kind);

        Directory.CreateDirectory(Path.Combine(_machine.Folders.Home, ".claude"));
        Assert.Equal(AgentProbeKind.Directory, _machine.Registrar().DetectInstalled(code).By!.Kind);

        File.WriteAllText(Path.Combine(_machine.Folders.Home, ".claude.json"), "{}");
        var byFile = _machine.Registrar().DetectInstalled(code);
        Assert.Equal(AgentProbeKind.File, byFile.By!.Kind);
        Assert.Equal(Path.Combine(_machine.Folders.Home, ".claude.json"), byFile.At);
    }

    [Fact]
    public void TestAnExecutableProbeIgnoresRelativePathEntries()
    {
        var relative = Path.Combine(_machine.Root, "rel");
        Directory.CreateDirectory(relative);
        File.WriteAllText(Path.Combine(relative, FakeAgentMachine.Platform == AgentPlatform.Windows ? "copilot.exe" : "copilot"), "");
        // The same folder, written relative to the working directory: it would be found if relative entries were used.
        var fromHere = Path.GetRelativePath(Environment.CurrentDirectory, relative);
        Assert.False(Path.IsPathFullyQualified(fromHere));

        Assert.False(_machine.Registrar(fromHere).DetectInstalled(AgentClientCatalog.Copilot).Detected);
        Assert.True(_machine.Registrar(relative).DetectInstalled(AgentClientCatalog.Copilot).Detected);
    }

    [Fact]
    public void TestTheStatesNotRegisteredRegisteredAndStale()
    {
        _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        var registrar = _machine.Registrar();
        Assert.Equal(AgentClientState.NotRegistered, registrar.Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);

        _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register);
        Assert.Equal(AgentClientState.Registered, registrar.Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);

        var moved = AgentBridgeLaunch.Create(Path.Combine(_machine.Root, "moved", "netrisk-mcp-bridge"), "production", null);
        Assert.Equal(AgentClientState.Stale, registrar.Status(AgentClientCatalog.ClaudeCode, moved).State);
        var otherEnvironment = AgentBridgeLaunch.Create(_machine.BridgePath, "homolog", null);
        Assert.Equal(AgentClientState.Stale, registrar.Status(AgentClientCatalog.ClaudeCode, otherEnvironment).State);

        // Without a bridge to compare with, an entry whose command no longer exists still reads as stale.
        Assert.Equal(AgentClientState.Registered, registrar.Status(AgentClientCatalog.ClaudeCode, null).State);
        File.Delete(_machine.BridgePath);
        Assert.Equal(AgentClientState.Stale, registrar.Status(AgentClientCatalog.ClaudeCode, null).State);
    }

    [Fact]
    public void TestAnUndetectedClientIsListedWithItsReasonAndCanBePointedAtAFile()
    {
        var registrar = _machine.Registrar();
        var status = registrar.Status(AgentClientCatalog.VsCodeInsiders, _machine.Launch);

        Assert.Equal(AgentClientState.NotDetected, status.State);
        Assert.Equal(AgentClientProblem.NotDetected, status.Problem);
        Assert.Equal(registrar.DefaultConfigPath(AgentClientCatalog.VsCodeInsiders), status.ConfigPath);
        Assert.False(status.CanRegister);
        Assert.Equal(AgentClientProblem.NotDetected,
            registrar.Plan(AgentClientCatalog.VsCodeInsiders, AgentClientAction.Register, _machine.Launch).Problem);

        var picked = Path.Combine(_machine.Root, "portable-vscode", "mcp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(picked)!);
        File.WriteAllText(picked, "{\n    \"servers\": {}\n}\n");
        var manual = registrar.Status(AgentClientCatalog.VsCodeInsiders, _machine.Launch, picked);
        Assert.True(manual.IsManualPath);
        Assert.True(manual.CanRegister);

        _machine.Run(AgentClientCatalog.VsCodeInsiders, AgentClientAction.Register, manualPath: picked);
        Assert.Equal(AgentClientState.Registered,
            registrar.Status(AgentClientCatalog.VsCodeInsiders, _machine.Launch, picked).State);
    }

    [Fact]
    public void TestAMissingConfigDirectoryIsReportedAndNotCreated()
    {
        // Claude Desktop installed (its app bundle) but never started: no settings folder yet.
        var mac = new AgentClientRegistrar(_machine.Folders, AgentPlatform.MacOS, null, _machine.Time);
        Directory.CreateDirectory(Path.Combine(_machine.Folders.Applications, "Claude.app"));

        var status = mac.Status(AgentClientCatalog.ClaudeDesktop, _machine.Launch);
        var plan = mac.Plan(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Register, _machine.Launch);

        Assert.Equal(AgentClientState.NotRegistered, status.State);
        Assert.Equal(AgentClientProblem.DirectoryMissing, status.Problem);
        Assert.False(status.CanRegister);
        Assert.Equal(AgentClientProblem.DirectoryMissing, plan.Problem);
        Assert.False(Directory.Exists(Path.Combine(_machine.Folders.MacApplicationSupport, "Claude")));
    }

    [Fact]
    public void TestChatGptIsGuidedAndNeverWritten()
    {
        Directory.CreateDirectory(Path.Combine(_machine.Folders.Home, ".codex"));
        var registrar = _machine.Registrar();

        var status = registrar.Status(AgentClientCatalog.ChatGpt, _machine.Launch);
        var plan = registrar.Plan(AgentClientCatalog.ChatGpt, AgentClientAction.Register, _machine.Launch);

        Assert.Equal(AgentClientState.Guided, status.State);
        Assert.NotNull(status.DetectedBy);
        Assert.Null(status.ConfigPath);
        Assert.False(status.CanRegister);
        Assert.Equal(AgentClientProblem.Guided, plan.Problem);
        Assert.False(registrar.Apply(plan).Written);
        Assert.Empty(Directory.GetFiles(Path.Combine(_machine.Folders.Home, ".codex")));
    }

    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void TestEveryClientStateProblemAndGuideKeyResolvesIn(string file)
    {
        var declared = XDocument.Load(GUIClient.Tests.Resources.EntityConfigurationSchema.ResourcePath(file)).Root!
            .Elements("data").Select(d => d.Attribute("name")?.Value).ToHashSet(StringComparer.Ordinal);

        var missing = AgentClientTexts.AllKeys.Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{file} does not declare: {string.Join(", ", missing)}");
    }

    [Fact]
    public void TestTheBridgeIsLookedForBesideNetRiskAndInTheBuildTreeOnly()
    {
        var app = Path.Combine(_machine.Root, "src", "GUIClient", "bin", "Debug", "net11.0");
        var bridge = Path.Combine(_machine.Root, "src", "McpBridge", "bin", "Debug", "net11.0",
            AgentBridgeLocator.ExecutableName(FakeAgentMachine.Platform));

        var candidates = AgentBridgeLocator.Candidates(app, FakeAgentMachine.Platform);
        Assert.Equal([Path.Combine(app, AgentBridgeLocator.ExecutableName(FakeAgentMachine.Platform)), bridge], candidates);
        Assert.Null(AgentBridgeLocator.Find(app, FakeAgentMachine.Platform));

        Directory.CreateDirectory(Path.GetDirectoryName(bridge)!);
        File.WriteAllText(bridge, "");
        Assert.Equal(bridge, AgentBridgeLocator.Find(app, FakeAgentMachine.Platform));

        Assert.Single(AgentBridgeLocator.Candidates(Path.Combine(_machine.Root, "Program Files", "NetRisk"), FakeAgentMachine.Platform));
    }

    [Fact]
    public void TestDotnetRootIsTakenOnlyFromASharedRuntime()
    {
        var root = Path.Combine(_machine.Root, "dotnet");
        var runtime = Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.0");
        Directory.CreateDirectory(runtime);

        Assert.Null(AgentBridgeLocator.DotnetRootOf(runtime, FakeAgentMachine.Platform));
        File.WriteAllText(Path.Combine(root, FakeAgentMachine.Platform == AgentPlatform.Windows ? "dotnet.exe" : "dotnet"), "");
        Assert.Equal(root, AgentBridgeLocator.DotnetRootOf(runtime + Path.DirectorySeparatorChar, FakeAgentMachine.Platform));
        // A self-contained NetRisk runs from its own folder: no shared root to pass on.
        Assert.Null(AgentBridgeLocator.DotnetRootOf(Path.Combine(_machine.Root, "app"), FakeAgentMachine.Platform));
    }

    private static string Rooted(params string[] segments) =>
        Path.Combine(new[] { OperatingSystem.IsWindows() ? @"C:\fake" : "/fake" }.Concat(segments).ToArray());
}
