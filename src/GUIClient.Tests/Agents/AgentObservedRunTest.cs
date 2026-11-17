using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T360 (S55) observation harness: the official MCP Python SDK (<c>scripts/agents/observe_mcp_client.py</c>) talks through
/// the real <c>netrisk-mcp-bridge</c> to the real loopback server and runtime. Stand-ins: the approval dialogs, the clock
/// and the API behind the tools (the T362 test world). Opt-in (<c>NETRISK_OBSERVE_MCP=1</c>, needs <c>uv</c> with the
/// <c>mcp</c> package cached; never downloads): <c>scripts/agents/observe-mcp.sh</c>. Spec: "Observed run" in S55.
/// </summary>
[TestSubject(typeof(AgentCoworkRuntime))]
[Trait("Category", "Observation")]
public class AgentObservedRunTest
{
    public const string McpPackage = "mcp==2.3.0";

    [Fact]
    public async Task TestARealMcpClientRegistersWorksUnderALeaseAndIsRefusedAfterExpiry()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("NETRISK_OBSERVE_MCP") == "1",
            "Observation harness (T360): set NETRISK_OBSERVE_MCP=1, or run scripts/agents/observe-mcp.sh.");

        var root = RepoRoot();
        var bridge = Path.Combine(root, "src", "McpBridge", "bin", "Debug", "net10.0",
            OperatingSystem.IsWindows() ? "netrisk-mcp-bridge.exe" : "netrisk-mcp-bridge");
        Assert.True(File.Exists(bridge), "Build the solution first: " + bridge + " is missing.");
        var script = Path.Combine(root, "scripts", "agents", "observe_mcp_client.py");
        var work = Directory.CreateTempSubdirectory("netrisk-t360-");
        var discovery = Path.Combine(work.FullName, "agents", AgentDiscoveryFile.FileName);
        var clientTranscript = Path.Combine(work.FullName, "client-transcript.txt");
        // Outside the working folder, which is deleted at the end.
        var output = Environment.GetEnvironmentVariable("NETRISK_OBSERVE_OUT") is { Length: > 0 } o
            ? o : Path.Combine(Path.GetTempPath(), "netrisk-t360-observed-run.md");

        // The real runtime and server; only the dialogs, the clock and the API behind the tools are stand-ins.
        var world = new ClassifiedWorld();
        var time = new ManualTimeProvider();
        var prompter = new ScriptedPrompter
        {
            OnRegistration = _ => Task.FromResult(true),
            OnAccess = _ => Task.FromResult(AccessDecision.Grant(1, AgentAccessMode.Read, false))
        };
        var audit = new RecordingAuditSink();
        var user = AgentReadRig.Everything();
        await using var runtime = new AgentCoworkRuntime(new InMemoryAgentRegistrationStore(), prompter, audit, time,
            new AgentCoworkOptions
            {
                Server = new McpServerOptions { ServerVersion = "observation" },
                Tools = world.Tools(),
                CurrentUser = () => user,
                Classification = world.Classification()
            });
        await runtime.StartAsync(AgentRig.UserId, discovery);

        var uv = Environment.GetEnvironmentVariable("NETRISK_OBSERVE_UV") is { Length: > 0 } u ? u : "uv";
        var start = new ProcessStartInfo(uv)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in new[] { "run", "--offline", "--no-project", "--with", McpPackage, "python", "-I", script,
                     "--bridge", bridge, "--discovery", discovery, "--transcript", clientTranscript })
            start.ArgumentList.Add(a);

        using var client = Process.Start(start)!;
        try
        {
            var stderr = new StringBuilder();
            client.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
            client.BeginErrorReadLine();

            var host = new List<string>();
            var done = false;
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (!done && await client.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith("@@CONTROL CLOCK ", StringComparison.Ordinal))
                {
                    var minutes = int.Parse(line["@@CONTROL CLOCK ".Length..]);
                    time.Advance(TimeSpan.FromMinutes(minutes));
                    runtime.Sweep();
                    host.Add($"host: clock advanced {minutes} min; active leases now: {runtime.Leases.ActiveLeases().Count()}");
                    await client.StandardInput.WriteLineAsync("@@OK");
                    await client.StandardInput.FlushAsync();
                }
                else if (line == "@@CONTROL DONE") done = true;
            }
            await client.WaitForExitAsync(timeout.Token);
            Assert.True(client.ExitCode == 0 && done, "The MCP client failed:\n" + stderr);

            var transcript = await File.ReadAllTextAsync(clientTranscript);
            var report = new StringBuilder().AppendLine("```text").Append(transcript)
                .AppendLine().AppendLine("host-side activity panel (AgentActivityLog), oldest first:");
            foreach (var e in runtime.Activity.Entries().OrderBy(e => e.At))
                report.AppendLine($"  {e.Tool,-22} {(e.IsEdit ? "edit" : "read"),-5} {e.Item?.ToString() ?? "-",-10} {e.Result}");
            report.AppendLine("host-side audit events (AgentAuditKind), in order:");
            report.AppendLine("  " + string.Join(", ", audit.Events.Select(e => e.Kind)));
            foreach (var h in host) report.AppendLine(h);
            report.AppendLine("```");
            await File.WriteAllTextAsync(output, report.ToString());
            Console.WriteLine("T360 transcript: " + output);

            // What T360 asks to see, from the client's side.
            Assert.Contains("\"status\": \"pending\"", transcript);
            Assert.Contains("\"status\": \"approved\"", transcript);
            Assert.Contains("\"status\": \"granted\"", transcript);
            Assert.Contains("edit tools listed: []", transcript);
            Assert.Contains("\"status\": \"read_only\"", transcript);
            Assert.Contains("\"status\": \"withheld\"", transcript);
            Assert.Contains("\"status\": \"access_expired\"", transcript);
            Assert.DoesNotContain(ClassifiedWorld.Marker, transcript);

            // The leak check on what the client actually received, not on the shortened transcript (review #8).
            var raw = await File.ReadAllTextAsync(clientTranscript + ".raw.json");
            Assert.Contains("\"withheld\"", raw);
            Assert.DoesNotContain(ClassifiedWorld.Marker, raw);
            Assert.Contains("<redacted-agent-key>", raw);
        }
        finally
        {
            // Never leave uv, python or the bridge running, nor the temporary folder behind (review #8).
            try
            {
                if (!client.HasExited) client.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // already gone
            }
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException)
            {
                // best effort: the runtime may still hold the discovery file for a moment
            }
        }
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
