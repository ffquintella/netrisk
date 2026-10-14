using System;
using System.Collections.Generic;
using System.Globalization;

namespace GUIClient.Tools;

/// <summary>
/// What an Integer or Boolean field of the entity form loads with and what it saves — the pure half of
/// <c>EntityFormViewModel</c>, kept free of Avalonia so <c>GUIClient.Tests</c> can compile it (T241, T242).
///
/// A <b>nullable</b> field with no stored value is <em>not set</em> (<c>null</c>) and saves nothing until the user gives
/// it a value. It used to load as the definition's default — an absent <c>criticality</c> as 0, an absent
/// <c>internetFacing</c> as false — and every save sent that back, so renaming an entity declared it criticality 0 and not
/// internet-facing: an audited change that feeds the risk index weights (M53) and that the API refuses outright to a
/// scoped <c>entity_risk_context</c> holder (S40 §6.7 item 5), who therefore could not save the entity at all.
/// A non-nullable field keeps its default, as before: it must hold a value.
/// </summary>
public static class EntityFieldValues
{
    /// <summary>
    /// The value an Integer field loads with: the stored one; else, when the field is nullable, not set; else the
    /// definition's default, else 0. A stored value that does not parse counts as absent.
    /// </summary>
    public static decimal? LoadInteger(IReadOnlyList<string> stored, bool nullable, string? defaultValue)
    {
        if (stored.Count > 0 && TryParseInteger(stored[0], out var value)) return value;
        if (nullable) return null;
        return TryParseInteger(defaultValue, out var fallback) ? fallback : 0;
    }

    /// <summary>The value a Boolean field loads with, by the same rule as <see cref="LoadInteger"/>; the fallback is false.</summary>
    public static bool? LoadBoolean(IReadOnlyList<string> stored, bool nullable, string? defaultValue)
    {
        if (stored.Count > 0 && bool.TryParse(stored[0]?.Trim(), out var value)) return value;
        if (nullable) return null;
        return bool.TryParse(defaultValue?.Trim(), out var fallback) && fallback;
    }

    /// <summary>What an Integer field saves: nothing while it is not set, else its whole value.</summary>
    public static string? SaveInteger(decimal? value) =>
        value is { } set ? Convert.ToUInt32(set).ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>What a Boolean field saves: nothing while it is not set, else <c>true</c> or <c>false</c>.</summary>
    public static string? SaveBoolean(bool? value) => value switch
    {
        null => null,
        true => "true",
        false => "false"
    };

    private static bool TryParseInteger(string? raw, out decimal value) =>
        decimal.TryParse(raw?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
