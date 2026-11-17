using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using GUIClient.Agents;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// A throwaway machine for the M65 tests: a home folder and the other known folders under one temporary directory,
/// and a bridge executable. Nothing outside it is read or written.
/// </summary>
internal sealed class FakeAgentMachine : IDisposable
{
    public FakeAgentMachine()
    {
        Root = Path.Combine(Path.GetTempPath(), "netrisk-m65-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(Root, "home");
        Folders = new AgentKnownFolders(home, Path.Combine(Root, "roaming"), Path.Combine(Root, "local"),
            Path.Combine(home, "Library", "Application Support"), Path.Combine(home, ".config"), Path.Combine(Root, "Applications"));
        foreach (var folder in new[] { Folders.Home, Folders.RoamingAppData, Folders.LocalAppData, Folders.Applications })
            Directory.CreateDirectory(folder);

        BridgePath = Path.Combine(Root, "app", AgentBridgeLocator.ExecutableName(Platform));
        Directory.CreateDirectory(Path.GetDirectoryName(BridgePath)!);
        File.WriteAllText(BridgePath, "bridge");
        Launch = AgentBridgeLaunch.Create(BridgePath, "production", null);
    }

    public static AgentPlatform Platform => AgentPlatforms.Current ?? AgentPlatform.Linux;

    public string Root { get; }
    public AgentKnownFolders Folders { get; }
    public string BridgePath { get; }
    public AgentBridgeLaunch Launch { get; }
    public ManualTimeProvider Time { get; } = new();

    public AgentClientRegistrar Registrar(string? pathVariable = null) => new(Folders, Platform, pathVariable, Time);

    /// <summary>The client's file on this platform; its directory is created so the client counts as installed.</summary>
    public string ConfigOf(AgentClientDefinition client)
    {
        var path = Registrar().DefaultConfigPath(client)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Claude Code's directory is the home folder; its ~/.claude folder is what marks it installed.
        if (client == AgentClientCatalog.ClaudeCode) Directory.CreateDirectory(Path.Combine(Folders.Home, ".claude"));
        return path;
    }

    public string WriteConfig(AgentClientDefinition client, string content, UnixFileMode? mode = null)
    {
        var path = ConfigOf(client);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        if (mode is { } unix && !OperatingSystem.IsWindows()) File.SetUnixFileMode(path, unix);
        return path;
    }

    /// <summary>Plans and applies; asserts the plan was clean.</summary>
    public AgentClientWriteResult Run(AgentClientDefinition client, AgentClientAction action, AgentBridgeLaunch? launch = null,
        string? manualPath = null)
    {
        var registrar = Registrar();
        var plan = registrar.Plan(client, action, action == AgentClientAction.Register ? launch ?? Launch : launch, manualPath);
        Assert.Equal(AgentClientProblem.None, plan.Problem);
        return registrar.Apply(plan);
    }

    public static JsonObject Parse(string path, bool jsonc = false) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions
        {
            CommentHandling = jsonc ? System.Text.Json.JsonCommentHandling.Skip : System.Text.Json.JsonCommentHandling.Disallow,
            AllowTrailingCommas = jsonc
        })!;

    /// <summary>Every file in the client's directory other than its configuration file.</summary>
    public static string[] Siblings(string configPath) =>
        Directory.GetFiles(Path.GetDirectoryName(configPath)!)
            .Where(f => Path.GetFileName(f) != Path.GetFileName(configPath))
            .Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a temporary folder.
        }
    }
}

/// <summary>Realistic client files, each holding another server whose environment carries a token.</summary>
internal static class AgentClientSamples
{
    public const string OtherToken = "ghp_exampleTokenOfAnotherServer0123456789";

    /// <summary>Shaped like Claude Code's own file: state keys, a project with its own servers, a user-scope server.</summary>
    public const string ClaudeCode = """
        {
          "numStartups": 42,
          "theme": "dark",
          "projects": {
            "/work/repo": {
              "allowedTools": [],
              "mcpServers": {
                "local-only": { "type": "stdio", "command": "/usr/bin/true", "args": [] }
              }
            }
          },
          "mcpServers": {
            "github": {
              "type": "stdio",
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-github"],
              "env": { "GITHUB_TOKEN": "ghp_exampleTokenOfAnotherServer0123456789" }
            },
            "weather": {
              "type": "http",
              "url": "https://example.test/mcp"
            }
          },
          "userID": "abc"
        }

        """;

    public const string VsCodeWithComments = """
        {
            // Servers for Copilot
            "servers": {
                /* the team's search server */
                "search": {
                    "type": "stdio",
                    "command": "search-mcp",
                    "env": { "TOKEN": "ghp_exampleTokenOfAnotherServer0123456789" },
                },
            },
            "inputs": [],
        }

        """;
}
