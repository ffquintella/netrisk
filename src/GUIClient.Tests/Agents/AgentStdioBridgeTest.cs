using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using NetRisk.McpBridge;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T345 (S55): the stdio bridge relays newline-delimited JSON-RPC to the GUI's loopback endpoint with the launch token
/// from the discovery file, and tells the client plainly when NetRisk is not there. Runs against a real listener
/// bound to 127.0.0.1 inside this process; nothing leaves the machine.
/// </summary>
[TestSubject(typeof(StdioBridge))]
public class AgentStdioBridgeTest
{
    [Fact]
    public async Task TestTheBridgeSaysSoWhenNetRiskIsNotRunning()
    {
        var output = new MemoryStream();
        using var bridge = new StdioBridge(() => (null, "not found"),
            new StringReader("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"initialize\",\"params\":{}}\n" +
                             "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n"),
            output, TextWriter.Null);

        await bridge.RunAsync(CancellationToken.None);

        var line = Assert.Single(Lines(output));
        Assert.Equal(5, (int)line["id"]!);
        Assert.Equal(StdioBridge.NotRunningMessage, (string?)line["error"]!["message"]);
    }

    [Fact]
    public async Task TestTheBridgeRelaysToTheLoopbackServerWithTheLaunchToken()
    {
        var rig = new AgentRig();
        await using var server = StartServer(rig);
        var info = new AgentDiscoveryInfo($"http://127.0.0.1:{server.Port}/mcp", AgentRig.LaunchToken,
            Environment.ProcessId, DateTimeOffset.UtcNow);
        var output = new MemoryStream();
        using var bridge = new StdioBridge(() => (info, null), new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"," +
            "\"capabilities\":{},\"clientInfo\":{\"name\":\"bridge-test\",\"version\":\"1\"}}}\n"), output, TextWriter.Null);
        await bridge.RunAsync(CancellationToken.None);

        await bridge.ForwardAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", CancellationToken.None);
        await bridge.ForwardAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", CancellationToken.None);

        var lines = Lines(output);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2025-06-18", (string?)lines[0]["result"]!["protocolVersion"]);
        Assert.Contains(lines[1]["result"]!["tools"]!.AsArray(), t => (string?)t!["name"] == "register_agent");
    }

    [Fact]
    public async Task TestAWrongLaunchTokenIsRefusedAndReportedAsAnError()
    {
        var rig = new AgentRig();
        await using var server = StartServer(rig);
        var forged = new AgentDiscoveryInfo($"http://127.0.0.1:{server.Port}/mcp", "not-the-launch-token",
            Environment.ProcessId, DateTimeOffset.UtcNow);
        var output = new MemoryStream();
        using var bridge = new StdioBridge(() => (forged, null), new StringReader(""), output, TextWriter.Null);

        await bridge.ForwardAsync("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"ping\"}", CancellationToken.None);

        var line = Assert.Single(Lines(output));
        Assert.Equal(7, (int)line["id"]!);
        Assert.Contains("403", (string?)line["error"]!["message"]);
    }

    private static McpLoopbackServer StartServer(AgentRig rig)
    {
        var server = new McpLoopbackServer(
            port => new McpEndpoint(new LoopbackRequestGuard(port, AgentRig.LaunchToken), rig.Dispatcher, rig.Audit, rig.Time),
            rig.Audit, rig.Time);
        server.Start();
        return server;
    }

    private static JsonObject[] Lines(MemoryStream output) =>
        Encoding.UTF8.GetString(output.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonNode.Parse(l)!.AsObject())
            .ToArray();
}
