using System.Text;
using DAL.Entities;
using DAL.Enums;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// Everything Jira Cloud and Jira Server / Data Center disagree about, in one place.
///
/// There are exactly three differences, and every one of them turns into an error that names the
/// wrong cause when it is got wrong:
///
/// <list type="bullet">
/// <item>The API version. Cloud serves <c>/rest/api/3</c>; Data Center has never had it, and answers
/// a v3 path by authenticating first and then refusing the route — a <b>403</b> on a credential that
/// is perfectly valid, which sends the operator to rotate a token that was never the problem.</item>
/// <item>The body format. Cloud's v3 descriptions and comments are Atlassian Document Format and it
/// rejects a plain string; Data Center's v2 takes wiki markup and rejects the ADF object.</item>
/// <item>The credential. Cloud is basic auth with <c>email:api-token</c>. A Data Center Personal
/// Access Token must travel as <c>Authorization: Bearer</c>; used as a basic-auth password it simply
/// fails to authenticate, and after a few such attempts Seraph puts the account behind a CAPTCHA and
/// starts answering 403 instead of 401.</item>
/// </list>
///
/// Keyed on the provider kind rather than on a deployment flag because the kind is what the registry
/// resolves a provider by, so there is no way for the two to drift apart.
/// </summary>
internal static class JiraDialect
{
    internal static bool IsDataCenter(IssueTrackerProviderKind kind) =>
        kind == IssueTrackerProviderKind.JiraDataCenter;

    /// <summary>The REST root the platform API lives under, without a trailing slash.</summary>
    internal static string ApiBase(IssueTrackerProviderKind kind) =>
        IsDataCenter(kind) ? "/rest/api/2" : "/rest/api/3";

    /// <summary>Whether a description or comment body is an ADF document rather than a string.</summary>
    internal static bool UsesAdf(IssueTrackerProviderKind kind) => !IsDataCenter(kind);

    /// <summary>
    /// Whether the stored credential is a Data Center PAT (bearer) or a user + secret pair (basic).
    ///
    /// Decided by whether the connection names an authentication user, because that is the only signal
    /// the schema carries and it happens to be the honest one: a PAT belongs to nobody the caller has
    /// to name, and basic auth cannot be attempted without a user. So a Data Center connection with an
    /// empty user is a PAT, and one with a user is username + password.
    /// </summary>
    internal static bool UsesBearer(IssueTrackerConnection connection) =>
        IsDataCenter(connection.Provider) && string.IsNullOrWhiteSpace(connection.AuthUser);

    internal static string AuthHeader(IssueTrackerConnection connection, string? token) =>
        UsesBearer(connection)
            ? "Bearer " + token
            : "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{connection.AuthUser}:{token}"));
}
