namespace Tools.RiskFlags;

/// <summary>
/// The entity-schema names the flag 5 derivation reads (Stage 9.5, S46 §4.4). Code constants, like
/// <see cref="Risks.RiskChainSchema"/>: <c>RiskFlagsServiceInMemoryTest.TestRS1_FlagSchemaMatchesTheEntitiesConfiguration</c>
/// holds them against <c>EntitiesConfiguration.yaml</c>, so a rename on either side fails a test instead of
/// silently turning flag 5 off.
/// </summary>
public static class RiskFlagSchema
{
    /// <summary>The <c>organizationData</c> property that points at its classification level.</summary>
    public const string SecurityClassificationProperty = "securityClassification";

    public const string SecurityClassificationLevelDefinition = "securityClassificationLevel";

    /// <summary>The Boolean on a classification level that derives flag 5 (schema 2.6).</summary>
    public const string SensitiveProperty = "sensitive";

    /// <summary>The longest derived-basis text stored (<c>risk_flags.derived_basis</c>).</summary>
    public const int MaxBasisLength = 1000;

    /// <summary>The longest derived note stored (<c>risk_flags.derived_note</c>).</summary>
    public const int MaxNoteLength = 500;

    /// <summary>Text cut to a column's length with an ellipsis, so a long basis never fails a save.</summary>
    public static string? Truncate(string? text, int max)
    {
        if (text is null || text.Length <= max) return text;
        return text[..(max - 1)] + "…";
    }
}
