namespace DAL.Entities;

/// <summary>
/// Who wrote a CMDB-fed asset-context fact (M58, T291, S39 §3.2 CMDB-INET / CMDB-REL): <c>hosts.internet_facing_source</c>
/// and <c>host_application_links.source</c>. Ranked like <see cref="CriticalitySources"/>: a person's statement (manual)
/// outranks the register's (cmdb), so an import never overwrites or deletes what somebody set by hand; an unknown or null
/// writer ranks 0.
/// </summary>
public static class AssetContextSources
{
    /// <summary>A person, through the hosts API. No import moves it.</summary>
    public const string Manual = "manual";

    /// <summary>A Jira Assets import.</summary>
    public const string Cmdb = "cmdb";

    /// <summary>Higher wins; an unknown or null source ranks 0, so any known writer may replace it.</summary>
    public static int Rank(string? source) => source switch
    {
        Manual => 2,
        Cmdb => 1,
        _ => 0
    };

    /// <summary>Whether <paramref name="writer"/> may write a fact currently owned by <paramref name="stored"/>.</summary>
    public static bool MayOverwrite(string? stored, string writer) => Rank(writer) >= Rank(stored);
}
