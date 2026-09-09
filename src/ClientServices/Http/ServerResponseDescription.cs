using System;
using System.Linq;
using System.Net;
using RestSharp;

namespace ClientServices.Http;

/// <summary>
/// A server failure described twice: once for a human, once for the code that has to notice the
/// same failure repeating.
/// </summary>
/// <param name="Key">
/// A stable identity for the *kind* of failure — path, transport-or-status, status code, media type.
/// Deliberately excludes the body snippet and any exception text, so a proxy that varies its error
/// page (a request id, a timestamp) still collapses to one key instead of logging forever.
/// </param>
/// <param name="Message">What to write to the log. Names the endpoint, the status and the body.</param>
public sealed record ServerResponseProblem(string Key, string Message);

/// <summary>
/// Turns a refused or unreadable REST response into a sentence that says what the server did.
///
/// This exists because of the loop recorded in nr-gui20260909.log: 4,537 copies of
/// <c>Unknown error '&lt;' is an invalid start of a value. Path: $ | LineNumber: 0 …</c>. That is
/// <see cref="System.Text.Json.JsonSerializer"/> complaining about the first character of the body,
/// and it says nothing an operator can act on — not the endpoint, not the status code, not the fact
/// that the body was an HTML page because a reverse proxy answered instead of the API. Every one of
/// those lines was a caller that had a <see cref="RestResponse"/> in hand and reported the exception
/// message instead of it.
/// </summary>
public static class ServerResponseDescription
{
    /// <summary>How much of the body to quote. Enough to recognise a proxy's error page.</summary>
    internal const int SnippetLength = 120;

    /// <summary>
    /// Describes <paramref name="response"/> as a failure.
    ///
    /// <paramref name="readError"/> is the exception a caller got while parsing the body, when there
    /// was one. It only shapes the message for a body that is *not* obviously markup — for markup we
    /// can say something better than the parser can.
    /// </summary>
    public static ServerResponseProblem Describe(string path, RestResponse? response,
        Exception? readError = null)
    {
        if (response == null)
            return new ServerResponseProblem($"no-response|{path}",
                $"the server returned no response to {path}");

        // A missing status is the only reliable sign that nothing came back. RestSharp reports
        // ResponseStatus.Error and fills in ErrorException for an ordinary 403 as well — the status,
        // the headers and the body are all there — so branching on ResponseStatus reported every
        // refusal as "the request did not complete" and threw away the explanation.
        if (response.StatusCode == 0)
            return DescribeTransportFailure(path,
                response.ErrorException?.Message ?? response.ErrorMessage ?? response.ResponseStatus.ToString());

        var status = DescribeStatus(response.StatusCode);
        var contentType = ContentTypeOf(response);
        var markup = MarkupKindOf(response.Content, contentType);

        if (markup != null)
            return new ServerResponseProblem(
                $"non-json|{path}|{(int)response.StatusCode}|{contentType ?? "?"}",
                $"the server returned a non-JSON response ({markup}) to {path} — {status}"
                + DescribeContentType(contentType) + DescribeSnippet(response.Content)
                + ". A reverse proxy, a sign-in interstitial or an error page in front of the API "
                + "answers like this; check that Server:Url reaches the API itself");

        if (readError != null)
            return new ServerResponseProblem(
                $"unreadable|{path}|{(int)response.StatusCode}|{contentType ?? "?"}",
                $"the server's answer to {path} could not be read — {status}"
                + DescribeContentType(contentType) + DescribeSnippet(response.Content)
                + $". {readError.Message}");

        if (!response.IsSuccessStatusCode)
            return new ServerResponseProblem($"status|{path}|{(int)response.StatusCode}",
                $"the server refused {path} — {status}" + DescribeSnippet(response.Content));

        return new ServerResponseProblem($"unexpected|{path}|{(int)response.StatusCode}",
            $"the server's answer to {path} was not usable — {status}"
            + DescribeContentType(contentType) + DescribeSnippet(response.Content));
    }

    /// <summary>Describes a request that never got an answer.</summary>
    public static ServerResponseProblem DescribeTransportFailure(string path, string reason) =>
        new($"transport|{path}",
            $"the request to {path} did not complete: {Collapse(reason)}");

    /// <summary>
    /// The markup family <paramref name="content"/> belongs to, or null when it is not markup.
    ///
    /// A leading <c>&lt;</c> is the whole test, because a leading <c>&lt;</c> is exactly what the JSON
    /// reader was refusing. The content type is consulted only to name the family more precisely: a
    /// proxy that sends an HTML page as <c>application/json</c> is a real thing, and it is still HTML.
    /// </summary>
    public static string? MarkupKindOf(string? content, string? contentType = null)
    {
        var trimmed = content?.TrimStart();
        if (string.IsNullOrEmpty(trimmed) || trimmed[0] != '<') return null;

        if (LooksLikeHtml(trimmed) || (contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? false))
            return "HTML";

        return contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) ?? false ? "XML" : "XML or HTML";
    }

    private static bool LooksLikeHtml(string trimmed)
    {
        var head = trimmed.Length > 512 ? trimmed[..512] : trimmed;
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase)
               || head.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase)
               || head.Contains("<head", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The status, with its number as well as its name. <c>0</c> is what RestSharp reports when the
    /// exchange never produced one, and printing "HTTP 0 (0)" would be worse than saying so.
    /// </summary>
    private static string DescribeStatus(HttpStatusCode status) =>
        status == 0 ? "no HTTP status" : $"HTTP {(int)status} ({status})";

    private static string? ContentTypeOf(RestResponse response) =>
        response.ContentType ?? response.ContentHeaders
            ?.FirstOrDefault(h => string.Equals(h.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();

    private static string DescribeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "" : $", content-type {contentType}";

    private static string DescribeSnippet(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return ", empty body";

        var collapsed = Collapse(content);
        return collapsed.Length <= SnippetLength
            ? $", body: \"{collapsed}\""
            : $", first {SnippetLength} bytes of body: \"{collapsed[..SnippetLength]}…\"";
    }

    /// <summary>
    /// Flattens whitespace so a multi-line HTML page stays one log line — a wrapped error page is
    /// how a single failure came to look like thirty in the log file.
    /// </summary>
    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
