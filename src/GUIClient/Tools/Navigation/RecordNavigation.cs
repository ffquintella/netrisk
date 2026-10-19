using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GUIClient.Models;
using GUIClient.Navigation;
using Model.Authentication;

namespace GUIClient.Tools.Navigation;

/// <summary>What <c>INavigationService.NavigateTo(NavigationTarget)</c> does with a destination.</summary>
public enum RecordNavigationDecision
{
    Open = 1,

    /// <summary>The module's button is disabled for the user: refused with a toast (defence in depth, S40 §10.3).</summary>
    NotAllowed = 2,

    /// <summary>Incidents behind face verification: only the navigation bar runs that check, so a link must not skip it.</summary>
    RequiresFaceVerification = 3
}

/// <summary>
/// The pure half of opening a record in another module (S40 §5.9.2, §10.3): whether to go, the toasts' resource keys, how
/// a module finds the record, and the findings filter of the Vulnerabilities sub-views.
/// </summary>
public static class RecordNavigation
{
    public const string NotAllowedKey = "NavModuleNotAllowedFMT";
    public const string FaceVerificationKey = "NavIncidentsRequireFaceIdMSG";
    public const string OutOfScopeKey = "NavRecordOutOfScopeMSG";

    public static RecordNavigationDecision Decide(NavigationTarget target, AuthenticatedUserInfo? user, bool faceIdRequired)
    {
        if (!ModuleAccess.CanOpen(target.View, user)) return RecordNavigationDecision.NotAllowed;
        return target.View == AvaliableViews.Incidents && faceIdRequired
            ? RecordNavigationDecision.RequiresFaceVerification
            : RecordNavigationDecision.Open;
    }

    /// <summary>
    /// The record among those loaded, else among those the service lists again (it may have been created since the list
    /// loaded). Null — out of scope or missing — when neither has it or the service refuses: the module then selects nothing.
    /// A refusal or failure goes to <paramref name="onFailure"/> (the caller's log), never swallowed silently.
    /// </summary>
    public static async Task<T?> FindAsync<T>(IEnumerable<T>? loaded, Func<T, bool> isRecord, Func<Task<IEnumerable<T>?>> reload,
        Action<Exception>? onFailure = null)
        where T : class
    {
        if (loaded?.FirstOrDefault(isRecord) is { } found) return found;
        try
        {
            return (await reload())?.FirstOrDefault(isRecord);
        }
        catch (Exception ex)
        {
            // A refusal (403/404) or a transport failure: the record is not shown, never a half-selected state.
            onFailure?.Invoke(ex);
            return null;
        }
    }

    /// <summary>
    /// Waits for the module's first load if it is still running: one that lands after a selection replaces the list and
    /// drops it. Its failure is the load's own to report (<paramref name="onFailure"/>); the record is then looked for anew.
    /// </summary>
    public static async Task AwaitLoadAsync(Task? load, Action<Exception>? onFailure = null)
    {
        if (load is null) return;
        try
        {
            await load;
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(ex);
        }
    }

    /// <summary><see cref="FindAsync{T}"/> over the records <paramref name="loaded"/> holds once the first load has ended.</summary>
    public static async Task<T?> FindAfterLoadAsync<T>(Task? load, Func<IEnumerable<T>?> loaded, Func<T, bool> isRecord,
        Func<Task<IEnumerable<T>?>> reload, Action<Exception>? onFailure = null)
        where T : class
    {
        await AwaitLoadAsync(load, onFailure);
        return await FindAsync(loaded(), isRecord, reload, onFailure);
    }

    /// <summary>The hosts list filter that asks the server for one host, within the caller's scope: <c>id==‹id›</c>.</summary>
    public static string HostFilter(int id) => "id==" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The findings filter of a Vulnerabilities sub-view: <c>host=‹id›</c> or <c>application=‹id›</c>; null otherwise.</summary>
    public static string? FindingsFilter(string? subView, int id) => subView switch
    {
        NavigationTarget.HostSubView => "host=" + id.ToString(CultureInfo.InvariantCulture),
        NavigationTarget.EntitySubView => "application=" + id.ToString(CultureInfo.InvariantCulture),
        _ => null
    };
}
