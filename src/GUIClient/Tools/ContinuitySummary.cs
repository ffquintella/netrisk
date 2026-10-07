using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.Authentication;
using Model.Continuity;
using Tools.Continuity;

namespace GUIClient.Tools;

/// <summary>The units a duration is typed in. The value is the minutes per unit.</summary>
public enum DurationUnit
{
    Minutes = 1,
    Hours = 60,
    Days = 1_440
}

/// <summary>
/// The pure half of the Stage 9.3 desktop screens (S43 §7): how a duration reads and converts, the
/// computed localization key of every status, reason, source, threat and outcome, the threat headline,
/// and the parameter form's validation. Free of Avalonia so <c>GUIClient.Tests</c> compiles it directly.
/// </summary>
public static class ContinuitySummary
{
    /// <summary>365 days, the server's ceiling for any objective or measure.</summary>
    public const int MaxMinutes = 525_600;

    public const string ProcessDefinition = "businessProcess";
    public const string ItServiceDefinition = "itService";

    /// <summary>Only business processes and IT services have a BIA.</summary>
    public static bool IsSubject(string? definitionName) =>
        definitionName is ProcessDefinition or ItServiceDefinition;

    /// <summary>
    /// <c>null</c> → <paramref name="absent"/> ("Absent", never "0" and never blank); otherwise days,
    /// hours and minutes, largest first, zero parts dropped: 90 → "1 h 30 min", 2 880 → "2 d", 0 → "0 min".
    /// </summary>
    public static string Duration(int? minutes, string absent, string daysFormat = "{0} d",
        string hoursFormat = "{0} h", string minutesFormat = "{0} min")
    {
        if (minutes is not { } total) return absent;
        if (total == 0) return string.Format(CultureInfo.CurrentCulture, minutesFormat, 0);

        var parts = new List<string>();
        var days = total / (int)DurationUnit.Days;
        var hours = total % (int)DurationUnit.Days / (int)DurationUnit.Hours;
        var rest = total % (int)DurationUnit.Hours;

        if (days > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, daysFormat, days));
        if (hours > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, hoursFormat, hours));
        if (rest > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, minutesFormat, rest));

        return string.Join(" ", parts);
    }

    /// <summary>
    /// A typed value in a unit, as minutes. Empty is valid and means "not declared". Negative, a value
    /// that is not a whole number of minutes, or one past <see cref="MaxMinutes"/> is invalid — checked
    /// in <see cref="decimal"/> before converting, so a huge value cannot overflow into a small one.
    /// </summary>
    public static bool TryToMinutes(decimal? value, DurationUnit unit, out int? minutes)
    {
        minutes = null;
        if (value is not { } typed) return true;

        var total = typed * (int)unit;
        if (total < 0 || total > MaxMinutes || decimal.Truncate(total) != total) return false;

        minutes = (int)total;
        return true;
    }

    /// <summary>The largest unit that holds <paramref name="minutes"/> exactly, for re-opening an editor.</summary>
    public static (decimal Value, DurationUnit Unit) ForEditing(int minutes)
    {
        if (minutes > 0 && minutes % (int)DurationUnit.Days == 0) return (minutes / (int)DurationUnit.Days, DurationUnit.Days);
        if (minutes > 0 && minutes % (int)DurationUnit.Hours == 0) return (minutes / (int)DurationUnit.Hours, DurationUnit.Hours);
        return (minutes, DurationUnit.Minutes);
    }

    public static string UnitKey(DurationUnit unit) => unit switch
    {
        DurationUnit.Days => "UnitDays",
        DurationUnit.Hours => "UnitHours",
        _ => "UnitMinutes"
    };

    public static string StatusKey(ObjectiveVerificationStatus status) => status switch
    {
        ObjectiveVerificationStatus.Met => "VerificationMet",
        ObjectiveVerificationStatus.NotMet => "VerificationNotMet",
        ObjectiveVerificationStatus.Unverified => "VerificationUnverified",
        _ => "VerificationAbsent"
    };

    public static string ReasonKey(VerificationReason reason) => "VerificationReason" + reason;

    public static string SourceKey(CriticalitySource? source) => source switch
    {
        CriticalitySource.Bia => "CriticalitySourceBia",
        CriticalitySource.Declared => "CriticalitySourceDeclared",
        _ => "CriticalityNotDeclared"
    };

    public static string ThreatReasonKey(ContinuityThreatReason reason) => "ThreatReason" + reason;

    public static string OutcomeKey(RestorationTestOutcome outcome) => outcome == RestorationTestOutcome.Failed
        ? "TestOutcomeFailed"
        : "TestOutcomeSucceeded";

    /// <summary>Every key this class computes — held against the three resource files by a test, because
    /// <c>LocalizationCoverageTest</c> only sees literal keys.</summary>
    public static IReadOnlyList<string> ComputedKeys { get; } =
        Enum.GetValues<DurationUnit>().Select(UnitKey)
            .Concat(Enum.GetValues<ObjectiveVerificationStatus>().Select(StatusKey))
            .Concat(Enum.GetValues<VerificationReason>().Select(ReasonKey))
            .Concat(new CriticalitySource?[] { CriticalitySource.Bia, CriticalitySource.Declared, null }.Select(SourceKey))
            .Concat(Enum.GetValues<ContinuityThreatReason>().Select(ThreatReasonKey))
            .Concat(Enum.GetValues<RestorationTestOutcome>().Select(OutcomeKey))
            .Distinct()
            .ToList();

    /// <summary>The weight as shown: two decimals in the user's culture ("0,50" in pt-BR).</summary>
    public static string Weight(decimal weight) => weight.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// "Confirmed — weight 1.00", "Unverified — weight 0.50" or "None": the class is the worst item's,
    /// so a node with any confirmed item reads confirmed.
    /// </summary>
    public static string ThreatHeadline(ContinuityThreatDto? threat, string none, string confirmedFormat,
        string unverifiedFormat)
    {
        if (threat is null || threat.ThreatWeight <= 0m) return none;

        var confirmed = threat.Items.Any(i => i.Class == ContinuityThreatClass.Confirmed);
        return string.Format(CultureInfo.CurrentCulture, confirmed ? confirmedFormat : unverifiedFormat,
            Weight(threat.ThreatWeight));
    }

    /// <summary>
    /// The key of the first problem in the parameter form, or null when both values are acceptable —
    /// the same bounds the server enforces (<see cref="ContinuitySettings"/>), which revalidates anyway.
    /// </summary>
    public static string? SettingsErrorKey(int? validityDays, decimal? unverifiedWeight)
    {
        if (validityDays is not { } days || !ContinuitySettings.IsValidValidityDays(days))
            return "RestorationTestValidityDaysHint";

        if (unverifiedWeight is not { } weight || !ContinuitySettings.IsValidWeight(weight))
            return "UnverifiedThreatWeightHint";

        return null;
    }
}

/// <summary>
/// Who sees what on the Stage 9.3 screens — for showing and enabling only; the server decides every
/// call (S43 §7).
/// </summary>
public static class ContinuityAccess
{
    private static readonly string[] ReadPermissions = ["riskmanagement", "bia_manage", "restoration_test_record"];

    /// <summary>The audience of <c>RequireContinuityRead</c>: an administrator, the <c>Administrator</c>
    /// role, or any of the three permissions.</summary>
    public static bool CanRead(AuthenticatedUserInfo? user) =>
        user is not null
        && (user.IsAdmin
            || string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal)
            || ReadPermissions.Any(p => user.UserPermissions?.Contains(p) ?? false));

    /// <summary>The audience of <c>RequireAdminOnly</c>, which guards the two parameters.</summary>
    public static bool CanEditSettings(AuthenticatedUserInfo? user) =>
        user is not null && (user.IsAdmin || string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal));
}

/// <summary>One direct dependency of the selected node, in the words the panel shows.</summary>
public sealed class ContinuityDependencyRow(BiaDependencyDto dependency, bool outgoing, string directionText)
{
    public BiaDependencyDto Dependency { get; } = dependency;

    /// <summary>True when the selected node depends on the other one; only these are removed from here.</summary>
    public bool IsOutgoing { get; } = outgoing;

    public string DirectionText { get; } = directionText;

    public string OtherName { get; } = (outgoing ? dependency.ProviderName : dependency.DependentName)
                                       ?? "#" + (outgoing ? dependency.ProviderEntityId : dependency.DependentEntityId);

    public string Description { get; } = dependency.Description ?? string.Empty;
}

/// <summary>One restoration test as the tests dialog lists it.</summary>
public sealed class RestorationTestRow(RestorationTestDto test, string outcomeText, string absent, string voidedFormat)
{
    public RestorationTestDto Test { get; } = test;

    public DateTime TestedAt { get; } = test.TestedAt.ToLocalTime();

    public string OutcomeText { get; } = outcomeText;

    public string AchievedRtoText { get; } = ContinuitySummary.Duration(test.AchievedRtoMinutes, absent);

    public string AchievedRpoText { get; } = ContinuitySummary.Duration(test.AchievedRpoMinutes, absent);

    public string DeclaredText { get; } =
        $"{ContinuitySummary.Duration(test.DeclaredRtoMinutes, absent)} / {ContinuitySummary.Duration(test.DeclaredRpoMinutes, absent)}";

    public string Evidence { get; } = test.EvidenceReference ?? string.Empty;

    public string RecordedBy { get; } = test.RecordedById is { } id ? "#" + id : string.Empty;

    public bool IsVoided { get; } = test.VoidedAt is not null;

    public string VoidedText { get; } = test.VoidedAt is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, voidedFormat, test.VoidReason);
}
