using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;

namespace NetRisk.McpBridge;

/// <summary>
/// <c>netrisk-mcp-bridge [--environment &lt;name&gt;] [--discovery &lt;path&gt;]</c> — the command an MCP client that only
/// speaks stdio launches to reach the NetRisk desktop client (T345, S55).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var environment = "production";
        string? discoveryPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--environment" when i + 1 < args.Length:
                    environment = args[++i];
                    break;
                case "--discovery" when i + 1 < args.Length:
                    discoveryPath = args[++i];
                    break;
                case "-h" or "--help":
                    await Console.Error.WriteLineAsync(
                        "netrisk-mcp-bridge [--environment <name>] [--discovery <path>]\n" +
                        "Connects an MCP client over stdio to the NetRisk desktop client running on this computer.");
                    return 0;
                default:
                    if (args[i].StartsWith("--environment=", StringComparison.Ordinal))
                    {
                        environment = args[i]["--environment=".Length..];
                        break;
                    }
                    await Console.Error.WriteLineAsync("netrisk-mcp-bridge: unknown argument " + args[i]);
                    return 2;
            }
        }

        try
        {
            discoveryPath ??= AgentDiscoveryFile.DefaultPath(environment);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync("netrisk-mcp-bridge: " + ex.Message);
            return 2;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        await using var output = Console.OpenStandardOutput();
        using var bridge = StdioBridge.ForDiscoveryFile(discoveryPath, input, output, Console.Error);
        return await bridge.RunAsync(stop.Token);
    }
}
