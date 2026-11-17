using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GUIClient.Agents;

/// <summary>Where the running GUI's MCP endpoint is, and the per-launch token every request must carry.</summary>
public sealed record AgentDiscoveryInfo(string Url, string LaunchToken, int ProcessId, DateTimeOffset StartedAt)
{
    public override string ToString() => $"AgentDiscoveryInfo {{ Url = {Url}, ProcessId = {ProcessId} }}";
}

/// <summary>
/// The rendezvous between the GUI and local MCP clients (T344, T345): a per-user JSON file readable only by its owner
/// (0600 in a 0700 directory, or a protected user-only ACL on Windows). It holds the loopback URL and a per-launch
/// token — no agent key — and is deleted when the server stops. Shared verbatim with the stdio bridge, so it depends
/// on nothing but the BCL.
/// </summary>
public static class AgentDiscoveryFile
{
    public const string FileName = "mcp-discovery.json";
    public const int FormatVersion = 1;
    public const int MaxFileBytes = 4096;

    private static readonly Regex EnvironmentName = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);

    /// <summary>The directory beside the client's other per-user data (mirrors <c>EnvironmentService</c>).</summary>
    public static string DefaultDirectory(string environment, string? applicationData = null)
    {
        if (!EnvironmentName.IsMatch(environment))
            throw new ArgumentException("Invalid environment name.", nameof(environment));
        var appData = applicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = environment == "production"
            ? Path.Combine(appData, "NRGUIClient")
            : Path.Combine(appData, environment, "NRGUIClient");
        return Path.Combine(root, "agents");
    }

    public static string DefaultPath(string environment, string? applicationData = null) =>
        Path.Combine(DefaultDirectory(environment, applicationData), FileName);

    public static string Serialize(AgentDiscoveryInfo info) => new JsonObject
    {
        ["version"] = FormatVersion,
        ["transport"] = "streamable-http",
        ["url"] = info.Url,
        ["launch_token"] = info.LaunchToken,
        ["pid"] = info.ProcessId,
        ["started_at"] = info.StartedAt.UtcDateTime.ToString("O")
    }.ToJsonString();

    /// <summary>Parses and validates a discovery document. The URL must be <c>http://127.0.0.1:port/mcp</c>.</summary>
    public static AgentDiscoveryInfo? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return Fail("not a JSON object", out error);
            if (root["version"]?.GetValueKind() != JsonValueKind.Number || root["version"]!.GetValue<int>() != FormatVersion)
                return Fail("unsupported format version", out error);

            var url = root["url"]?.GetValueKind() == JsonValueKind.String ? root["url"]!.GetValue<string>() : null;
            var token = root["launch_token"]?.GetValueKind() == JsonValueKind.String
                ? root["launch_token"]!.GetValue<string>()
                : null;
            var pid = root["pid"]?.GetValueKind() == JsonValueKind.Number ? root["pid"]!.GetValue<int>() : 0;
            var started = root["started_at"]?.GetValueKind() == JsonValueKind.String
                          && DateTimeOffset.TryParse(root["started_at"]!.GetValue<string>(),
                              System.Globalization.CultureInfo.InvariantCulture,
                              System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : DateTimeOffset.MinValue;

            if (!IsLoopbackEndpoint(url)) return Fail("the URL is not a loopback MCP endpoint", out error);
            if (string.IsNullOrEmpty(token) || token.Length > 128 || !Regex.IsMatch(token, "^[A-Za-z0-9_-]+$"))
                return Fail("missing or malformed launch token", out error);
            if (pid <= 0) return Fail("missing process id", out error);

            return new AgentDiscoveryInfo(url!, token, pid, started);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return Fail("not valid JSON", out error);
        }
    }

    /// <summary>
    /// Reads the file a GUI wrote, refusing one that other users could read or that names a non-loopback endpoint.
    /// Whether that GUI is still running is a separate question (<see cref="IsProcessAlive"/>).
    /// </summary>
    public static AgentDiscoveryInfo? TryRead(string path, out string? error)
    {
        error = null;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return Fail("not found", out error);
            if (file.Length > MaxFileBytes) return Fail("too large", out error);
            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode shared = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(path) & shared) != 0)
                    return Fail("the file is accessible to other users; refusing to trust it", out error);
            }
            return Parse(File.ReadAllText(path), out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("unreadable: " + ex.GetType().Name, out error);
        }
    }

    /// <summary>Writes the file atomically with owner-only permissions from the moment it exists.</summary>
    public static void Write(string path, AgentDiscoveryInfo info)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? throw new ArgumentException("No directory in path.", nameof(path));
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temporary = Path.Combine(directory, "." + FileName + "." + Environment.ProcessId + ".tmp");
        if (File.Exists(temporary)) File.Delete(temporary);

        var bytes = System.Text.Encoding.UTF8.GetBytes(Serialize(info));
        FileStream stream;
        if (OperatingSystem.IsWindows()) stream = CreateUserOnlyWindows(temporary);
        else stream = CreateUserOnlyUnix(temporary);
        using (stream)
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Deletes the file only if it is still this process's: a newer GUI may have replaced it.</summary>
    public static bool DeleteIfOwned(string path, int processId)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var current = Parse(File.ReadAllText(path), out _);
            if (current is not null && current.ProcessId != processId) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool IsLoopbackEndpoint(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Host == "127.0.0.1"
        && uri.Port is > 0 and <= 65535
        && !uri.IsDefaultPort
        && uri.AbsolutePath == "/mcp"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.UserInfo);

    [UnsupportedOSPlatform("windows")]
    private static FileStream CreateUserOnlyUnix(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.CreateNew,
        Access = FileAccess.Write,
        Share = FileShare.None,
        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
    });

    [SupportedOSPlatform("windows")]
    private static FileStream CreateUserOnlyWindows(string path)
    {
        var user = WindowsIdentity.GetCurrent().User
                   ?? throw new InvalidOperationException("No user SID for the current process.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadData,
            FileShare.None, 4096, FileOptions.None, security);
    }

    private static AgentDiscoveryInfo? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }
}
