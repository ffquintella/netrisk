using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T344/T349 (S55 "Transport and local security"): only a loopback peer, the loopback literal as Host, no foreign
/// Origin and the per-launch token get past the guard — and they are checked before the body is read. The listener
/// binds 127.0.0.1 on a per-launch port, sends no CORS headers, and the discovery file is owner-only.
/// </summary>
[TestSubject(typeof(LoopbackRequestGuard))]
public class AgentLoopbackGuardTest
{
    private const int Port = AgentRig.Port;
    private static readonly LoopbackRequestGuard Guard = new(Port, AgentRig.LaunchToken);

    [Fact]
    public void TestTheLoopbackLiteralWithItsPortIsAccepted()
    {
        Assert.Equal(LoopbackRefusal.None, Guard.Check(IPAddress.Loopback, "127.0.0.1:" + Port, null, AgentRig.LaunchToken));
        Assert.Equal(LoopbackRefusal.None,
            Guard.Check(IPAddress.Loopback, "127.0.0.1:" + Port, "http://127.0.0.1:" + Port, AgentRig.LaunchToken));
    }

    [Theory]
    [InlineData("localhost:47123")]
    [InlineData("evil.example:47123")]
    [InlineData("127.0.0.1.nip.io:47123")]
    [InlineData("127.0.0.1:47124")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]:47123")]
    [InlineData("0.0.0.0:47123")]
    public void TestANonLoopbackHostIsRefused(string host)
    {
        Assert.Equal(LoopbackRefusal.ForeignHost, Guard.Check(IPAddress.Loopback, host, null, AgentRig.LaunchToken));
    }

    [Fact]
    public void TestAMissingHostIsRefused()
    {
        Assert.Equal(LoopbackRefusal.MissingHost, Guard.Check(IPAddress.Loopback, null, null, AgentRig.LaunchToken));
        Assert.Equal(LoopbackRefusal.MissingHost, Guard.Check(IPAddress.Loopback, "", null, AgentRig.LaunchToken));
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("null")]
    [InlineData("http://localhost:47123")]
    [InlineData("https://127.0.0.1:47123")]
    [InlineData("http://127.0.0.1:47124")]
    [InlineData("")]
    public void TestAForeignOriginIsRefused(string origin)
    {
        Assert.Equal(LoopbackRefusal.ForeignOrigin,
            Guard.Check(IPAddress.Loopback, "127.0.0.1:" + Port, origin, AgentRig.LaunchToken));
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.1")]
    [InlineData("8.8.8.8")]
    public void TestANonLoopbackPeerIsRefused(string address)
    {
        Assert.Equal(LoopbackRefusal.NotLoopbackPeer,
            Guard.Check(IPAddress.Parse(address), "127.0.0.1:" + Port, null, AgentRig.LaunchToken));
        Assert.Equal(LoopbackRefusal.NotLoopbackPeer, Guard.Check(null, "127.0.0.1:" + Port, null, AgentRig.LaunchToken));
    }

    [Fact]
    public void TestThePerLaunchTokenIsRequired()
    {
        var host = "127.0.0.1:" + Port;
        Assert.Equal(LoopbackRefusal.MissingLaunchToken, Guard.Check(IPAddress.Loopback, host, null, null));
        Assert.Equal(LoopbackRefusal.BadLaunchToken, Guard.Check(IPAddress.Loopback, host, null, "guessed"));
        Assert.Equal(LoopbackRefusal.BadLaunchToken,
            Guard.Check(IPAddress.Loopback, host, null, AgentRig.LaunchToken + "x"));
    }

    [Fact]
    public async Task TestAForeignHostIsRefusedBeforeTheRequestReachesTheAgentLayer()
    {
        var rig = new AgentRig();

        var response = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(1, "register_agent", "{\"name\":\"Rebound\"}"), host: "attacker.example:" + Port));

        Assert.Equal(403, response.Status);
        Assert.DoesNotContain(rig.Audit.Events, e => e.Kind == AgentAuditKind.RegistrationRequested);
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.RequestRejected
                                               && e.Detail.Contains(nameof(LoopbackRefusal.ForeignHost)));
    }

    [Fact]
    public async Task TestAForeignOriginIsRefusedBeforeTheRequestReachesTheAgentLayer()
    {
        var rig = new AgentRig();

        var response = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(1, "register_agent", "{\"name\":\"Drive-by\"}"), origin: "https://evil.example"));

        Assert.Equal(403, response.Status);
        Assert.DoesNotContain(rig.Audit.Events, e => e.Kind == AgentAuditKind.RegistrationRequested);
    }

    [Fact]
    public async Task TestARequestWithoutTheLaunchTokenOrFromAnotherHostIsRefused()
    {
        var rig = new AgentRig();
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

        Assert.Equal(403, (await rig.SendAsync(AgentRig.Post(body, token: null))).Status);
        Assert.Equal(403, (await rig.SendAsync(AgentRig.Post(body), IPAddress.Parse("192.168.0.20"))).Status);
        Assert.Equal(200, (await rig.SendAsync(AgentRig.Post(body))).Status);
    }

    [Fact]
    public async Task TestNoCorsHeaderIsEverSent()
    {
        var rig = new AgentRig();
        var preflight = "OPTIONS /mcp HTTP/1.1\r\nHost: 127.0.0.1:" + Port + "\r\n" +
                        LoopbackRequestGuard.LaunchTokenHeader + ": " + AgentRig.LaunchToken + "\r\n" +
                        "Origin: http://127.0.0.1:" + Port + "\r\nAccess-Control-Request-Method: POST\r\n\r\n";

        var options = await rig.SendAsync(preflight);
        var post = await rig.SendAsync(AgentRig.Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}"));
        var refused = await rig.SendAsync(AgentRig.Post("{}", origin: "http://evil.example"));

        Assert.Equal(405, options.Status);
        foreach (var response in new[] { options, post, refused })
            Assert.DoesNotContain("Access-Control-", response.Head, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestInitializeOverLoopbackNegotiatesAndOpensASession()
    {
        var rig = new AgentRig();

        var response = await rig.SendAsync(AgentRig.Post(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"," +
            "\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"2.1.0\"}}}"));

        Assert.Equal(200, response.Status);
        var result = JsonNode.Parse(response.Body)!["result"]!;
        Assert.Equal("2025-06-18", (string?)result["protocolVersion"]);
        Assert.NotNull(result["capabilities"]!["tools"]);
        var session = response.Header("Mcp-Session-Id");
        Assert.False(string.IsNullOrEmpty(session));

        var registered = await rig.SendAsync(AgentRig.Post(
            AgentRig.ToolCall(2, "register_agent", "{\"name\":\"Claude Code\"}"), sessionId: session));
        Assert.Equal(200, registered.Status);
        var pending = rig.Registry.GetPrompt(
            (string)JsonNode.Parse(registered.Body)!["result"]!["structuredContent"]!["pending_id"]!)!;
        Assert.Equal("claude-code", pending.Client.Name);

        var stale = await rig.SendAsync(AgentRig.Post("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"ping\"}",
            sessionId: "unknown-session"));
        Assert.Equal(404, stale.Status);
    }

    [Theory]
    [InlineData("POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\nHost: evil.example\r\nContent-Length: 2\r\n\r\n{}", 400)]
    [InlineData("POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\nTransfer-Encoding: chunked\r\n\r\n2\r\n{}\r\n0\r\n\r\n", 501)]
    [InlineData("POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\nContent-Length: 99999999\r\n\r\n", 413)]
    [InlineData("POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\n\r\n", 411)]
    [InlineData("POST http://127.0.0.1:47123/mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\nContent-Length: 2\r\n\r\n{}", 400)]
    [InlineData("POST /mcp HTTP/1.0\r\nHost: 127.0.0.1:47123\r\nContent-Length: 2\r\n\r\n{}", 505)]
    [InlineData("POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\n Folded: header\r\nContent-Length: 2\r\n\r\n{}", 400)]
    public async Task TestMalformedOrAmbiguousRequestsAreRefused(string raw, int status)
    {
        var rig = new AgentRig();

        var response = await rig.SendAsync(raw);

        Assert.Equal(status, response.Status);
        Assert.DoesNotContain(rig.Audit.Events, e => e.Kind == AgentAuditKind.RegistrationRequested);
    }

    [Fact]
    public async Task TestAnOversizedHeaderBlockIsRefused()
    {
        var rig = new AgentRig();
        var raw = "POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:47123\r\nX-Padding: " +
                  new string('a', LoopbackHttp.MaxHeaderBytes) + "\r\n\r\n";

        Assert.Equal(431, (await rig.SendAsync(raw)).Status);
    }

    [Fact]
    public async Task TestTheListenerBindsLoopbackOnlyOnAPortChosenAtLaunch()
    {
        var rig = new AgentRig();
        await using var server = new McpLoopbackServer(
            port => new McpEndpoint(new LoopbackRequestGuard(port, AgentRig.LaunchToken), rig.Dispatcher, rig.Audit, rig.Time),
            rig.Audit, rig.Time);

        server.Start();

        Assert.Equal(IPAddress.Loopback, server.LocalEndpoint!.Address);
        Assert.True(server.Port > 0);
        Assert.Equal(server.Port, server.LocalEndpoint.Port);
    }

    [Fact]
    public void TestTheDiscoveryFileIsOwnerOnlyAndHoldsNoAgentKey()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX modes; Windows uses a protected user-only ACL.");
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "nr-agent-" + Guid.NewGuid().ToString("N"), "agents");
        var path = Path.Combine(directory, AgentDiscoveryFile.FileName);
        try
        {
            AgentDiscoveryFile.Write(path, new AgentDiscoveryInfo("http://127.0.0.1:47123/mcp", AgentRig.LaunchToken,
                Environment.ProcessId, DateTimeOffset.UtcNow));

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(directory));
            var text = File.ReadAllText(path);
            Assert.DoesNotContain(AgentKeyIssuer.Prefix, text, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));

            var read = AgentDiscoveryFile.TryRead(path, out var error);
            Assert.Null(error);
            Assert.Equal("http://127.0.0.1:47123/mcp", read!.Url);
            Assert.Equal(AgentRig.LaunchToken, read.LaunchToken);
            Assert.DoesNotContain(AgentRig.LaunchToken, read.ToString(), StringComparison.Ordinal);

            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            Assert.Null(AgentDiscoveryFile.TryRead(path, out error));
            Assert.Contains("other users", error);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("http://evil.example:47123/mcp")]
    [InlineData("http://localhost:47123/mcp")]
    [InlineData("https://127.0.0.1:47123/mcp")]
    [InlineData("http://127.0.0.1:47123/other")]
    [InlineData("http://127.0.0.1/mcp")]
    [InlineData("http://user@127.0.0.1:47123/mcp")]
    public void TestTheDiscoveryReaderRefusesAnythingButTheLoopbackEndpoint(string url)
    {
        var json = AgentDiscoveryFile.Serialize(new AgentDiscoveryInfo(url, AgentRig.LaunchToken, 42, DateTimeOffset.UtcNow));

        Assert.Null(AgentDiscoveryFile.Parse(json, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TestTheDiscoveryFileIsRemovedOnlyByTheProcessThatWroteIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nr-agent-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, AgentDiscoveryFile.FileName);
        try
        {
            AgentDiscoveryFile.Write(path, new AgentDiscoveryInfo("http://127.0.0.1:47123/mcp", AgentRig.LaunchToken,
                Environment.ProcessId + 1, DateTimeOffset.UtcNow));

            Assert.False(AgentDiscoveryFile.DeleteIfOwned(path, Environment.ProcessId));
            Assert.True(File.Exists(path));
            Assert.True(AgentDiscoveryFile.DeleteIfOwned(path, Environment.ProcessId + 1));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TestTheBridgeFindsTheFileWhereTheClientWritesIt()
    {
        var appData = Path.Combine(Path.GetTempPath(), "appdata");

        Assert.Equal(Path.Combine(appData, "NRGUIClient", "agents", AgentDiscoveryFile.FileName),
            AgentDiscoveryFile.DefaultPath("production", appData));
        Assert.Equal(Path.Combine(appData, "dev", "NRGUIClient", "agents", AgentDiscoveryFile.FileName),
            AgentDiscoveryFile.DefaultPath("dev", appData));
        Assert.Throws<ArgumentException>(() => AgentDiscoveryFile.DefaultPath("../../etc", appData));
    }
}
