using System;
using Material.Icons;
using Model;

namespace GUIClient.Tools;

/// <summary>
/// The status → icon table behind <c>Converters/IntStatusToMaterialIconkindConverter</c>, kept free of Avalonia
/// so <c>GUIClient.Tests</c> can check it. <c>Vulnerability.Status</c> is a <c>ushort</c> and <c>Host.Status</c> a
/// <c>short</c>; both carry the same <see cref="IntStatus"/> numbers, so the host header (S38 §6) reuses this table.
/// </summary>
public static class StatusIconMap
{
    /// <summary>
    /// The icon for <paramref name="value"/> when it is a status number and <paramref name="targetType"/> can hold an
    /// icon kind; <c>false</c> otherwise.
    /// </summary>
    public static bool TryResolve(object? value, Type targetType, out MaterialIconKind kind)
    {
        kind = default;

        ushort? numeric = value switch
        {
            ushort u => u,
            short s and >= 0 => (ushort)s,
            int i and >= 0 and <= ushort.MaxValue => (ushort)i,
            _ => null
        };

        if (numeric is not { } status || !CanHoldIcon(targetType)) return false;

        kind = For(status);
        return true;
    }

    /// <summary>
    /// Whether a binding target can take an icon kind. <c>MaterialIcon.Kind</c> is a <c>MaterialIconKind?</c> in
    /// Material.Icons.Avalonia 3.x; testing only <c>IsAssignableTo(typeof(MaterialIconKind))</c> refused it, so no
    /// status icon was ever drawn (T325).
    /// </summary>
    public static bool CanHoldIcon(Type targetType) =>
        targetType == typeof(object) ||
        (Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(MaterialIconKind);

    /// <summary>The icon for one <see cref="IntStatus"/> number; unknown numbers get <see cref="MaterialIconKind.FlaskEmpty"/>.</summary>
    public static MaterialIconKind For(ushort status) => (IntStatus)status switch
    {
        IntStatus.New => MaterialIconKind.NewReleases,
        IntStatus.Verified => MaterialIconKind.Verified,
        IntStatus.Rejected => MaterialIconKind.Denied,
        IntStatus.AwaitingFix => MaterialIconKind.WrenchClock,
        IntStatus.Closed => MaterialIconKind.CloseOctagon,
        IntStatus.Fixed => MaterialIconKind.BugCheck,
        IntStatus.Mitigated => MaterialIconKind.VacuumCleaner,
        IntStatus.NotRelevant => MaterialIconKind.FileDiscard,
        IntStatus.Retired => MaterialIconKind.HourglassEmpty,
        IntStatus.Active => MaterialIconKind.CheckCircleOutline,
        IntStatus.Duplicated => MaterialIconKind.ContentDuplicate,
        IntStatus.Outdated => MaterialIconKind.History,
        IntStatus.Deleted => MaterialIconKind.TrashCan,
        IntStatus.Prioritized => MaterialIconKind.PriorityHigh,
        IntStatus.Processing => MaterialIconKind.CogPlay,
        IntStatus.Error => MaterialIconKind.AlertCircle,
        _ => MaterialIconKind.FlaskEmpty
    };
}
