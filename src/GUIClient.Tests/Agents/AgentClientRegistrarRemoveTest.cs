using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T364, T366 (S55 "Remove"): Unregister deletes only the <c>netrisk</c> entry — wherever it sits among the other
/// servers — keeps the servers object, writes a <c>.bak</c>, writes nothing when there is nothing to remove, and leaves a
/// file that does not parse untouched.
/// </summary>
[TestSubject(typeof(AgentClientRegistrar))]
public class AgentClientRegistrarRemoveTest : IDisposable
{
    private const string Entry = "{ \"type\": \"stdio\", \"command\": \"/old/netrisk-mcp-bridge\", \"args\": [] }";

    private readonly FakeAgentMachine _machine = new();

    public void Dispose() => _machine.Dispose();

    [Theory]
    [InlineData("first")]
    [InlineData("middle")]
    [InlineData("last")]
    public void TestUnregisterRemovesOnlyTheNetriskEntryWhereverItIs(string position)
    {
        var servers = position switch
        {
            "first" => $"\"netrisk\": {Entry},\n    \"a\": {{ \"command\": \"a\" }},\n    \"b\": {{ \"command\": \"b\" }}",
            "middle" => $"\"a\": {{ \"command\": \"a\" }},\n    \"netrisk\": {Entry},\n    \"b\": {{ \"command\": \"b\" }}",
            _ => $"\"a\": {{ \"command\": \"a\" }},\n    \"b\": {{ \"command\": \"b\" }},\n    \"netrisk\": {Entry}"
        };
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode,
            "{\n  \"theme\": \"dark\",\n  \"mcpServers\": {\n    " + servers + "\n  },\n  \"userID\": \"abc\"\n}\n");
        var before = FakeAgentMachine.Parse(path);

        var result = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Unregister);

        Assert.True(result.Written);
        var after = FakeAgentMachine.Parse(path);
        Assert.Equal(["a", "b"], after["mcpServers"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.True(JsonNode.DeepEquals(before["mcpServers"]!["a"], after["mcpServers"]!["a"]));
        Assert.True(JsonNode.DeepEquals(before["mcpServers"]!["b"], after["mcpServers"]!["b"]));
        Assert.Equal("dark", (string)after["theme"]!);
        Assert.Equal("abc", (string)after["userID"]!);
        Assert.DoesNotContain("netrisk", File.ReadAllText(path));
    }

    [Fact]
    public void TestUnregisterKeepsTheServersObjectEvenWhenItIsLeftEmpty()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeDesktop,
            "{\n  \"mcpServers\": {\n    \"netrisk\": " + Entry + "\n  }\n}\n");

        _machine.Run(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Unregister);

        var root = FakeAgentMachine.Parse(path);
        Assert.Empty(root["mcpServers"]!.AsObject());
    }

    [Fact]
    public void TestRegisterThenUnregisterRestoresTheOriginalBytes()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);

        _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Register);
        var result = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Unregister);

        Assert.True(result.Written);
        Assert.Equal(AgentClientSamples.ClaudeCode, File.ReadAllText(path));
        Assert.Contains("netrisk", File.ReadAllText(result.BackupPath!));
        Assert.Equal(AgentClientState.NotRegistered,
            _machine.Registrar().Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);
    }

    [Fact]
    public void TestUnregisterWhenNothingIsRegisteredWritesNothing()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, AgentClientSamples.ClaudeCode);

        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Unregister, null);
        var result = _machine.Registrar().Apply(plan);

        Assert.Equal(AgentClientProblem.None, plan.Problem);
        Assert.False(plan.HasChanges);
        Assert.False(result.Written);
        Assert.Equal(AgentClientSamples.ClaudeCode, File.ReadAllText(path));
        Assert.Empty(FakeAgentMachine.Siblings(path).Where(n => n.Contains("netrisk", StringComparison.Ordinal)));
    }

    [Fact]
    public void TestUnregisterWithoutAFileWritesNothing()
    {
        var path = _machine.ConfigOf(AgentClientCatalog.ClaudeDesktop);

        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeDesktop, AgentClientAction.Unregister, null);

        Assert.False(plan.HasChanges);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TestUnregisterLeavesAMalformedFileUntouched()
    {
        const string broken = "{ \"mcpServers\": { \"netrisk\": " + Entry + ", }";
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode, broken);

        var plan = _machine.Registrar().Plan(AgentClientCatalog.ClaudeCode, AgentClientAction.Unregister, null);
        var result = _machine.Registrar().Apply(plan);

        Assert.Equal(AgentClientProblem.Unparseable, plan.Problem);
        Assert.False(result.Written);
        Assert.Equal(broken, File.ReadAllText(path));
    }

    [Fact]
    public void TestUnregisterRemovesAStaleEntryAndNeedsNoBridge()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.ClaudeCode,
            "{\n  \"mcpServers\": {\n    \"netrisk\": " + Entry + ",\n    \"a\": { \"command\": \"a\" }\n  }\n}\n");
        Assert.Equal(AgentClientState.Stale, _machine.Registrar().Status(AgentClientCatalog.ClaudeCode, _machine.Launch).State);

        var result = _machine.Run(AgentClientCatalog.ClaudeCode, AgentClientAction.Unregister, launch: null);

        Assert.True(result.Written);
        Assert.Equal(["a"], FakeAgentMachine.Parse(path)["mcpServers"]!.AsObject().Select(p => p.Key).ToArray());
    }

    [Fact]
    public void TestUnregisterInAVsCodeFileKeepsCommentsAndTrailingCommas()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.VsCode, AgentClientSamples.VsCodeWithComments);
        _machine.Run(AgentClientCatalog.VsCode, AgentClientAction.Register);

        _machine.Run(AgentClientCatalog.VsCode, AgentClientAction.Unregister);

        Assert.Equal(AgentClientSamples.VsCodeWithComments, File.ReadAllText(path));
    }

    [Fact]
    public void TestUnregisterOfTheLastMemberAfterACommentKeepsTheComment()
    {
        var content = "{\n    \"servers\": {\n        \"a\": { \"command\": \"a\" }, // keep me\n        \"netrisk\": "
                      + Entry + "\n    }\n}\n";
        var path = _machine.WriteConfig(AgentClientCatalog.VsCode, content);

        _machine.Run(AgentClientCatalog.VsCode, AgentClientAction.Unregister);

        var text = File.ReadAllText(path);
        Assert.Contains("// keep me", text);
        Assert.Equal(["a"], FakeAgentMachine.Parse(path, jsonc: true)["servers"]!.AsObject().Select(p => p.Key).ToArray());
    }

    [Fact]
    public void TestUnregisterKeepsABackup()
    {
        var path = _machine.WriteConfig(AgentClientCatalog.Copilot,
            "{\n  \"mcpServers\": {\n    \"netrisk\": " + Entry + "\n  }\n}\n");
        var before = File.ReadAllBytes(path);

        var result = _machine.Run(AgentClientCatalog.Copilot, AgentClientAction.Unregister);

        Assert.Equal(before, File.ReadAllBytes(result.BackupPath!));
        Assert.Equal(["mcp-config.json.netrisk-20261117T120000000Z.bak"], FakeAgentMachine.Siblings(path));
    }
}
