using System.Text.RegularExpressions;

namespace ConsoleClient.Configuration;

/// <summary>
/// Reads the deployment environment file (<c>/netrisk/netrisk.env</c>) as a configuration source, so
/// that <c>Database:ConnectionString</c> resolves however the console binary was launched.
///
/// Why the console client needs this and the other three hosts do not. Security finding NR-2026-025
/// moved the database credential out of <c>appsettings.json</c> into <c>/netrisk/netrisk.env</c>,
/// mode 0600, and the container entrypoints load it with <c>load_netrisk_env</c> into their own
/// environment — PID 1's. The API, the website and the background jobs are then <c>exec</c>'d by that
/// same entrypoint, so they inherit it. The console container is a keepalive
/// (<c>tail -f /dev/null</c>), so operator commands arrive by <c>docker exec</c>, which builds a
/// fresh environment from the image configuration and inherits none of those exports.
///
/// 2.17.3 answered that with <c>/usr/local/bin/netrisk-console</c>, a wrapper inside the image that
/// re-reads the file per invocation. That fixes the documented entry point and nothing else: the
/// launcher operators actually type is a script on the *host*, it is owned by the external
/// <c>ffquintella-dockerapp_netrisk</c> Puppet module rather than by this repository, and the one
/// deployed on apldc1vds0044 is dated October 2023 — it predates NR-2026-025 and runs
/// <c>docker exec … /bin/bash -c "cd /netrisk; /netrisk/ConsoleClient $1 $2 $3 $4"</c>, straight past
/// the wrapper. Every <c>netrisk-console database …</c> command on that host therefore still reached
/// <c>Configuration["Database:ConnectionString"]</c> as null, and before
/// <see cref="ServerServices.Services.DatabaseConnectionStringResolver"/> existed that surfaced as
/// "Unable to connect to any of the specified MySQL hosts" against a localhost nobody configured.
///
/// A fix that lives in a launcher can only ever cover the launchers this repository ships. Reading
/// the file here covers every path into the binary — the wrapper, a bare <c>docker exec</c>, the
/// stale host script, and whatever the next one is.
///
/// The parsing rules are the shell loader's rules, deliberately (see
/// <c>build/Docker/netrisk-console.sh</c> and the four entrypoints, held byte-identical by
/// <c>Packaging.Tests/DeploymentEnvironmentFileLoaderTest</c>): the file is a literal KEY=VALUE
/// env-file, never a shell script. The value is a connection string full of <c>;</c> — a command
/// separator — and sourcing it with <c>.</c> is what caused the 2.17.0 restart loop, so the raw
/// remainder of the line is taken verbatim with no unquoting, unescaping or trimming.
/// </summary>
public static class DeploymentEnvironmentFile
{
    /// <summary>Where Puppet writes the file inside every NetRisk container.</summary>
    public const string DefaultPath = "/netrisk/netrisk.env";

    /// <summary>
    /// Overrides <see cref="DefaultPath"/>. Exists for tests and for an operator running the binary
    /// outside a container; a deployment has no reason to set it.
    /// </summary>
    public const string PathOverrideVariable = "NETRISK_ENV_FILE";

    /// <summary>.NET's configuration separator, as written in the env file.</summary>
    private const string EnvironmentKeySeparator = "__";

    /// <summary>
    /// The separator <c>IConfiguration</c> uses, so <c>Database__ConnectionString</c> in the file
    /// binds to <c>Database:ConnectionString</c> exactly as the environment provider would.
    /// </summary>
    private const string ConfigurationKeySeparator = ":";

    /// <summary>The shell loader's key filter: <c>^[A-Za-z_][A-Za-z0-9_]*$</c>.</summary>
    private static readonly Regex ValidKey = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <inheritdoc cref="DefaultPath"/>
    public static string ResolvePath() =>
        Environment.GetEnvironmentVariable(PathOverrideVariable) is { Length: > 0 } overridden
            ? overridden
            : DefaultPath;

    /// <summary>
    /// Reads <see cref="ResolvePath"/> into configuration entries. An absent or unreadable file is
    /// not an error — the file only exists in a deployed container, and every other environment
    /// supplies the setting some other way.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> Read() => Read(ResolvePath());

    /// <inheritdoc cref="Read()"/>
    public static IReadOnlyList<KeyValuePair<string, string?>> Read(string path)
    {
        string[] lines;

        try
        {
            if (!File.Exists(path)) return [];
            lines = File.ReadAllLines(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Mode 0600 owned by the service account: another user reading it gets nothing rather
            // than a crash, and DatabaseConnectionStringMissingException then names the setting.
            return [];
        }

        return Parse(lines);
    }

    /// <summary>
    /// Applies the shell loader's rules to the file's lines: a line starting with <c>#</c> is a
    /// comment, a line with no <c>=</c> is skipped, the key is everything before the first <c>=</c>
    /// and must be a plain identifier, and the value is the rest of the line exactly as written.
    /// A later assignment wins, because <c>export</c> overwrites.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator < 0) continue;

            var key = line[..separator];
            if (!ValidKey.IsMatch(key)) continue;

            values[key.Replace(EnvironmentKeySeparator, ConfigurationKeySeparator)] =
                line[(separator + 1)..];
        }

        return [.. values];
    }
}
