using Model.Database;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerServices.SchemaUpgrade;

/// <summary>
/// Loads and validates the Track 6 schema-upgrade phase manifest
/// (<c>SchemaUpgradePhases.yaml</c>). The manifest drives the <c>database upgrade-schema</c>
/// tool, so each new phase is added as data here rather than as new command code.
/// </summary>
public static class SchemaUpgradeManifestLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Parses a manifest from raw YAML content. Throws <see cref="SchemaUpgradeManifestException"/> on invalid input.</summary>
    public static SchemaUpgradeManifest Load(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new SchemaUpgradeManifestException("Schema upgrade manifest is empty.");

        SchemaUpgradeManifest? manifest;
        try
        {
            manifest = Deserializer.Deserialize<SchemaUpgradeManifest>(yaml);
        }
        catch (Exception ex)
        {
            throw new SchemaUpgradeManifestException($"Schema upgrade manifest could not be parsed: {ex.Message}", ex);
        }

        if (manifest is null || manifest.Phases.Count == 0)
            throw new SchemaUpgradeManifestException("Schema upgrade manifest declares no phases.");

        Validate(manifest);
        return manifest;
    }

    /// <summary>Parses a manifest from a file path.</summary>
    public static SchemaUpgradeManifest LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new SchemaUpgradeManifestException($"Schema upgrade manifest not found at '{path}'.");

        return Load(File.ReadAllText(path));
    }

    private static void Validate(SchemaUpgradeManifest manifest)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var phase in manifest.Phases)
        {
            if (string.IsNullOrWhiteSpace(phase.Phase))
                throw new SchemaUpgradeManifestException("A phase entry is missing its 'phase' identifier.");

            if (!seen.Add(phase.Phase))
                throw new SchemaUpgradeManifestException($"Duplicate phase '{phase.Phase}' in manifest.");

            if (phase.TargetVersion <= phase.StartVersion)
                throw new SchemaUpgradeManifestException(
                    $"Phase '{phase.Phase}' has targetVersion ({phase.TargetVersion}) not greater than startVersion ({phase.StartVersion}).");

            if (phase.Destructive)
            {
                if (string.IsNullOrWhiteSpace(phase.RequiresPhase))
                    throw new SchemaUpgradeManifestException(
                        $"Destructive phase '{phase.Phase}' must declare 'requiresPhase'.");
                if (phase.ObservationDays <= 0)
                    throw new SchemaUpgradeManifestException(
                        $"Destructive phase '{phase.Phase}' must declare a positive 'observationDays'.");
            }
        }

        // Cross-references resolve to declared phases.
        foreach (var phase in manifest.Phases.Where(p => p.RequiresPhase is not null))
        {
            if (manifest.GetPhase(phase.RequiresPhase!) is null)
                throw new SchemaUpgradeManifestException(
                    $"Phase '{phase.Phase}' requires phase '{phase.RequiresPhase}', which is not declared.");
        }
    }
}

/// <summary>Raised when the schema-upgrade manifest is missing, malformed, or internally inconsistent.</summary>
public class SchemaUpgradeManifestException : Exception
{
    public SchemaUpgradeManifestException(string message) : base(message) { }
    public SchemaUpgradeManifestException(string message, Exception inner) : base(message, inner) { }
}
