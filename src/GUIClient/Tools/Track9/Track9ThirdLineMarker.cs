using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using Model.DecisionCycle;

namespace GUIClient.Tools.Track9;

/// <summary>
/// Stage 9.9 (S50 R10): how the user and role permission editors keep the third-line marker out of every bulk
/// selection — for showing and saving only; the API stores whatever it is sent.
///
/// <see cref="ThirdLineAssurance.PermissionKey"/> restricts rather than grants: <c>ThirdLineReadOnlyRequirement</c>
/// refuses every write to whoever holds it, administrators included. The "select all" button of the user editor
/// already skipped it, but its lists can be bulk-selected in other ways — a range or keyboard select-all in the
/// user editor's multiple-selection list, or several entries moved at once across the role editor's transfer list
/// — and each swept the marker up with everything else. So the marker is not in those lists at all: it is a check
/// box of its own, ticked on purpose or not at all, and these functions take it out on load and put it back on save.
/// </summary>
public static class Track9ThirdLineMarker
{
    /// <summary>Whether <paramref name="key"/> is the marker.</summary>
    public static bool IsMarker(string? key) => !ThirdLineAssurance.IsBulkGrantable(key);

    /// <summary>What a bulk-selectable permission list shows: every permission but the marker, in order.</summary>
    public static List<Permission> Selectable(IEnumerable<Permission> all) => ThirdLineAssurance.BulkGrantable(all);

    /// <summary>The marker among <paramref name="all"/>, or null on an installation that has not seeded it.</summary>
    public static Permission? Find(IEnumerable<Permission> all) => all.FirstOrDefault(p => IsMarker(p.Key));

    /// <summary>Whether a stored grant holds the marker: what its check box shows when the user or role is loaded.</summary>
    public static bool IsGranted(IEnumerable<string?>? keys) => keys?.Any(IsMarker) ?? false;

    /// <summary>
    /// The permissions a user save sends: the list's choice — never the marker by way of it — plus the marker only
    /// when its own box is ticked and the installation has it.
    /// </summary>
    public static List<Permission?> Compose(IEnumerable<Permission?> selected, Permission? marker, bool granted)
    {
        var result = selected.Where(p => p is not null && !IsMarker(p.Key)).ToList();
        if (granted && marker is not null) result.Add(marker);
        return result;
    }

    /// <summary>The permission keys a role save sends, by the same rule.</summary>
    public static List<string> Compose(IEnumerable<string> selectedKeys, Permission? marker, bool granted)
    {
        var result = selectedKeys.Where(key => !IsMarker(key)).ToList();
        if (granted && marker is not null) result.Add(marker.Key);
        return result;
    }
}
