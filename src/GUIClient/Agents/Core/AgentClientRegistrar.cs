using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Tools.Security;

namespace GUIClient.Agents;

/// <summary>What the "Connect an agent" window shows for a client (T363).</summary>
public enum AgentClientState
{
    /// <summary>None of the client's probes found it.</summary>
    NotDetected,

    /// <summary>Installed; its file holds no <c>netrisk</c> entry (or does not exist yet).</summary>
    NotRegistered,

    /// <summary>The entry launches this bridge, with this environment.</summary>
    Registered,

    /// <summary>An entry exists but launches another path or with other settings (the bridge moved).</summary>
    Stale,

    /// <summary>The file cannot be used; the problem says why. Nothing is written to it.</summary>
    Unreadable,

    /// <summary>Not written automatically; the window shows the steps (T365).</summary>
    Guided,

    /// <summary>The client declares no configuration location on this platform.</summary>
    Unsupported
}

public enum AgentClientProblem
{
    None,
    NotDetected,
    NoLocation,
    DirectoryMissing,
    UnsafeFileName,
    SymbolicLink,
    Unreadable,
    TooLarge,
    Unparseable,
    RootNotAnObject,
    ContainerNotAnObject,
    DuplicateContainer,
    DuplicateEntry,
    VerificationFailed,
    BridgeNotFound,
    ChangedSincePreview,
    WriteFailed,
    Guided
}

public enum AgentClientAction
{
    Register,
    Unregister
}

/// <summary>A client's detection and registration state.</summary>
public sealed record AgentClientStatus(
    AgentClientDefinition Client,
    AgentClientState State,
    AgentClientProblem Problem,
    string? ConfigPath,
    bool IsManualPath,
    AgentClientProbe? DetectedBy,
    string? DetectedAt)
{
    public bool CanRegister => Client.Support == AgentClientSupport.Automatic && Problem == AgentClientProblem.None
                               && State is AgentClientState.NotRegistered or AgentClientState.Stale;

    public bool CanUnregister => Client.Support == AgentClientSupport.Automatic
                                 && State is AgentClientState.Registered or AgentClientState.Stale;
}

/// <summary>
/// A previewed change to one client's file: the diff the user approves, and the exact bytes that will be written.
/// Only <see cref="AgentClientRegistrar.Plan"/> makes one, and <see cref="AgentClientRegistrar.Apply"/> writes only
/// what the plan holds, and only while the file is still what the preview was made from.
/// </summary>
public sealed class AgentClientPlan
{
    internal AgentClientPlan(AgentClientDefinition client, AgentClientAction action, string? configPath,
        AgentClientProblem problem, bool hasChanges, string diff, byte[]? before, byte[]? after, JsonObject? entry)
    {
        Client = client;
        Action = action;
        ConfigPath = configPath;
        Problem = problem;
        HasChanges = hasChanges;
        Diff = diff;
        Before = before;
        After = after;
        Entry = entry;
    }

    public AgentClientDefinition Client { get; }
    public AgentClientAction Action { get; }
    public string? ConfigPath { get; }
    public AgentClientProblem Problem { get; }

    /// <summary>False when the file already says what was asked (registering twice writes nothing).</summary>
    public bool HasChanges { get; }

    public string Diff { get; }

    /// <summary>Whether a <c>.bak</c> copy will be kept (the file exists today).</summary>
    public bool KeepsBackup => Before is not null;

    internal byte[]? Before { get; }
    internal byte[]? After { get; }
    internal JsonObject? Entry { get; }
}

public sealed record AgentClientWriteResult(AgentClientProblem Problem, bool Written, string? BackupPath);

/// <summary>
/// Detects the AI clients installed on this machine and writes or removes the <c>netrisk</c> stdio entry in their
/// configuration (T363, T364, S55). Paths come only from <see cref="AgentKnownFolders"/> or a file the user picked; every
/// file is resolved and every write is made through <see cref="SafePathTool.FileWithin"/> in the config directory.
/// Avalonia-free; the window is in <c>ViewModels/Agents/AgentConnectViewModel</c>.
/// </summary>
public sealed class AgentClientRegistrar
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly AgentKnownFolders _folders;
    private readonly AgentPlatform _platform;
    private readonly string? _pathVariable;
    private readonly TimeProvider _time;

    /// <param name="pathVariable">The <c>PATH</c> the executable probes look in; only its absolute entries are used.</param>
    public AgentClientRegistrar(AgentKnownFolders folders, AgentPlatform platform, string? pathVariable, TimeProvider time)
    {
        _folders = folders;
        _platform = platform;
        _pathVariable = pathVariable;
        _time = time;
    }

    public IReadOnlyList<AgentClientDefinition> Clients => AgentClientCatalog.All;

    /// <summary>Where the client's file is on this platform, from the known folders; null when it declares none.</summary>
    public string? DefaultConfigPath(AgentClientDefinition client) =>
        client.Locations.TryGetValue(_platform, out var location)
            ? Path.Combine(location.Directory.Resolve(_folders), location.FileName)
            : null;

    /// <summary>The first probe that finds the client, and what it found.</summary>
    public (bool Detected, AgentClientProbe? By, string? At) DetectInstalled(AgentClientDefinition client)
    {
        if (!client.Probes.TryGetValue(_platform, out var probes)) return (false, null, null);
        foreach (var probe in probes)
        {
            switch (probe.Kind)
            {
                case AgentProbeKind.File when probe.Path!.Resolve(_folders) is var file && File.Exists(file):
                    return (true, probe, file);
                case AgentProbeKind.Directory when probe.Path!.Resolve(_folders) is var directory && Directory.Exists(directory):
                    return (true, probe, directory);
                case AgentProbeKind.Executable when FindOnPath(probe.Executable!) is { } executable:
                    return (true, probe, executable);
            }
        }
        return (false, null, null);
    }

    /// <summary>The client's state, read from its file (<paramref name="manualPath"/>: a file the user picked instead).</summary>
    public AgentClientStatus Status(AgentClientDefinition client, AgentBridgeLaunch? launch, string? manualPath = null)
    {
        var (detected, by, at) = manualPath is null ? DetectInstalled(client) : (true, (AgentClientProbe?)null, manualPath);
        var isManual = manualPath is not null;
        if (client.Support == AgentClientSupport.Guided)
            return new(client, AgentClientState.Guided, AgentClientProblem.Guided, null, false, by, at);

        var problem = Resolve(client, manualPath, out var directory, out var file);
        var configPath = file ?? (isManual ? manualPath : DefaultConfigPath(client));
        AgentClientStatus Result(AgentClientState state, AgentClientProblem why) =>
            new(client, state, why, configPath, isManual, by, at);

        if (problem == AgentClientProblem.NoLocation) return Result(AgentClientState.Unsupported, problem);
        if (problem != AgentClientProblem.None) return Result(AgentClientState.Unreadable, problem);
        if (!detected) return Result(AgentClientState.NotDetected, AgentClientProblem.NotDetected);

        var read = ReadCurrent(directory!, file!, out var content);
        if (read == AgentClientProblem.DirectoryMissing) return Result(AgentClientState.NotRegistered, read);
        if (read != AgentClientProblem.None) return Result(AgentClientState.Unreadable, read);

        var inspection = AgentClientConfigEditor.Inspect(content, client.Format!);
        if (inspection.Problem != AgentConfigProblem.None) return Result(AgentClientState.Unreadable, Map(inspection.Problem));
        if (inspection.Entry is not { } entry) return Result(AgentClientState.NotRegistered, AgentClientProblem.None);

        var current = launch is null
            ? EntryCommand(entry) is { } command && File.Exists(command)
            : Matches(entry, client.Format!.Entry(launch));
        return Result(current ? AgentClientState.Registered : AgentClientState.Stale, AgentClientProblem.None);
    }

    /// <summary>
    /// Prepares a change without writing anything: reads the file, merges or removes the entry, proves the merge and
    /// renders the diff. A file that cannot be used gives a plan with a problem and no change.
    /// </summary>
    public AgentClientPlan Plan(AgentClientDefinition client, AgentClientAction action, AgentBridgeLaunch? launch,
        string? manualPath = null)
    {
        AgentClientPlan Refused(AgentClientProblem problem, string? path = null) =>
            new(client, action, path, problem, false, "", null, null, null);

        if (client.Support == AgentClientSupport.Guided || client.Format is null) return Refused(AgentClientProblem.Guided);
        if (action == AgentClientAction.Register && launch is null) return Refused(AgentClientProblem.BridgeNotFound);
        if (manualPath is null && !DetectInstalled(client).Detected) return Refused(AgentClientProblem.NotDetected);

        var problem = Resolve(client, manualPath, out var directory, out var file);
        if (problem != AgentClientProblem.None) return Refused(problem);
        var read = ReadCurrent(directory!, file!, out var before);
        if (read != AgentClientProblem.None) return Refused(read, file);

        var entry = action == AgentClientAction.Register ? client.Format.Entry(launch!) : null;
        var edit = entry is not null
            ? AgentClientConfigEditor.Upsert(before, client.Format, entry)
            : AgentClientConfigEditor.Remove(before, client.Format);
        if (edit.Problem != AgentConfigProblem.None) return Refused(Map(edit.Problem), file);

        var diff = edit.Changed ? AgentClientConfigEditor.Diff(before, edit.Content, file!) : "";
        return new AgentClientPlan(client, action, file, AgentClientProblem.None, edit.Changed, diff, before,
            edit.Content, entry);
    }

    /// <summary>
    /// Writes a plan the user approved: only if the file is byte-for-byte what the preview was made from, only if the
    /// new content differs from it in the <c>netrisk</c> entry alone; a timestamped <c>.bak</c> first (same permissions),
    /// then a new file beside it renamed over the original (atomic on one volume).
    /// </summary>
    public AgentClientWriteResult Apply(AgentClientPlan plan)
    {
        if (plan.Problem != AgentClientProblem.None) return new(plan.Problem, false, null);
        if (!plan.HasChanges) return new(AgentClientProblem.None, false, null);

        string file, directory;
        try
        {
            file = SafePathTool.FileWithin(Path.GetDirectoryName(plan.ConfigPath!)!, Path.GetFileName(plan.ConfigPath!));
            directory = Path.GetDirectoryName(file)!;
        }
        catch (ArgumentException)
        {
            return new(AgentClientProblem.UnsafeFileName, false, null);
        }

        var read = ReadCurrent(directory, file, out var current);
        if (read != AgentClientProblem.None) return new(read, false, null);
        if (!SameBytes(current, plan.Before)) return new(AgentClientProblem.ChangedSincePreview, false, null);
        if (!AgentClientConfigEditor.OnlyEntryDiffers(current, plan.After, plan.Client.Format!, plan.Entry))
            return new(AgentClientProblem.VerificationFailed, false, null);

        try
        {
            UnixFileMode? mode = current is not null && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(file) : null;
            var backup = current is not null ? WriteBackup(directory, file, current, mode) : null;
            var temporary = WriteNew(directory, TemporaryName(file), plan.After!, mode ?? OwnerOnly);
            try
            {
                File.Move(temporary, file, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
            return new(AgentClientProblem.None, true, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(AgentClientProblem.WriteFailed, false, null);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private AgentClientProblem Resolve(AgentClientDefinition client, string? manualPath, out string? directory,
        out string? file)
    {
        directory = file = null;
        if (client.Support != AgentClientSupport.Automatic || client.Format is null) return AgentClientProblem.Guided;

        string folder, name;
        if (manualPath is not null)
        {
            if (!Path.IsPathFullyQualified(manualPath)) return AgentClientProblem.UnsafeFileName;
            var full = Path.GetFullPath(manualPath);
            folder = Path.GetDirectoryName(full) ?? "";
            name = Path.GetFileName(full);
        }
        else
        {
            if (!client.Locations.TryGetValue(_platform, out var location)) return AgentClientProblem.NoLocation;
            folder = location.Directory.Resolve(_folders);
            name = location.FileName;
        }

        try
        {
            file = SafePathTool.FileWithin(folder, name);
            directory = Path.GetDirectoryName(file);
            return AgentClientProblem.None;
        }
        catch (ArgumentException)
        {
            file = null;
            return AgentClientProblem.UnsafeFileName;
        }
    }

    /// <summary>
    /// The file's bytes (null when it does not exist yet). A symbolic link is refused: replacing it would turn the
    /// user's link into a plain file, and the write would land somewhere other than the path shown.
    /// </summary>
    private static AgentClientProblem ReadCurrent(string directory, string file, out byte[]? content)
    {
        content = null;
        try
        {
            if (!Directory.Exists(directory)) return AgentClientProblem.DirectoryMissing;
            var info = new FileInfo(file);
            if (info.LinkTarget is not null) return AgentClientProblem.SymbolicLink;
            if (Directory.Exists(file)) return AgentClientProblem.Unreadable;
            if (!info.Exists) return AgentClientProblem.None;
            if (info.Length > AgentClientConfigEditor.MaxFileBytes) return AgentClientProblem.TooLarge;
            content = File.ReadAllBytes(file);
            return content.Length > AgentClientConfigEditor.MaxFileBytes
                ? AgentClientProblem.TooLarge
                : AgentClientProblem.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            content = null;
            return AgentClientProblem.Unreadable;
        }
    }

    private string? WriteBackup(string directory, string file, byte[] content, UnixFileMode? mode)
    {
        var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var name = Path.GetFileName(file) + ".netrisk-" + stamp;
        for (var attempt = 1; attempt <= 9; attempt++)
        {
            var candidate = name + (attempt == 1 ? "" : "-" + attempt) + ".bak";
            if (File.Exists(SafePathTool.FileWithin(directory, candidate))) continue;
            return WriteNew(directory, candidate, content, mode ?? OwnerOnly);
        }
        throw new IOException("No free backup name.");
    }

    private static string TemporaryName(string file) =>
        Path.GetFileName(file) + ".netrisk-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)) + ".tmp";

    /// <summary>A new file (never an existing one or a planted link: <c>CreateNew</c>) with exactly this mode.</summary>
    private static string WriteNew(string directory, string name, byte[] content, UnixFileMode mode)
    {
        var path = SafePathTool.FileWithin(directory, name);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = mode;
        try
        {
            using (var stream = new FileStream(path, options))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            // The umask can only narrow UnixCreateMode; set the original's mode exactly, no wider and no narrower.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, mode);
            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private string? FindOnPath(string executable)
    {
        if (string.IsNullOrEmpty(_pathVariable)) return null;
        var names = _platform == AgentPlatform.Windows
            ? new[] { executable + ".exe", executable + ".cmd", executable + ".bat" }
            : new[] { executable };
        var separator = _platform == AgentPlatform.Windows ? ';' : ':';
        foreach (var folder in _pathVariable.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(folder)) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Whether an entry launches what NetRisk would write now: same command, arguments and environment.</summary>
    internal static bool Matches(JsonObject existing, JsonObject expected)
    {
        if (EntryCommand(existing) != EntryCommand(expected)) return false;
        if (!JsonNode.DeepEquals(existing["args"] ?? new JsonArray(), expected["args"] ?? new JsonArray())) return false;
        if (!JsonNode.DeepEquals(existing["env"] ?? new JsonObject(), expected["env"] ?? new JsonObject())) return false;
        return existing["type"] is null || Text(existing["type"]) is "stdio" or "local";
    }

    private static string? EntryCommand(JsonObject entry) => Text(entry["command"]);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool SameBytes(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is harmless; the original was not touched.
        }
    }

    private static AgentClientProblem Map(AgentConfigProblem problem) => problem switch
    {
        AgentConfigProblem.None => AgentClientProblem.None,
        AgentConfigProblem.Unparseable => AgentClientProblem.Unparseable,
        AgentConfigProblem.RootNotAnObject => AgentClientProblem.RootNotAnObject,
        AgentConfigProblem.ContainerNotAnObject => AgentClientProblem.ContainerNotAnObject,
        AgentConfigProblem.DuplicateContainer => AgentClientProblem.DuplicateContainer,
        AgentConfigProblem.DuplicateEntry => AgentClientProblem.DuplicateEntry,
        AgentConfigProblem.TooLarge => AgentClientProblem.TooLarge,
        AgentConfigProblem.VerificationFailed => AgentClientProblem.VerificationFailed,
        _ => AgentClientProblem.Unparseable
    };
}

/// <summary>The localization keys the client states, problems, names and guides resolve through.</summary>
public static class AgentClientTexts
{
    public static string StateKey(AgentClientState state) => "AgentConnectState" + state;

    public static string? ProblemKey(AgentClientProblem problem) =>
        problem == AgentClientProblem.None ? null : "AgentConnectProblem" + problem;

    /// <summary>Every computed key, for the test that each one is declared in all three resource files.</summary>
    public static IEnumerable<string> AllKeys =>
        Enum.GetValues<AgentClientState>().Select(StateKey)
            .Concat(Enum.GetValues<AgentClientProblem>().Select(ProblemKey).OfType<string>())
            .Concat(AgentClientCatalog.All.Select(c => c.NameKey))
            .Concat(AgentClientCatalog.All.Select(c => c.GuideKey).OfType<string>());
}
