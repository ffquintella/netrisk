using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GUIClient.Agents;

/// <summary>The desktop platforms a client's configuration paths are declared for (M65, S55).</summary>
public enum AgentPlatform
{
    Windows,
    MacOS,
    Linux
}

public static class AgentPlatforms
{
    /// <summary>The platform this process runs on, or null on one NetRisk does not ship for.</summary>
    public static AgentPlatform? Current =>
        OperatingSystem.IsWindows() ? AgentPlatform.Windows
        : OperatingSystem.IsMacOS() ? AgentPlatform.MacOS
        : OperatingSystem.IsLinux() ? AgentPlatform.Linux
        : null;
}

/// <summary>
/// The operating-system known folders every client path is built from — and nothing else. S55: "Locations come from the
/// platform's known folders, not from caller input". Environment overrides a client may honour (<c>CLAUDE_CONFIG_DIR</c>,
/// <c>COPILOT_HOME</c>) are deliberately not read: a user who set one points NetRisk at the file with "Choose file".
/// </summary>
public enum AgentKnownFolder
{
    /// <summary>The user's home (<c>SpecialFolder.UserProfile</c>).</summary>
    Home,

    /// <summary>Windows <c>%APPDATA%</c> (<c>SpecialFolder.ApplicationData</c>).</summary>
    RoamingAppData,

    /// <summary>Windows <c>%LOCALAPPDATA%</c> (<c>SpecialFolder.LocalApplicationData</c>).</summary>
    LocalAppData,

    /// <summary>macOS <c>~/Library/Application Support</c>.</summary>
    MacApplicationSupport,

    /// <summary>Linux <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> (<c>SpecialFolder.ApplicationData</c>).</summary>
    XdgConfig,

    /// <summary>macOS <c>/Applications</c>.</summary>
    Applications
}

/// <summary>Where each <see cref="AgentKnownFolder"/> is on this machine. Tests pass a fake tree.</summary>
public sealed record AgentKnownFolders(string Home, string RoamingAppData, string LocalAppData,
    string MacApplicationSupport, string XdgConfig, string Applications)
{
    public static AgentKnownFolders FromSystem()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AgentKnownFolders(home, appData,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(home, "Library", "Application Support"),
            appData,
            OperatingSystem.IsMacOS() ? "/Applications" : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    }

    public string Of(AgentKnownFolder folder) => folder switch
    {
        AgentKnownFolder.Home => Home,
        AgentKnownFolder.RoamingAppData => RoamingAppData,
        AgentKnownFolder.LocalAppData => LocalAppData,
        AgentKnownFolder.MacApplicationSupport => MacApplicationSupport,
        AgentKnownFolder.XdgConfig => XdgConfig,
        AgentKnownFolder.Applications => Applications,
        _ => throw new ArgumentOutOfRangeException(nameof(folder))
    };
}

/// <summary>A known folder plus fixed, reviewed segments under it. Never built from input.</summary>
public sealed record AgentPathSpec(AgentKnownFolder Folder, IReadOnlyList<string> Segments)
{
    public static AgentPathSpec Under(AgentKnownFolder folder, params string[] segments) => new(folder, segments);

    public string Resolve(AgentKnownFolders folders) =>
        Path.Combine(new[] { folders.Of(Folder) }.Concat(Segments).ToArray());
}

/// <summary>A client's configuration file: the directory writes are contained in, and the file name inside it.</summary>
public sealed record AgentConfigLocation(AgentPathSpec Directory, string FileName);

public enum AgentProbeKind
{
    /// <summary>A file exists at the path.</summary>
    File,

    /// <summary>A directory exists at the path.</summary>
    Directory,

    /// <summary>An executable of that name is in one of the absolute directories on <c>PATH</c>. Nothing is run.</summary>
    Executable
}

/// <summary>One way of telling a client is installed. Probes only look at the file system; none starts a process.</summary>
public sealed record AgentClientProbe(AgentProbeKind Kind, AgentPathSpec? Path, string? Executable)
{
    public static AgentClientProbe FileAt(AgentKnownFolder folder, params string[] segments) =>
        new(AgentProbeKind.File, AgentPathSpec.Under(folder, segments), null);

    public static AgentClientProbe DirectoryAt(AgentKnownFolder folder, params string[] segments) =>
        new(AgentProbeKind.Directory, AgentPathSpec.Under(folder, segments), null);

    public static AgentClientProbe OnPath(string executable) => new(AgentProbeKind.Executable, null, executable);
}

/// <summary>
/// How a client's file declares stdio MCP servers: the object the servers live in, whether the file is JSON with
/// comments, the <c>type</c> it expects, and whether it wants a <c>tools</c> list.
/// </summary>
public sealed record AgentClientFormat(string ContainerKey, bool Jsonc, string? TypeValue, bool AllTools)
{
    /// <summary>The <c>netrisk</c> entry for this format: the bridge, its arguments and environment, nothing else.</summary>
    public JsonObject Entry(AgentBridgeLaunch launch)
    {
        var entry = new JsonObject();
        if (TypeValue is not null) entry["type"] = TypeValue;
        entry["command"] = launch.Command;
        entry["args"] = new JsonArray(launch.Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
        if (launch.Env.Count > 0)
        {
            var env = new JsonObject();
            foreach (var (name, value) in launch.Env.OrderBy(e => e.Key, StringComparer.Ordinal)) env[name] = value;
            entry["env"] = env;
        }
        if (AllTools) entry["tools"] = new JsonArray("*");
        return entry;
    }
}

public enum AgentClientSupport
{
    /// <summary>NetRisk can write and remove the entry (after a preview and a click).</summary>
    Automatic,

    /// <summary>NetRisk shows the steps; it writes nothing.</summary>
    Guided
}

/// <summary>
/// One row of the client table (T365): a detector (probes) and a writer (format + location). A new client is a new row.
/// </summary>
public sealed record AgentClientDefinition(
    string Id,
    string NameKey,
    AgentClientSupport Support,
    AgentClientFormat? Format,
    IReadOnlyDictionary<AgentPlatform, AgentConfigLocation> Locations,
    IReadOnlyDictionary<AgentPlatform, IReadOnlyList<AgentClientProbe>> Probes,
    string? GuideKey = null);

/// <summary>
/// The AI clients the "Connect an agent" window knows (T363, T365, S55). Locations and formats were checked against each
/// vendor's documentation on 2026-11-17 — see docs/features/agent-mcp-server.md, "Implementation notes (M65)". They
/// change between releases; re-check before changing a row.
/// </summary>
public static class AgentClientCatalog
{
    /// <summary>The name of the entry NetRisk writes and removes. Nothing else in a client's file is touched.</summary>
    public const string EntryName = "netrisk";

    private static IReadOnlyList<AgentClientProbe> Probes(params AgentClientProbe[] probes) => probes;

    /// <summary>Claude Code: user-scope servers are the top-level <c>mcpServers</c> of <c>~/.claude.json</c>.</summary>
    public static readonly AgentClientDefinition ClaudeCode = new(
        "claude-code", "AgentConnectClientClaudeCode", AgentClientSupport.Automatic,
        new AgentClientFormat("mcpServers", Jsonc: false, TypeValue: "stdio", AllTools: false),
        AllPlatforms(new AgentConfigLocation(AgentPathSpec.Under(AgentKnownFolder.Home), ".claude.json")),
        AllPlatforms(Probes(
            AgentClientProbe.FileAt(AgentKnownFolder.Home, ".claude.json"),
            AgentClientProbe.DirectoryAt(AgentKnownFolder.Home, ".claude"),
            AgentClientProbe.OnPath("claude"))));

    /// <summary>Claude Desktop: <c>claude_desktop_config.json</c> → <c>mcpServers</c>.</summary>
    public static readonly AgentClientDefinition ClaudeDesktop = new(
        "claude-desktop", "AgentConnectClientClaudeDesktop", AgentClientSupport.Automatic,
        new AgentClientFormat("mcpServers", Jsonc: false, TypeValue: null, AllTools: false),
        new Dictionary<AgentPlatform, AgentConfigLocation>
        {
            [AgentPlatform.MacOS] = new(AgentPathSpec.Under(AgentKnownFolder.MacApplicationSupport, "Claude"),
                "claude_desktop_config.json"),
            [AgentPlatform.Windows] = new(AgentPathSpec.Under(AgentKnownFolder.RoamingAppData, "Claude"),
                "claude_desktop_config.json"),
            [AgentPlatform.Linux] = new(AgentPathSpec.Under(AgentKnownFolder.XdgConfig, "Claude"),
                "claude_desktop_config.json")
        },
        new Dictionary<AgentPlatform, IReadOnlyList<AgentClientProbe>>
        {
            [AgentPlatform.MacOS] = Probes(
                AgentClientProbe.DirectoryAt(AgentKnownFolder.MacApplicationSupport, "Claude"),
                AgentClientProbe.DirectoryAt(AgentKnownFolder.Applications, "Claude.app")),
            [AgentPlatform.Windows] = Probes(
                AgentClientProbe.DirectoryAt(AgentKnownFolder.RoamingAppData, "Claude"),
                AgentClientProbe.DirectoryAt(AgentKnownFolder.LocalAppData, "AnthropicClaude")),
            [AgentPlatform.Linux] = Probes(AgentClientProbe.DirectoryAt(AgentKnownFolder.XdgConfig, "Claude"))
        });

    /// <summary>GitHub Copilot in VS Code (the Local harness): the user profile's <c>mcp.json</c> → <c>servers</c>.</summary>
    public static readonly AgentClientDefinition VsCode = VsCodeRow("vscode", "AgentConnectClientVsCode", "Code");

    /// <summary>The same for VS Code Insiders.</summary>
    public static readonly AgentClientDefinition VsCodeInsiders =
        VsCodeRow("vscode-insiders", "AgentConnectClientVsCodeInsiders", "Code - Insiders");

    /// <summary>
    /// GitHub Copilot CLI, and the Copilot harness in VS Code, which reads the same file: <c>~/.copilot/mcp-config.json</c>
    /// → <c>mcpServers</c>, with a <c>tools</c> list.
    /// </summary>
    public static readonly AgentClientDefinition Copilot = new(
        "copilot", "AgentConnectClientCopilot", AgentClientSupport.Automatic,
        new AgentClientFormat("mcpServers", Jsonc: false, TypeValue: "stdio", AllTools: true),
        AllPlatforms(new AgentConfigLocation(AgentPathSpec.Under(AgentKnownFolder.Home, ".copilot"), "mcp-config.json")),
        AllPlatforms(Probes(
            AgentClientProbe.DirectoryAt(AgentKnownFolder.Home, ".copilot"),
            AgentClientProbe.OnPath("copilot"))));

    /// <summary>
    /// ChatGPT (T365): shown, never written. ChatGPT on the web reaches MCP servers only as remote connectors, which a
    /// loopback server deliberately is not; the desktop app runs local servers from the Codex <c>config.toml</c>, which
    /// this release does not edit (a TOML writer is a follow-up row). The guide gives the steps.
    /// </summary>
    public static readonly AgentClientDefinition ChatGpt = new(
        "chatgpt", "AgentConnectClientChatGpt", AgentClientSupport.Guided, null,
        new Dictionary<AgentPlatform, AgentConfigLocation>(),
        new Dictionary<AgentPlatform, IReadOnlyList<AgentClientProbe>>
        {
            [AgentPlatform.MacOS] = Probes(
                AgentClientProbe.DirectoryAt(AgentKnownFolder.Applications, "ChatGPT.app"),
                AgentClientProbe.DirectoryAt(AgentKnownFolder.Home, ".codex")),
            [AgentPlatform.Windows] = Probes(AgentClientProbe.DirectoryAt(AgentKnownFolder.Home, ".codex")),
            [AgentPlatform.Linux] = Probes(AgentClientProbe.DirectoryAt(AgentKnownFolder.Home, ".codex"))
        },
        GuideKey: "AgentConnectChatGptGuideMSG");

    public static readonly IReadOnlyList<AgentClientDefinition> All =
        [ClaudeCode, ClaudeDesktop, VsCode, VsCodeInsiders, Copilot, ChatGpt];

    private static AgentClientDefinition VsCodeRow(string id, string nameKey, string product) => new(
        id, nameKey, AgentClientSupport.Automatic,
        new AgentClientFormat("servers", Jsonc: true, TypeValue: "stdio", AllTools: false),
        new Dictionary<AgentPlatform, AgentConfigLocation>
        {
            [AgentPlatform.MacOS] = new(AgentPathSpec.Under(AgentKnownFolder.MacApplicationSupport, product, "User"),
                "mcp.json"),
            [AgentPlatform.Windows] = new(AgentPathSpec.Under(AgentKnownFolder.RoamingAppData, product, "User"),
                "mcp.json"),
            [AgentPlatform.Linux] = new(AgentPathSpec.Under(AgentKnownFolder.XdgConfig, product, "User"), "mcp.json")
        },
        new Dictionary<AgentPlatform, IReadOnlyList<AgentClientProbe>>
        {
            [AgentPlatform.MacOS] = Probes(AgentClientProbe.DirectoryAt(AgentKnownFolder.MacApplicationSupport, product, "User")),
            [AgentPlatform.Windows] = Probes(AgentClientProbe.DirectoryAt(AgentKnownFolder.RoamingAppData, product, "User")),
            [AgentPlatform.Linux] = Probes(AgentClientProbe.DirectoryAt(AgentKnownFolder.XdgConfig, product, "User"))
        });

    private static IReadOnlyDictionary<AgentPlatform, T> AllPlatforms<T>(T value) =>
        Enum.GetValues<AgentPlatform>().ToDictionary(p => p, _ => value);
}

/// <summary>
/// What a client starts: the stdio bridge (T345), with <c>--environment</c> outside production and, when NetRisk runs on
/// a shared .NET install, <c>DOTNET_ROOT</c> (the observed run showed clients start servers with a minimal environment).
/// Built only by <see cref="Create"/>, which admits exactly those three values — so an entry can never carry a port, the
/// launch token, an agent key or anything else read at run time (S55: "The entry holds no port, token or key").
/// </summary>
public sealed class AgentBridgeLaunch
{
    public const string DotnetRootVariable = "DOTNET_ROOT";
    private const int MaxPathLength = 1024;
    private static readonly Regex EnvironmentName = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);

    private AgentBridgeLaunch(string command, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env)
    {
        Command = command;
        Args = args;
        Env = env;
    }

    public string Command { get; }
    public IReadOnlyList<string> Args { get; }
    public IReadOnlyDictionary<string, string> Env { get; }

    /// <exception cref="ArgumentException">A path is not absolute or carries control characters, or the environment
    /// name is not one the bridge accepts.</exception>
    public static AgentBridgeLaunch Create(string bridgePath, string environment, string? dotnetRoot)
    {
        if (!IsCleanAbsolutePath(bridgePath))
            throw new ArgumentException("The bridge path must be an absolute path.", nameof(bridgePath));
        if (!EnvironmentName.IsMatch(environment))
            throw new ArgumentException("Invalid environment name.", nameof(environment));
        if (dotnetRoot is not null && !IsCleanAbsolutePath(dotnetRoot))
            throw new ArgumentException("DOTNET_ROOT must be an absolute path.", nameof(dotnetRoot));

        IReadOnlyList<string> args = environment == "production" ? [] : ["--environment", environment];
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dotnetRoot is not null) env[DotnetRootVariable] = dotnetRoot;
        return new AgentBridgeLaunch(bridgePath, args, env);
    }

    private static bool IsCleanAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Length <= MaxPathLength && Path.IsPathFullyQualified(path)
        && !path.Any(char.IsControl);
}

/// <summary>Finds the bridge executable from where NetRisk itself runs — never from input.</summary>
public static class AgentBridgeLocator
{
    public const string BaseName = "netrisk-mcp-bridge";
    private static readonly Regex BuildSegment = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);

    public static string ExecutableName(AgentPlatform platform) =>
        platform == AgentPlatform.Windows ? BaseName + ".exe" : BaseName;

    /// <summary>
    /// Beside the NetRisk executable (where a package puts it); and, for a build from source, the McpBridge project's
    /// own output next to GUIClient's (<c>…/GUIClient/bin/&lt;configuration&gt;/&lt;framework&gt;</c> →
    /// <c>…/McpBridge/bin/&lt;configuration&gt;/&lt;framework&gt;</c>).
    /// </summary>
    public static IReadOnlyList<string> Candidates(string appBaseDirectory, AgentPlatform platform)
    {
        var exe = ExecutableName(platform);
        var baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appBaseDirectory));
        var candidates = new List<string> { Path.Combine(baseDirectory, exe) };

        var framework = new DirectoryInfo(baseDirectory);
        var configuration = framework.Parent;
        var bin = configuration?.Parent;
        var project = bin?.Parent;
        if (project?.Parent is { } sourceRoot && project.Name == "GUIClient" && bin!.Name == "bin"
            && BuildSegment.IsMatch(configuration!.Name) && BuildSegment.IsMatch(framework.Name))
            candidates.Add(Path.Combine(sourceRoot.FullName, "McpBridge", "bin", configuration.Name, framework.Name, exe));

        return candidates;
    }

    public static string? Find(string appBaseDirectory, AgentPlatform platform) =>
        Candidates(appBaseDirectory, platform).FirstOrDefault(File.Exists);

    /// <summary>
    /// The .NET root a framework-dependent bridge needs, when NetRisk itself runs on a shared runtime
    /// (<c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c> with <c>&lt;root&gt;/dotnet</c> beside it); null
    /// for a self-contained NetRisk, whose runtime directory is its own.
    /// </summary>
    public static string? DotnetRootOf(string runtimeDirectory, AgentPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory)) return null;
        var version = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(runtimeDirectory)));
        var framework = version.Parent;
        var shared = framework?.Parent;
        var root = shared?.Parent;
        if (root is null || framework!.Name != "Microsoft.NETCore.App" || shared!.Name != "shared") return null;
        var host = Path.Combine(root.FullName, platform == AgentPlatform.Windows ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? root.FullName : null;
    }
}
