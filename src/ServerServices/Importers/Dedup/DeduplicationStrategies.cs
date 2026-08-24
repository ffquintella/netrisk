using System.Security.Cryptography;
using System.Text;
using Contracts.Importers;
using Tools.Security;

namespace ServerServices.Importers.Dedup;

/// <summary>
/// The scanner's own identifier for the finding — a Snyk issue id, a Dependabot alert number, a
/// SARIF result guid.
///
/// Highest precedence when present, because the tool itself promises the id is stable across runs,
/// and no fingerprint we compute can beat that promise.
/// </summary>
public class UniqueIdFromToolStrategy : IDeduplicationStrategy
{
    public const string StrategyName = "UniqueIdFromTool";

    public string Name => StrategyName;

    public string? ComputeKey(DedupContext context, DedupFieldSet fields)
    {
        var id = context.Finding.ToolUniqueId;
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Hashed rather than stored raw: tool ids range from a short integer to a long URL, and the
        // dedup_key column is one fixed 64-character shape so every strategy's output is comparable
        // and indexable the same way.
        return DedupHash.Sha256($"{context.Finding.Tool}{id}");
    }
}

/// <summary>
/// SHA-256 over a configurable, ordered field set (Track 3 milestone 3.3.1) — the default strategy.
///
/// The field set is chosen so the key survives cosmetic change: a reworded description, a drifted
/// line number inside a snippet, a re-scored CVSS. GitLab's location fingerprint is the same idea.
/// </summary>
public class HashBasedStrategy : IDeduplicationStrategy
{
    public const string StrategyName = "HashBased";

    public string Name => StrategyName;

    public string? ComputeKey(DedupContext context, DedupFieldSet fields)
    {
        var values = fields.Resolve(context).ToList();

        // Every component empty means the configuration named nothing this finding carries. A key
        // over nothing would merge every such finding into one, so decline instead and let the
        // chain fall through.
        if (values.All(string.IsNullOrEmpty)) return null;

        return DedupHash.Sha256(string.Join("", values));
    }
}

/// <summary>
/// The hash NetRisk computed for Nessus findings before Track 3 (Track 3 milestone 3.3.1).
///
/// Kept so that re-importing a <c>.nessus</c> file matches findings imported by the old code path
/// instead of duplicating the entire register once. It reproduces the previous expression exactly —
/// SHA-1 of plugin name + host id + severity + risk factor + service id, concatenated with no
/// separator — because "close enough" here means every pre-Track-3 finding duplicates.
/// </summary>
public class LegacyHashCodeStrategy : IDeduplicationStrategy
{
    public const string StrategyName = "LegacyHashCode";

    public string Name => StrategyName;

    /// <summary>
    /// The key produced here is compared against <c>vulnerabilities.import_hash</c>, which is where
    /// the old code stored it — pre-Track-3 rows have no <c>dedup_key</c> at all.
    /// </summary>
    public bool MatchesLegacyImportHash => true;

    public string? ComputeKey(DedupContext context, DedupFieldSet fields)
    {
        var f = context.Finding;

        // Without a resolved host and service the legacy string cannot be rebuilt, and a partial
        // one would hash to something that matches nothing.
        if (context.HostId == null || context.HostServiceId == null) return null;
        if (string.IsNullOrWhiteSpace(f.Title)) return null;

        f.ToolFields.TryGetValue("riskFactor", out var riskFactor);

        var legacy = f.Title + context.HostId.Value + f.RawSeverity + (riskFactor ?? string.Empty) +
                     context.HostServiceId.Value;

        return HashTool.CreateSha1(legacy);
    }
}

/// <summary>
/// Delegates to a plugin-supplied key function (Track 3 milestone 3.3.1, the <c>Custom</c> row).
///
/// One instance wraps one plugin, so a deployment with two dedup plugins gets two strategies whose
/// names come from the plugins themselves.
/// </summary>
public class PluginDeduplicationStrategy(IDeduplicationStrategyPlugin plugin) : IDeduplicationStrategy
{
    public string Name => plugin.StrategyName;

    public string? ComputeKey(DedupContext context, DedupFieldSet fields)
    {
        var key = plugin.ComputeKey(context.Finding);
        if (string.IsNullOrWhiteSpace(key)) return null;

        // Hashed on the way out so a plugin cannot overflow the column or produce a key shaped
        // differently from every other strategy's.
        return DedupHash.Sha256($"{plugin.StrategyName}{key}");
    }
}

/// <summary>
/// The one place a dedup key is turned into its stored form: lower-case hex SHA-256, 64 characters,
/// matching the <c>dedup_key varchar(64)</c> column exactly.
/// </summary>
public static class DedupHash
{
    public static string Sha256(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
