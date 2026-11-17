using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T364, T366 (S55 "Merge, never overwrite"): registering adds or replaces the <c>netrisk</c> entry and nothing else —
/// other servers and keys are kept byte for byte, a file that does not parse is left untouched and reported, a
/// timestamped <c>.bak</c> is kept, the write is atomic, re-running changes nothing, and no port, token or key is written.
/// </summary>
[TestSubject(typeof(AgentClientRegistrar))]
public class AgentClientRegistrarMergeTest : IDisposable
{
    private readonly FakeAgentMachine _machine = new();

    public void Dispose() => _machine.Dispose();

    [Fact]
    public void TestOtherServersAndKeysAreKeptByteForByte()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        var before = File.ReadAllBytes(path);

        var result = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register);

        Assert.True(result.Written);
        var after = File.ReadAllBytes(path);
        // A pure insertion: the original bytes are the new file with one contiguous run taken out.
        var prefix = CommonPrefix(before, after);
        var suffix = CommonSuffix(before, after, prefix);
        Assert.Equal(before.Length, prefix + suffix);

        var oldRoot = (JsonObject)JsonNode.Parse(before)!;
        var newRoot = FakeAgentMachine.Parse(path);
        foreach (var key in new[] { "numStartups", "theme", "projects", "userID" })
            Assert.True(JsonNode.DeepEquals(oldRoot[key], newRoot[key]), key);
        Assert.True(JsonNode.DeepEquals(oldRoot["mcpServers"]!["github"], newRoot["mcpServers"]!["github"]));
        Assert.True(JsonNode.DeepEquals(oldRoot["mcpServers"]!["weather"], newRoot["mcpServers"]!["weather"]));
        Assert.Equal(_machine.BridgePath, (string)newRoot["mcpServers"]!["netrisk"]!["command"]!);
        // The project-scoped servers were not where the entry went.
        Assert.Null(newRoot["projects"]!["/work/repo"]!["mcpServers"]!["netrisk"]);
    }

    [Fact]
    public void TestTheEntryLaunchesTheBridgeAndCarriesNoPortTokenOrKey()
    {
        // A running GUI's discovery file, with its launch token, sits in the profile; none of it may reach a client file.
        var discovery = AgentDiscoveryFile.DefaultPath("production", _machine.Folders.RoamingAppData);
        AgentDiscoveryFile.Write(discovery, new AgentDiscoveryInfo("http://127.0.0.1:45123/mcp",
            "launchTokenThatMustNeverLeaveTheProfile0123456789", 4242, DateTimeOffset.UtcNow));
        var launch = AgentBridgeLaunch.Create(_machine.BridgePath, "homolog", "/opt/dotnet");

        foreach (var client in AgentClientCatalog.All.Where(c => c.Support == AgentClientSupport.Automatic))
        {
            var path = _machine.ConfigOf(client);
            Assert.True(_machine.Run(client, AgentClientAction.Register, launch).Written, client.Id);

            var text = File.ReadAllText(path);
            foreach (var forbidden in new[] { "127.0.0.1", "45123", "launch_token", "launchTokenThatMustNever", "agent_key", "Bearer" })
                Assert.DoesNotContain(forbidden, text);

            var entry = (JsonObject)FakeAgentMachine.Parse(path, client.Format!.Jsonc)[client.Format.ContainerKey]!["netrisk"]!;
            Assert.All(entry.Select(p => p.Key), k => Assert.Contains(k, new[] { "type", "command", "args", "env", "tools" }));
            Assert.Equal(_machine.BridgePath, (string)entry["command"]!);
            Assert.Equal(["--environment", "homolog"], entry["args"]!.AsArray().Select(a => (string)a!).ToArray());
            Assert.Equal(["DOTNET_ROOT"], entry["env"]!.AsObject().Select(p => p.Key).ToArray());
        }
    }

    [Fact]
    public void TestEachClientGetsTheEntryShapeItsDocumentationGives()
    {
        JsonObject EntryOf(AgentClientDefinition client)
        {
            var path = _machine.ConfigOf(client);
            _machine.Run(client, AgentClientAction.Register);
            return (JsonObject)FakeAgentMachine.Parse(path, client.Format!.Jsonc)[client.Format.ContainerKey]!["netrisk"]!;
        }

        Assert.Equal("stdio", (string)EntryOf(AgentClientCatalog.ClaudeCode)["type"]!);
        Assert.Null(EntryOf(AgentClientCatalog.ClaudeDesktop)["type"]);
        Assert.Equal("stdio", (string)EntryOf(AgentClientCatalog.VsCode)["type"]!);
        var copilot = EntryOf(AgentClientCatalog.Copilot);
        Assert.Equal("stdio", (string)copilot["type"]!);
        Assert.Equal(["*"], copilot["tools"]!.AsArray().Select(t => (string)t!).ToArray());
        Assert.Empty(EntryOf(AgentClientCatalog.ClaudeDesktop)["args"]!.AsArray());
    }

    [Theory]
    [InlineData("{\"mcpServers\": {", AgentClientProblem.Unparseable)]
    [InlineData("{\"mcpServers\": {}} trailing", AgentClientProblem.Unparseable)]
    [InlineData("{ // a comment Claude Code itself would reject\n \"mcpServers\": {} }", AgentClientProblem.Unparseable)]
    [InlineData("{\"mcpServers\": {\"a\": {},}}", AgentClientProblem.Unparseable)]
    [InlineData("[1, 2]", AgentClientProblem.RootNotAnObject)]
    [InlineData("{\"mcpServers\": []}", AgentClientProblem.ContainerNotAnObject)]
    [InlineData("{\"mcpServers\": {}, \"mcpServers\": {}}", AgentClientProblem.DuplicateContainer)]
    [InlineData("{\"mcpServers\": {\"netrisk\": {}, \"netrisk\": {}}}", AgentClientProblem.DuplicateEntry)]
    public void TestAMalformedFileIsLeftUntouchedAndReported(string content, AgentClientProblem expected)
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, content);
        var before = File.ReadAllBytes(path);
        var registrar = _machine.Registrar();

        var status = registrar.Status(AgentClientCatalog.ClaudeCode, _machine.Launch);
        var plan = registrar.Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, _machine.Launch);
        var result = registrar.Apply(plan);

        Assert.Equal(AgentClientState.Unreadable, status.State);
        Assert.Equal(expected, status.Problem);
        Assert.Equal(expected, plan.Problem);
        Assert.False(plan.HasChanges);
        Assert.False(result.Written);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(FakeAgentMachine.Siblings(path).Where(n => n.Contains("netrisk", StringComparison.Ordinal)));
    }

    [Fact]
    public void TestAJsoncFileWithOnlyCommentsIsNotReplaced()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.VsCode, "// nothing yet\n");

        var plan = _machine.Registrar().Plan(AgentClientCatalog.VsCode, AgentClientAction.Register, _machine.Launch);

        Assert.Equal(AgentClientProblem.Unparseable, plan.Problem);
        Assert.Equal("// nothing yet\n", File.ReadAllText(path));
    }

    [Fact]
    public void TestABackupOfTheOriginalIsKeptBesideItWithItsPermissions()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var before = File.ReadAllBytes(path);

        var result = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register);

        Assert.Equal(Path.Combine(Path.GetDirectoryName(path)!, ".claude.json.netrisk-20261117T120000000Z.bak"),
            result.BackupPath);
        Assert.Equal(before, File.ReadAllBytes(result.BackupPath!));
        if (!OperatingSystem.IsWindows())
        {
            // The file holds account details and other servers' tokens: neither copy may be wider than the original.
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(result.BackupPath!));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void TestASecondBackupInTheSameMillisecondGetsItsOwnName()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        var first = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register);

        var second = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Unregister);

        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.EndsWith(".netrisk-20261117T120000000Z-2.bak", second.BackupPath);
        Assert.Equal(AgentClientSamples.ClaudeCode, File.ReadAllText(path));
    }

    [Fact]
    public void TestRegisteringTwiceChangesNothing()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register);
        var once = File.ReadAllBytes(path);
        var siblings = FakeAgentMachine.Siblings(path);

        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, _machine.Launch);
        var again = _machine.Registrar().Apply(plan);

        Assert.Equal(AgentClientProblem.None, plan.Problem);
        Assert.False(plan.HasChanges);
        Assert.False(again.Written);
        Assert.Null(again.BackupPath);
        Assert.Equal(once, File.ReadAllBytes(path));
        Assert.Equal(siblings, FakeAgentMachine.Siblings(path));
        Assert.Equal(AgentClientState.Registered, _machine.Registrar().Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);
    }

    [Fact]
    public void TestTheWriteLeavesNoTemporaryFileAndKeepsTheModeOfTheOriginal()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeDesktop, "{\n  \"mcpServers\": {}\n}\n",
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        _machine.Run(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Register);

        Assert.Equal(["claude_desktop_config.json.netrisk-20261117T120000000Z.bak"], FakeAgentMachine.Siblings(path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                File.GetUnixFileMode(path));
    }

    [Fact]
    public void TestAMissingFileIsCreatedOwnerOnlyInTheClientsDirectory()
    {
        var path = _machine.ConfigOf(AgentClientCatalog.ClaudeDesktop);

        var result = _machine.Run(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Register);

        Assert.True(result.Written);
        Assert.Null(result.BackupPath);
        Assert.Equal(_machine.BridgePath, (string)FakeAgentMachine.Parse(path)["mcpServers"]!["netrisk"]!["command"]!);
        Assert.Empty(FakeAgentMachine.Siblings(path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void TestAFileChangedAfterThePreviewIsNotWritten()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        var registrar = _machine.Registrar();
        var plan = registrar.Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, _machine.Launch);
        // The client saved its own state while the preview was open.
        File.WriteAllText(path, AgentClientSamples.ClaudeCode.Replace("42", "43", StringComparison.Ordinal));

        var result = registrar.Apply(plan);

        Assert.Equal(AgentClientProblem.ChangedSincePreview, result.Problem);
        Assert.False(result.Written);
        Assert.Contains("\"numStartups\": 43", File.ReadAllText(path));
        Assert.Empty(FakeAgentMachine.Siblings(path).Where(n => n.Contains("netrisk", StringComparison.Ordinal)));
    }

    [Fact]
    public void TestTheLastCheckRefusesAPlanThatWouldTouchAnythingButTheEntry()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        var registrar = _machine.Registrar();
        var honest = registrar.Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, _machine.Launch);
        var tampered = Encoding.UTF8.GetString(honest.After!).Replace("\"theme\": \"dark\"", "\"theme\": \"light\"",
            StringComparison.Ordinal);
        var forged = new AgentClientPlan(honest.Client, honest.Action, honest.ConfigPath, AgentClientProblem.None, true,
            honest.Diff, honest.Before, Encoding.UTF8.GetBytes(tampered), honest.Entry);

        var result = registrar.Apply(forged);

        Assert.Equal(AgentClientProblem.VerificationFailed, result.Problem);
        Assert.Equal(AgentClientSamples.ClaudeCode, File.ReadAllText(path));
        Assert.Empty(FakeAgentMachine.Siblings(path).Where(n => n.Contains("netrisk", StringComparison.Ordinal)));
    }

    [Fact]
    public void TestAStaleEntryIsReplacedInPlace()
    {
        var old = AgentBridgeLaunch.Create(Path.Combine(_machine.Root, "old", "netrisk-mcp-bridge"), "production", null);
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);
        _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, old);
        Assert.Equal(AgentClientState.Stale, _machine.Registrar().Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);

        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, _machine.Launch);
        _machine.Registrar().Apply(plan);

        Assert.Contains("-", plan.Diff);
        Assert.Contains(old.Command, plan.Diff);
        Assert.Contains(_machine.BridgePath, plan.Diff);
        Assert.Equal(AgentClientState.Registered, _machine.Registrar().Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);
        var servers = FakeAgentMachine.Parse(path)["mcpServers"]!.AsObject().Select(p => p.Key).ToArray();
        Assert.Equal(["netrisk", "github", "weather"], servers);
    }

    [Fact]
    public void TestVsCodeCommentsAndTrailingCommasSurvive()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.VsCode, AgentClientSamples.VsCodeWithComments);

        _machine.Run(AgentClientCatalog.VsCode, AgentClientAction.Register);

        var text = File.ReadAllText(path);
        Assert.Contains("// Servers for Copilot", text);
        Assert.Contains("/* the team's search server */", text);
        var root = FakeAgentMachine.Parse(path, jsonc: true);
        Assert.Equal(["netrisk", "search"], root["servers"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.NotNull(root["inputs"]);
    }

    [Fact]
    public void TestAByteOrderMarkAndWindowsLineEndingsSurvive()
    {
        var content = "﻿{\r\n    \"mcpServers\": {\r\n        \"other\": { \"command\": \"x\" }\r\n    }\r\n}\r\n";
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeDesktop, content);

        _machine.Run(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Register);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.Contains("\r\n        \"netrisk\": {\r\n            \"command\"", text);
    }

    [Fact]
    public void TestTheEntryGoesIntoAnEmptyOrAbsentServersObject()
    {
        var empty = _machine.WriteConfig(AgentClientCatalog.ClaudeDesktop, "{\n  \"globalShortcut\": \"\",\n  \"mcpServers\": {}\n}\n");
        _machine.Run(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Register);
        var withEmpty = FakeAgentMachine.Parse(empty);

        var absent = _machine.WriteConfig(AgentClientCatalog.Copilot, "{\n  \"other\": true\n}\n");
        _machine.Run(AgentClientCatalog.Copilot, AgentClientAction.Register);
        var withAbsent = FakeAgentMachine.Parse(absent);

        Assert.Equal("", (string)withEmpty["globalShortcut"]!);
        Assert.NotNull(withEmpty["mcpServers"]!["netrisk"]);
        Assert.True((bool)withAbsent["other"]!);
        Assert.NotNull(withAbsent["mcpServers"]!["netrisk"]);
    }

    [Fact]
    public void TestASymbolicLinkIsNotFollowed()
    {
        if (OperatingSystem.IsWindows()) return; // creating a link needs a privilege there; the check is the same code
        var target = Path.Combine(_machine.Root, "dotfiles", "claude.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, AgentClientSamples.ClaudeCode);
        var link = _machine.ConfigOf(AgentClientCatalog.ClaudeCode);
        File.CreateSymbolicLink(link, target);

        var status = _machine.Registrar().Status(AgentClientCatalog.ClaudeCode, _machine.Launch);
        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, _machine.Launch);

        Assert.Equal(AgentClientProblem.SymbolicLink, status.Problem);
        Assert.Equal(AgentClientProblem.SymbolicLink, plan.Problem);
        Assert.Equal(AgentClientSamples.ClaudeCode, File.ReadAllText(target));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Theory]
    [InlineData("..evil.json")]
    [InlineData("with space.json")]
    public void TestAPickedFileWhoseNameIsOutsideTheAllowlistIsRefused(string name)
    {
        var picked = Path.Combine(_machine.Root, "picked", name);
        Directory.CreateDirectory(Path.GetDirectoryName(picked)!);
        File.WriteAllText(picked, "{}");

        var plan = _machine.Registrar().Plan(AgentClientCatalog.VsCode, AgentClientAction.Register, _machine.Launch, picked);

        Assert.Equal(AgentClientProblem.UnsafeFileName, plan.Problem);
        Assert.Equal("{}", File.ReadAllText(picked));
    }

    [Fact]
    public void TestRegisteringNeedsTheBridge()
    {
        _machine.ConfigOf(AgentClientCatalog.ClaudeCode);

        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Register, launch: null);

        Assert.Equal(AgentClientProblem.BridgeNotFound, plan.Problem);
    }

    [Theory]
    [InlineData("relative/netrisk-mcp-bridge", "production", null)]
    [InlineData("/abs/netrisk-mcp-bridge\n--evil", "production", null)]
    [InlineData("/abs/netrisk-mcp-bridge", "prod uction", null)]
    [InlineData("/abs/netrisk-mcp-bridge", "production", "relative/dotnet")]
    public void TestTheLaunchAdmitsOnlyAnAbsoluteBridgeAKnownEnvironmentAndDotnetRoot(string bridge, string environment,
        string? dotnetRoot)
    {
        var rooted = OperatingSystem.IsWindows() && bridge.StartsWith('/') ? "C:" + bridge : bridge;

        Assert.Throws<ArgumentException>(() => AgentBridgeLaunch.Create(rooted, environment, dotnetRoot));
    }

    private static int CommonPrefix(byte[] a, byte[] b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return i;
    }

    private static int CommonSuffix(byte[] a, byte[] b, int prefix)
    {
        var i = 0;
        while (i < a.Length - prefix && i < b.Length - prefix && a[^(i + 1)] == b[^(i + 1)]) i++;
        return i;
    }
}
