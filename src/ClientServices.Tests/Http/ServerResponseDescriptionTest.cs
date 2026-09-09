using System;
using System.Net;
using ClientServices.Http;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Http;

/// <summary>
/// Cover for the message half of the 2026-09-09 refresh loop: the client logged
/// <c>Unknown error '&lt;' is an invalid start of a value. Path: $ | LineNumber: 1 |
/// BytePositionInLine: 0.</c> 4,537 times, which names neither the endpoint, nor the status, nor the
/// fact that the body was an HTML page from something in front of the API. Every assertion here is
/// about a message an operator can act on.
/// </summary>
public class ServerResponseDescriptionTest
{
    private const string ProxyErrorPage =
        "<html>\n<head><title>502 Bad Gateway</title></head>\n<body>\n<center><h1>502 Bad Gateway</h1></center>\n<hr><center>nginx</center>\n</body>\n</html>\n";

    /// <summary>
    /// A response shaped the way RestSharp actually shapes one — including the part that is easy to
    /// get wrong: for any non-2xx it sets <c>ResponseStatus.Error</c> and an <c>ErrorException</c>,
    /// even though the exchange completed and the body is right there.
    /// </summary>
    private static RestResponse Response(HttpStatusCode status, string body, string contentType)
    {
        var succeeded = (int)status is >= 200 and <= 299;

        return new RestResponse(new RestRequest("/Authentication/GetToken"))
        {
            StatusCode = status,
            IsSuccessStatusCode = succeeded,
            ResponseStatus = succeeded ? ResponseStatus.Completed : ResponseStatus.Error,
            ErrorException = succeeded
                ? null
                : new System.Net.Http.HttpRequestException($"Request failed with status code {status}"),
            Content = body,
            ContentType = contentType
        };
    }

    /// <summary>
    /// The case from the log file: a 200 whose body is HTML. The old message blamed the JSON reader;
    /// this one has to say what the server sent and where.
    /// </summary>
    [Fact]
    public void AnHtmlBodyOnASuccessfulStatusIsReportedAsANonJsonResponse()
    {
        var problem = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.OK, ProxyErrorPage, "text/html"));

        Assert.Contains("non-JSON response (HTML)", problem.Message);
        Assert.Contains("/Authentication/GetToken", problem.Message);
        Assert.Contains("HTTP 200 (OK)", problem.Message);
        Assert.Contains("content-type text/html", problem.Message);
        Assert.Contains("502 Bad Gateway", problem.Message);
        Assert.Contains("reverse proxy", problem.Message);
    }

    /// <summary>The JSON reader's complaint must not be what an operator reads.</summary>
    [Fact]
    public void AnHtmlBodyDoesNotReportTheJsonReadersComplaint()
    {
        var readError = new System.Text.Json.JsonException(
            "'<' is an invalid start of a value. Path: $ | LineNumber: 1 | BytePositionInLine: 0.");

        var problem = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.OK, ProxyErrorPage, "text/html"), readError);

        Assert.DoesNotContain("invalid start of a value", problem.Message);
        Assert.DoesNotContain("BytePositionInLine", problem.Message);
    }

    /// <summary>
    /// A multi-line error page has to stay one log line. The proxy page above is seven lines; logged
    /// verbatim, one failure looked like seven in the file.
    /// </summary>
    [Fact]
    public void TheQuotedBodyIsCollapsedToOneLine()
    {
        var problem = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.OK, ProxyErrorPage, "text/html"));

        Assert.DoesNotContain("\n", problem.Message);
        Assert.DoesNotContain("\r", problem.Message);
    }

    /// <summary>A body longer than the snippet budget is truncated rather than pasted whole.</summary>
    [Fact]
    public void ALongBodyIsTruncated()
    {
        var problem = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.OK, "<html>" + new string('x', 4000) + "</html>", "text/html"));

        Assert.Contains($"first {ServerResponseDescription.SnippetLength} bytes of body", problem.Message);
        Assert.True(problem.Message.Length < 500, $"message was {problem.Message.Length} characters");
    }

    /// <summary>
    /// HTML served as <c>application/json</c> is still HTML — a proxy that rewrites the body but not
    /// the header is exactly the configuration that produces this failure.
    /// </summary>
    [Fact]
    public void MarkupIsRecognisedRegardlessOfTheDeclaredContentType()
    {
        Assert.Equal("HTML", ServerResponseDescription.MarkupKindOf(ProxyErrorPage, "application/json"));
        Assert.Equal("HTML", ServerResponseDescription.MarkupKindOf("  \n <!DOCTYPE html><p>hi</p>", null));
        Assert.Equal("XML", ServerResponseDescription.MarkupKindOf("<?xml version=\"1.0\"?><a/>", "text/xml"));
        Assert.Equal("XML or HTML", ServerResponseDescription.MarkupKindOf("<a/>", null));
    }

    /// <summary>A real JSON body is not markup, whatever else is wrong with it.</summary>
    [Theory]
    [InlineData("\"a-token\"")]
    [InlineData("{\"token\":\"a\"}")]
    [InlineData("")]
    [InlineData(null)]
    public void JsonAndEmptyBodiesAreNotMarkup(string? body)
    {
        Assert.Null(ServerResponseDescription.MarkupKindOf(body, "application/json"));
    }

    /// <summary>
    /// A refusal names the status and quotes what the server said. This is the branch that
    /// <c>ThrowOnAnyError</c> made unreachable, which is why the refresh now asks for a reporting
    /// client.
    /// </summary>
    [Fact]
    public void ARefusalNamesTheStatusAndTheBody()
    {
        var problem = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.Forbidden, "{\"title\":\"client registration not approved\"}", "application/json"));

        Assert.Contains("refused /Authentication/GetToken", problem.Message);
        Assert.Contains("HTTP 403 (Forbidden)", problem.Message);
        Assert.Contains("client registration not approved", problem.Message);
    }

    /// <summary>
    /// A completed refusal must not be described as a failed request.
    ///
    /// RestSharp sets <c>ResponseStatus.Error</c> on every non-2xx, so a description that branched on
    /// <c>ResponseStatus</c> reported "the request did not complete" for a 403 that arrived intact,
    /// with a body explaining itself — the same information loss this class exists to stop.
    /// </summary>
    [Fact]
    public void ACompletedRefusalIsNotMistakenForATransportFailure()
    {
        var response = Response(HttpStatusCode.BadGateway, "<html>502 from nginx</html>", "text/html");

        Assert.Equal(ResponseStatus.Error, response.ResponseStatus);

        var problem = ServerResponseDescription.Describe("/Authentication/GetToken", response);

        Assert.Contains("HTTP 502 (BadGateway)", problem.Message);
        Assert.DoesNotContain("did not complete", problem.Message);
        Assert.DoesNotContain("Request failed with status code", problem.Message);
    }

    /// <summary>A 2xx with an unparseable but non-markup body reports the parser's reason, having none better.</summary>
    [Fact]
    public void AnUnreadableJsonBodyReportsTheReadError()
    {
        var problem = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.OK, "{\"token\":\"a\"}", "application/json"),
            new System.Text.Json.JsonException("The JSON value could not be converted to System.String."));

        Assert.Contains("could not be read", problem.Message);
        Assert.Contains("HTTP 200 (OK)", problem.Message);
        Assert.Contains("could not be converted", problem.Message);
    }

    /// <summary>
    /// A request that never completed has no status, and saying "HTTP 0 (0)" would be worse than
    /// saying it did not complete.
    /// </summary>
    [Fact]
    public void ATransportFailureIsReportedWithoutInventingAStatus()
    {
        var response = new RestResponse(new RestRequest("/Authentication/GetToken"))
        {
            ResponseStatus = ResponseStatus.Error,
            ErrorException = new System.Net.Http.HttpRequestException("No such host is known."),
            StatusCode = 0
        };

        var problem = ServerResponseDescription.Describe("/Authentication/GetToken", response);

        Assert.Contains("did not complete", problem.Message);
        Assert.Contains("No such host is known.", problem.Message);
        Assert.DoesNotContain("HTTP 0", problem.Message);
    }

    /// <summary>
    /// The key is what collapses repeats, so it must not carry anything that varies between two
    /// occurrences of the same failure — a proxy stamps its error page with a request id and a time.
    /// </summary>
    [Fact]
    public void TheKeyIgnoresTheBodySoAVaryingErrorPageStillCollapses()
    {
        var first = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.BadGateway, "<html>request id 41ab2</html>", "text/html"));
        var second = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.BadGateway, "<html>request id 90ffc</html>", "text/html"));

        Assert.Equal(first.Key, second.Key);
        Assert.NotEqual(first.Message, second.Message);
    }

    /// <summary>...while genuinely different failures stay different, so each gets reported once.</summary>
    [Fact]
    public void TheKeyDistinguishesDifferentFailures()
    {
        var html = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.OK, ProxyErrorPage, "text/html"));
        var forbidden = ServerResponseDescription.Describe("/Authentication/GetToken",
            Response(HttpStatusCode.Forbidden, "no", "application/json"));
        var otherPath = ServerResponseDescription.Describe("/Authentication/AuthenticatedUserInfo",
            Response(HttpStatusCode.OK, ProxyErrorPage, "text/html"));

        Assert.NotEqual(html.Key, forbidden.Key);
        Assert.NotEqual(html.Key, otherPath.Key);
    }

    /// <summary>A null response is described rather than dereferenced.</summary>
    [Fact]
    public void ANullResponseIsDescribed()
    {
        var problem = ServerResponseDescription.Describe("/Authentication/GetToken", null);

        Assert.Contains("no response", problem.Message);
        Assert.Contains("/Authentication/GetToken", problem.Message);
    }
}
