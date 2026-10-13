using System;
using System.Collections.Generic;
using System.Linq;
using Model.Authentication;
using Model.DecisionCycle;

namespace GUIClient.Tools;

/// <summary>
/// The decision behind <c>Converters/HasPermissionConverter</c> — whether a write button guarded by a permission is
/// enabled — kept free of Avalonia so <c>GUIClient.Tests</c> covers it (T326).
///
/// It is a mirror for <em>enabling only</em>: hiding or disabling a button is not access control, and the API decides
/// every call (<c>[PermissionAuthorize]</c> on each action, e.g. <c>VulnerabilitiesController.Create</c> and
/// <c>DeleteOne</c>). What the mirror must not do is say "yes" when the server would say "no", so it follows the
/// server's rule and <b>fails closed</b>: anything it does not recognise — no user yet, an unexpected type, no
/// permission named — is "no". The previous version accepted only an <c>ObservableCollection&lt;string&gt;</c> and threw
/// on null; the client's permission list is a <c>List&lt;string&gt;</c>, the conversion failed, the binding fell back to
/// <c>IsEnabled</c>'s default of <c>true</c>, and every user got every findings button.
/// </summary>
public static class PermissionGate
{
    /// <summary>
    /// Whether the API would admit a write guarded by <c>[PermissionAuthorize(<paramref name="permission"/>)]</c> from
    /// <paramref name="subject"/>:
    /// <list type="bullet">
    /// <item>an <see cref="AuthenticatedUserInfo"/> — the <c>Admin</c> role claim (<see cref="AdminRoleClaim.IsHeldBy"/>,
    /// which <c>PermissionAuthorizationHandler</c> admits for any permission) or the permission itself;</item>
    /// <item>a bare permission list — the permission itself (a list cannot say whether its owner is an
    /// administrator);</item>
    /// </list>
    /// and in both cases never the third line, which <c>ThirdLineReadOnlyRequirement</c> refuses every write,
    /// administrators included. Permission names compare ordinally, as the server compares claim values.
    /// It checks the one method-level permission only: a class-level <c>[PermissionAuthorize]</c> the server also
    /// requires (e.g. <c>vulnerabilities</c> on <c>VulnerabilitiesController</c>) is the caller's to enforce, as
    /// <c>VulnerabilitiesViewModel</c> does by leaving the subject null for a user without it.
    /// </summary>
    public static bool Allows(object? subject, object? permission)
    {
        if (permission is not string required || string.IsNullOrWhiteSpace(required)) return false;

        return subject switch
        {
            AuthenticatedUserInfo user =>
                !IsThirdLine(user.UserPermissions) &&
                (AdminRoleClaim.IsHeldBy(user) || Holds(user.UserPermissions, required)),
            IEnumerable<string> permissions =>
                !IsThirdLine(permissions) && Holds(permissions, required),
            _ => false
        };
    }

    private static bool IsThirdLine(IEnumerable<string>? permissions) =>
        Holds(permissions, ThirdLineAssurance.PermissionKey);

    private static bool Holds(IEnumerable<string>? permissions, string permission) =>
        permissions?.Any(p => string.Equals(p, permission, StringComparison.Ordinal)) ?? false;
}
