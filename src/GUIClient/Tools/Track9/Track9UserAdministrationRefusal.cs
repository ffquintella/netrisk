using System;
using System.Net;
using System.Net.Http;
using Model.Authentication;
using Model.DecisionCycle;
using Model.Exceptions;

namespace GUIClient.Tools.Track9;

/// <summary>
/// Stage 9.9 (S50 R10): what the user and role editors say when the server refuses a save, and which saves they do not
/// send at all — for showing only; the API decides every call.
///
/// Two refusals reach these editors. <c>ThirdLineReadOnlyRequirement</c> answers 403 to every write of a holder of
/// <see cref="ThirdLinePermission"/>, administrators included; and the users and roles controllers answer 422
/// <see cref="ThirdLineAssurance.SelfAppointmentRule"/> to an administrator putting their own account in the third line,
/// because after that first refusal the account could not take the marker off again. Both used to surface as a bare
/// "Error saving users" — the 403 escaped the view model as a <c>RestComunicationException</c> and the app-wide handler
/// showed its message (nr-gui20261013.log) — so an administrator locked in the third line had nothing to act on.
/// </summary>
public static class Track9UserAdministrationRefusal
{
    private const string ThirdLinePermission = ThirdLineAssurance.PermissionKey;

    /// <summary>The caller is the third line: the server refuses every write of theirs, so the editors explain instead.</summary>
    public const string ThirdLineReadOnlyKey = "UsersThirdLineReadOnlyMSG";

    /// <summary>The save would put the caller's own account in the third line.</summary>
    public const string SelfAppointmentKey = "UsersThirdLineSelfAppointmentMSG";

    /// <summary>Any other 403: the caller may not make this change.</summary>
    public const string ForbiddenKey = "UsersSaveForbiddenMSG";

    /// <summary>Whether <paramref name="caller"/> holds the marker, through the role or directly.</summary>
    public static bool CallerIsReadOnly(AuthenticatedUserInfo? caller) =>
        caller?.UserPermissions?.Contains(ThirdLinePermission) == true;

    /// <summary>
    /// Whether saving user <paramref name="targetUserId"/> with the marker's own box ticked
    /// (<paramref name="grantsMarker"/>) would put <paramref name="caller"/>'s own account in the third line. Checked
    /// before anything is sent: the user is saved before the permissions, so a refusal of the second request would leave
    /// the first one applied.
    /// </summary>
    public static bool AppointsSelf(int targetUserId, AuthenticatedUserInfo? caller, bool grantsMarker) =>
        grantsMarker && caller?.UserId is { } callerId && callerId == targetUserId;

    /// <summary>
    /// The localization key for a refused save, or null when <paramref name="failure"/> is not a refusal these editors
    /// explain — the caller's own generic message applies then. Never the server's text: it is English, and the log
    /// already has it.
    /// </summary>
    public static string? MessageKey(Exception? failure, AuthenticatedUserInfo? caller)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is RuleBrokenException { RuleName: ThirdLineAssurance.SelfAppointmentRule })
                return SelfAppointmentKey;

            if (current is InvalidHttpRequestException { StatusCode: HttpStatusCode.Forbidden }
                or HttpRequestException { StatusCode: HttpStatusCode.Forbidden })
                return CallerIsReadOnly(caller) ? ThirdLineReadOnlyKey : ForbiddenKey;
        }

        return null;
    }
}
