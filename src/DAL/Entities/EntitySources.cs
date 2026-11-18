namespace DAL.Entities;

/// <summary>
/// Who wrote <c>hosts.entity_id</c> (<c>hosts.entity_source</c>, M58, T282, S39 D-20), ranked like
/// <see cref="CriticalitySources"/>: manual 4 &gt; cmdb 3 &gt; rule 2 &gt; visionone 1 &gt; import / null 0. An automated
/// writer re-stamps a host only when its policy allows it and it ranks at least as high as the current owner, so a
/// person's assignment is never overwritten and two disagreeing syncs cannot flip a host every night.
/// Policy and rationale: docs/features/host-entity-assignment.md.
/// </summary>
public static class EntitySources
{
    /// <summary>A person, through the hosts API or the desktop dialog. No sync moves it.</summary>
    public const string Manual = "manual";

    /// <summary>A Jira Assets connection's target entity: the register of record for ownership.</summary>
    public const string Cmdb = "cmdb";

    /// <summary>A vendor tag → entity rule (T263): more specific than a connection default.</summary>
    public const string Rule = "rule";

    /// <summary>A Vision One connection's target entity.</summary>
    public const string VisionOne = "visionone";

    /// <summary>The importer's scope when a scan created the host. Ranks with unknown.</summary>
    public const string Import = "import";

    /// <summary>Higher wins; an unknown or null source ranks 0.</summary>
    public static int Rank(string? source) => source switch
    {
        Manual => 4,
        Cmdb => 3,
        Rule => 2,
        VisionOne => 1,
        _ => 0
    };

    /// <summary>Whether <paramref name="writer"/> may replace an entity currently owned by <paramref name="stored"/>.</summary>
    public static bool MayOverwrite(string? stored, string writer) => Rank(writer) >= Rank(stored);
}
