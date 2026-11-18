namespace DAL.Entities;

/// <summary>
/// The writers of <c>hosts.criticality</c> and their precedence (M57, T275, S39 §5.1, D-10): manual, then CMDB, then
/// Vision One. Tenable ACR never writes the column; the index ranks it between CMDB and Vision One on its own.
/// </summary>
public static class CriticalitySources
{
    public const string Manual = "manual";
    public const string Cmdb = "cmdb";
    public const string VisionOne = "visionone";

    /// <summary>Higher wins; an unknown or null source ranks 0, so any known writer may replace it.</summary>
    public static int Rank(string? source) => source switch
    {
        Manual => 3,
        Cmdb => 2,
        VisionOne => 1,
        _ => 0
    };

    /// <summary>Whether <paramref name="writer"/> may write a criticality currently owned by <paramref name="stored"/>.</summary>
    public static bool MayOverwrite(string? stored, string writer) => Rank(writer) >= Rank(stored);
}
