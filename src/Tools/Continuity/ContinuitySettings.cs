using System.Collections.Generic;
using System.Globalization;
using DAL.Enums;
using Model.Continuity;
using Model.Exceptions;

namespace Tools.Continuity;

/// <summary>
/// The two Stage 9.3 parameters (S43 §4.7): how long a restoration test stays valid, and how much an
/// unverified threat weighs relative to a confirmed one.
///
/// Stored as <c>settings</c> rows. Reading never fails: a missing, unparsable, out-of-range or
/// over-precise value falls back to the default and its key is reported in
/// <see cref="ContinuitySettingsDto.FallbackApplied"/>, so a damaged row can never become 0 days (every
/// test stale) or weight 0 (unverified stops counting, against the decision of 2026-10-07). Writing
/// refuses the same values instead.
///
/// The weight is a decimal in the invariant culture with at most two places: <c>"0,5"</c> is malformed
/// whatever the process culture is.
/// </summary>
public static class ContinuitySettings
{
    public const int DefaultValidityDays = 365;
    public const int MinValidityDays = 1;
    public const int MaxValidityDays = 1_095;

    public const decimal DefaultUnverifiedWeight = 0.5m;
    public const decimal MinUnverifiedWeight = 0.01m;
    public const decimal MaxUnverifiedWeight = 1.00m;

    public static ContinuitySettingsDto Defaults() => new()
    {
        RestorationTestValidityDays = DefaultValidityDays,
        UnverifiedThreatWeight = DefaultUnverifiedWeight
    };

    /// <summary>The values in force, from the stored rows (absent key = no row).</summary>
    public static ContinuitySettingsDto Parse(IReadOnlyDictionary<string, string?> rows)
    {
        var result = Defaults();

        if (rows.TryGetValue(ContinuitySettingKeys.RestorationTestValidityDays, out var daysRaw)
            && TryParseValidityDays(daysRaw, out var days))
            result.RestorationTestValidityDays = days;
        else
            result.FallbackApplied.Add(ContinuitySettingKeys.RestorationTestValidityDays);

        if (rows.TryGetValue(ContinuitySettingKeys.UnverifiedThreatWeight, out var weightRaw)
            && TryParseWeight(weightRaw, out var weight))
            result.UnverifiedThreatWeight = weight;
        else
            result.FallbackApplied.Add(ContinuitySettingKeys.UnverifiedThreatWeight);

        return result;
    }

    public static bool TryParseValidityDays(string? raw, out int days)
    {
        days = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return false;
        if (!IsValidValidityDays(parsed)) return false;

        days = parsed;
        return true;
    }

    public static bool TryParseWeight(string? raw, out decimal weight)
    {
        weight = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var parsed)) return false;
        if (!IsValidWeight(parsed)) return false;

        weight = parsed;
        return true;
    }

    public static bool IsValidValidityDays(int days) => days is >= MinValidityDays and <= MaxValidityDays;

    /// <summary>0.01–1.00 with at most two decimal places (<c>0.500</c> is 0.5 and passes; <c>0.505</c> does not).</summary>
    public static bool IsValidWeight(decimal weight) =>
        weight is >= MinUnverifiedWeight and <= MaxUnverifiedWeight && decimal.Round(weight, 2) == weight;

    /// <summary>Refuses an out-of-range write, naming the key, before anything is stored.</summary>
    public static void ValidateForWrite(int? validityDays, decimal? unverifiedWeight)
    {
        if (validityDays is not { } days || !IsValidValidityDays(days))
            throw new InvalidParameterException(ContinuitySettingKeys.RestorationTestValidityDays,
                $"The restoration-test validity must be a whole number of days from {MinValidityDays} to {MaxValidityDays}.");

        if (unverifiedWeight is not { } weight || !IsValidWeight(weight))
            throw new InvalidParameterException(ContinuitySettingKeys.UnverifiedThreatWeight,
                $"The unverified-threat weight must be from {MinUnverifiedWeight.ToString(CultureInfo.InvariantCulture)} " +
                $"to {MaxUnverifiedWeight.ToString(CultureInfo.InvariantCulture)} with at most two decimal places.");
    }

    public static string FormatValidityDays(int days) => days.ToString(CultureInfo.InvariantCulture);

    public static string FormatWeight(decimal weight) => weight.ToString("0.##", CultureInfo.InvariantCulture);
}
