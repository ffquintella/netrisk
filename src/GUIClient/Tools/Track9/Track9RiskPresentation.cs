using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.Authentication;
using Model.DecisionCycle;
using Model.RiskFlags;
using Model.TailRisk;
using Model.TreatmentEconomics;

namespace GUIClient.Tools.Track9;

/// <summary>
/// Avalonia-free presentation and validation rules used by the Stage 9.5–9.7 desktop forms.
/// Server validation remains authoritative; these rules keep a visibly invalid request from being sent.
/// </summary>
public static class Track9RiskPresentation
{
    private static readonly string[] ReviewPermissions =
        ["review_insignificant", "review_low", "review_medium", "review_high", "review_veryhigh"];

    public static bool IsThirdLine(AuthenticatedUserInfo? user) =>
        user?.UserPermissions?.Contains(ThirdLineAssurance.PermissionKey) ?? false;

    public static bool CanReadRisks(AuthenticatedUserInfo? user) =>
        user is not null && ((user.UserPermissions?.Contains("riskmanagement") ?? false)
                             || string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal));

    public static bool CanDeclareFlags(AuthenticatedUserInfo? user) => CanReadRisks(user) && !IsThirdLine(user);

    public static bool CanReadMitigations(AuthenticatedUserInfo? user) =>
        user is not null &&
        (string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal) ||
         (user.UserPermissions?.Contains("accept_mitigation") ?? false) ||
         (user.UserPermissions?.Contains("plan_mitigations") ?? false));

    public static bool CanReadAppetite(AuthenticatedUserInfo? user) =>
        user is not null &&
        (string.Equals(user.UserRole, "Admin", StringComparison.Ordinal) ||
         string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal));

    public static bool CanReview(AuthenticatedUserInfo? user) =>
        user is not null && !IsThirdLine(user) &&
        (user.UserPermissions?.Any(ReviewPermissions.Contains) ?? false);

    public static bool CanPlanMitigations(AuthenticatedUserInfo? user) =>
        user is not null && !IsThirdLine(user) &&
        (string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal)
         || (user.UserPermissions?.Contains("plan_mitigations") ?? false));

    public static bool CanSubmitRisk(AuthenticatedUserInfo? user) =>
        user is not null && !IsThirdLine(user) && (user.UserPermissions?.Contains("submit_risks") ?? false);

    public static bool CanAdministerAppetite(AuthenticatedUserInfo? user) =>
        user is not null && !IsThirdLine(user) &&
        (string.Equals(user.UserRole, "Admin", StringComparison.Ordinal) ||
         string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal));

    public static string? ReasonError(string? reason, int maxLength, string requiredKey, string tooLongKey)
    {
        var value = reason?.Trim();
        if (string.IsNullOrEmpty(value)) return requiredKey;
        return value.Length > maxLength ? tooLongKey : null;
    }

    public static string? MitigationError(MitigationEconomicsDraft draft)
    {
        if (draft.Option is null) return "Track9OptionRequired";
        if (draft.Option == TreatmentOption.TransferShare && string.IsNullOrWhiteSpace(draft.TransferCounterparty))
            return "Track9CounterpartyRequired";
        if (draft.TransferCounterparty?.Trim().Length > TreatmentEconomicsLimits.MaxCounterpartyLength)
            return "Track9CounterpartyTooLong";
        if (draft.CostBasis?.Trim().Length > TreatmentEconomicsLimits.MaxCostBasisLength)
            return "Track9CostBasisTooLong";
        if (draft.EffortPersonDays is < 0 or > TreatmentEconomicsLimits.MaxEffortPersonDays)
            return "Track9EffortOutOfRange";
        if (draft.DurationDays is < 0 or > TreatmentEconomicsLimits.MaxDurationDays)
            return "Track9DurationOutOfRange";
        if (draft.PrerequisiteMitigationIds.Count > TreatmentEconomicsLimits.MaxPrerequisites)
            return "Track9TooManyPrerequisites";
        if (draft.PrerequisiteMitigationIds.Count != draft.PrerequisiteMitigationIds.Distinct().Count())
            return "Track9DuplicatePrerequisite";

        var amounts = new[] { draft.OneTime, draft.Annual, draft.SideEffectsAnnual };
        if (amounts.Any(v => v is < 0 or > TreatmentEconomicsLimits.MaxAmount))
            return "Track9AmountOutOfRange";
        if (draft.OneTime > 0 && draft.HorizonYears is not (>= TreatmentEconomicsLimits.MinHorizonYears and <= TreatmentEconomicsLimits.MaxHorizonYears))
            return "Track9HorizonRequired";
        if (draft.HorizonYears is < TreatmentEconomicsLimits.MinHorizonYears or > TreatmentEconomicsLimits.MaxHorizonYears)
            return "Track9HorizonOutOfRange";
        return null;
    }

    public static string? RiskTargetError(decimal? score, decimal? expectedLoss, DateTimeOffset? targetDate, string? rationale)
    {
        if (score is null && expectedLoss is null) return "Track9TargetValueRequired";
        if (score is < 0 or > TreatmentEconomicsLimits.MaxTargetScore) return "Track9TargetScoreOutOfRange";
        if (expectedLoss is < 0 or > TreatmentEconomicsLimits.MaxAmount) return "Track9AmountOutOfRange";
        if (targetDate is null) return "Track9TargetDateRequired";
        return ReasonError(rationale, TreatmentEconomicsLimits.MaxRationaleLength,
            "Track9RationaleRequired", "Track9RationaleTooLong");
    }

    public static string? LossComponentsError(IReadOnlyCollection<LossComponentDraft> components)
    {
        if (components.Count is < 1 or > 7) return "Track9LossComponentsCount";
        if (components.Select(c => c.Component).Distinct().Count() != components.Count)
            return "Track9DuplicateLossComponent";

        foreach (var component in components)
        {
            if (component.Min is null || component.MostLikely is null || component.Max is null)
                return "Track9LossRangeRequired";
            if (component.Min < 0 || component.Max > TailRiskLimits.MaxLoss ||
                component.Min > component.MostLikely || component.MostLikely > component.Max)
                return "Track9LossRangeInvalid";
            if (component.Basis?.Trim().Length > TailRiskLimits.MaxBasisLength)
                return "Track9BasisTooLong";
            if (component.Component == LossComponent.Fine && string.IsNullOrWhiteSpace(component.Basis))
                return "Track9FineBasisRequired";
        }

        return null;
    }

    public static string? CorrelationError(int? riskAId, int? riskBId, decimal? coefficient, string? rationale)
    {
        if (riskAId is null || riskBId is null) return "Track9TwoRisksRequired";
        if (riskAId == riskBId) return "Track9DistinctRisksRequired";
        if (coefficient is < 0 or > 1 || coefficient is null) return "Track9CorrelationOutOfRange";
        return ReasonError(rationale, TailRiskLimits.MaxRationaleLength,
            "Track9RationaleRequired", "Track9RationaleTooLong");
    }

    public static string? TailLimitsError(IReadOnlyCollection<decimal?> limits, string? rationale)
    {
        if (limits.All(v => v is null)) return "Track9TailLimitRequired";
        if (limits.Any(v => v is < 0 or > TailRiskLimits.MaxLimit)) return "Track9AmountOutOfRange";
        return ReasonError(rationale, TailRiskLimits.MaxRationaleLength,
            "Track9RationaleRequired", "Track9RationaleTooLong");
    }

    public static string Money(double? value, string notAvailable) => value is { } number
        ? number.ToString("N2", CultureInfo.CurrentCulture)
        : notAvailable;

    public static string Money(decimal? value, string notAvailable) => value is { } number
        ? number.ToString("N2", CultureInfo.CurrentCulture)
        : notAvailable;

    public static string Percent(double? value, string notAvailable) => value is { } number
        ? number.ToString("P1", CultureInfo.CurrentCulture)
        : notAvailable;

    public static string EnumKey<T>(T value) where T : struct, Enum => $"Track9{typeof(T).Name}{value}";

    public static string EnumKey(Enum value) => $"Track9{value.GetType().Name}{value}";

    public static bool IsCurrentSelection(int loadedId, int? selectedId) => selectedId == loadedId;

    public static bool IsLatestLoad(int loadVersion, int currentVersion) => loadVersion == currentVersion;
}

public sealed record MitigationEconomicsDraft(
    TreatmentOption? Option,
    string? TransferCounterparty,
    decimal? OneTime,
    decimal? Annual,
    decimal? SideEffectsAnnual,
    int? HorizonYears,
    string? CostBasis,
    decimal? EffortPersonDays,
    int? DurationDays,
    IReadOnlyCollection<int> PrerequisiteMitigationIds);

public sealed record LossComponentDraft(
    LossComponent Component,
    double? Min,
    double? MostLikely,
    double? Max,
    string? Basis);
