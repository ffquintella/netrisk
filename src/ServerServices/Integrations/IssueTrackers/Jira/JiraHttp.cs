using System.Text;
using System.Text.Json;
using DAL.Entities;
using Model.Integrations;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// The bits of Jira HTTP that the Service Management client, the Assets client and the metadata client
/// all need (Track 4 milestone 4.6): basic auth, JSON reading that does not throw on a missing
/// property, and one place that turns a status code into a sentence an operator can act on.
///
/// A static helper rather than a base class: the three clients have nothing in common except this, and
/// inheritance would have made the Assets client — which talks to a *different host* — look like a
/// variation on the site client rather than the separate thing it is.
/// </summary>
internal static class JiraHttp
{
    /// <summary>
    /// Atlassian Cloud basic auth: the account email as the user, an API token as the password.
    /// Password authentication has been refused since Atlassian deprecated it, so a connection whose
    /// token is really a password gets a 401 that <see cref="Describe"/> explains.
    /// </summary>
    internal static string BasicAuth(IssueTrackerConnection connection, string? token) =>
        "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{connection.AuthUser}:{token}"));

    internal static Task<OutboundHttpResponse> SendAsync(IOutboundHttpClient http,
        IssueTrackerConnection connection, string? token, string method, string url, string? body,
        CancellationToken ct)
    {
        return http.SendAsync(new OutboundHttpRequest
        {
            Method = method,
            Url = url,
            Body = body,
            Headers =
            {
                ["Authorization"] = BasicAuth(connection, token),
                ["Accept"] = "application/json"
            }
        }, ct);
    }

    /// <summary>A path on the connection's own site.</summary>
    internal static string SiteUrl(IssueTrackerConnection connection, string path) =>
        connection.BaseUrl.TrimEnd('/') + path;

    /// <summary>
    /// The Assets root for a workspace.
    ///
    /// A different host from the site — Atlassian serves Assets from <c>api.atlassian.com</c>, keyed by
    /// a workspace id that is neither the site name nor the Jira cloud id. Building it here rather than
    /// at each call site is what keeps one of them from quietly pointing at the site and 404ing.
    /// </summary>
    internal const string AssetsHost = "https://api.atlassian.com";

    internal static string AssetsUrl(string workspaceId, string path) =>
        $"{AssetsHost}/jsm/assets/workspace/{Uri.EscapeDataString(workspaceId)}/v1{path}";

    /// <summary>
    /// Reads a response body as JSON, or null when it is not JSON at all.
    ///
    /// Null rather than a throw because Jira answers some misconfigurations with an HTML sign-in page
    /// behind a 200, and a parse exception in that case says "unexpected character" where the useful
    /// message is "that URL is not a Jira API".
    /// </summary>
    internal static JsonDocument? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            }
            : null;

    internal static int? Int(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            // Jira is inconsistent about whether an id is a number or a string, even within one
            // response, so both are accepted rather than one of them silently reading as absent.
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    internal static long? Long(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    internal static bool Bool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;

    internal static DateTime? Utc(JsonElement element, string property)
    {
        var text = Str(element, property);

        return DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                                             | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Jira's "epoch millis in a nested object" temporal shape, which the Service Desk API uses for
    /// SLA cycle boundaries: <c>{"iso8601":"…","epochMillis":1712345678901}</c>. The ISO form is
    /// preferred and the epoch is the fallback, because older instances send only one of the two.
    /// </summary>
    internal static DateTime? JsmDate(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(property, out var node)) return null;

        if (node.ValueKind == JsonValueKind.String)
            return Utc(parent, property);

        if (node.ValueKind != JsonValueKind.Object) return null;

        if (Utc(node, "iso8601") is { } iso) return iso;

        return Long(node, "epochMillis") is { } millis
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime
            : null;
    }

    /// <summary>
    /// Turns a failed response into something an operator can act on.
    ///
    /// The 403 case matters most here and is the one this milestone had to get right: Assets is a
    /// Premium/Enterprise feature, so a perfectly valid credential on a Standard-plan site is refused,
    /// and reporting that as "authentication failed" sends the operator to rotate a token that was
    /// never the problem.
    /// </summary>
    internal static ConnectionTestResult Describe(OutboundHttpResponse response, string surface) =>
        response.StatusCode switch
        {
            0 => ConnectionTestResult.Fail($"{surface} could not be reached: {response.TransportError}"),
            401 => ConnectionTestResult.Fail(
                $"{surface} rejected the credentials (401). For Jira Cloud the user is the Atlassian "
                + "account email and the credential is an API token — a password is refused."),
            403 => ConnectionTestResult.Fail(
                $"{surface} accepted the credentials and refused the request (403). Either the account "
                + "lacks permission, or the site's plan does not include this feature — Assets needs "
                + "Jira Service Management Premium or Enterprise."),
            404 => ConnectionTestResult.Fail(
                $"{surface} returned 404. Check the base URL, and that the service desk or workspace "
                + "still exists."),
            _ => ConnectionTestResult.Fail($"{surface} answered HTTP {response.StatusCode}.")
        };

    internal static string Excerpt(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "(no response body)"
            : body.Length <= 400 ? body.Trim() : body[..400].Trim() + "…";
}
