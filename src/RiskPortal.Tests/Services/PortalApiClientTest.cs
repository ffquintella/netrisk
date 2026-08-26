using System.Net;
using JetBrains.Annotations;
using RiskPortal.Services;
using Xunit;

namespace RiskPortal.Tests.Services;

/// <summary>
/// The part of <see cref="PortalApiClient"/> that decides what a business reviewer is told when the
/// API refuses something.
///
/// Worth its own tests because it is the difference between a portal a reviewer can use and one they
/// have to phone somebody about. "403" tells them nothing; "you are not an appointed risk reviewer for
/// this business entity" tells them who to ask.
/// </summary>
[TestSubject(typeof(PortalApiClient))]
public class PortalApiClientTest
{
    [Fact]
    public void TheServersOwnMessageIsPreferredOverAnythingInvented()
    {
        var explanation = PortalApiClient.Explain(HttpStatusCode.UnprocessableEntity,
            """{"error":"risk_appetite_ceiling","message":"Residual 9.10 is above the acceptance ceiling of 6.00."}""");

        Assert.Equal("Residual 9.10 is above the acceptance ceiling of 6.00.", explanation);
    }

    [Fact]
    public void ACapitalisedMessagePropertyIsAlsoRead()
    {
        var explanation = PortalApiClient.Explain(HttpStatusCode.BadRequest,
            """{"Message":"A justification is required."}""");

        Assert.Equal("A justification is required.", explanation);
    }

    [Fact]
    public void AForbiddenWithNoBodyExplainsTheAppointmentRule()
    {
        var explanation = PortalApiClient.Explain(HttpStatusCode.Forbidden, null);

        Assert.Contains("not an appointed risk reviewer", explanation);
    }

    [Fact]
    public void AnExpiredSessionSaysSoAndReassuresAboutEarlierDecisions()
    {
        var explanation = PortalApiClient.Explain(HttpStatusCode.Unauthorized, "");

        Assert.Contains("session has expired", explanation);
        Assert.Contains("unaffected", explanation);
    }

    [Fact]
    public void AConflictIsTheAlreadyAcceptedCase()
    {
        Assert.Contains("already has a live acceptance",
            PortalApiClient.Explain(HttpStatusCode.Conflict, null));
    }

    [Fact]
    public void AnUnknownStatusFallsBackToNothingWasSaved()
    {
        Assert.Contains("Nothing was saved",
            PortalApiClient.Explain(HttpStatusCode.InternalServerError, null));
    }

    /// <summary>
    /// A plain-text body is still better than a generic message, but only if it is short enough to be
    /// a message rather than an HTML error page.
    /// </summary>
    [Fact]
    public void AShortPlainTextBodyIsUsed()
    {
        Assert.Equal("Id mismatch.",
            PortalApiClient.Explain(HttpStatusCode.BadRequest, "Id mismatch."));
    }

    [Fact]
    public void ALongNonJsonBodyIsNotShownToTheReviewer()
    {
        var page = new string('x', 5000);

        var explanation = PortalApiClient.Explain(HttpStatusCode.BadRequest, page);

        Assert.DoesNotContain("xxxx", explanation);
        Assert.Contains("Nothing was saved", explanation);
    }

    [Fact]
    public void AJsonBodyWithNoMessageFallsBackRatherThanShowingJson()
    {
        var explanation = PortalApiClient.Explain(HttpStatusCode.BadRequest,
            """{"error":"invalid_parameter","parameterName":"ExpiresAt"}""");

        Assert.DoesNotContain("parameterName", explanation);
    }
}
