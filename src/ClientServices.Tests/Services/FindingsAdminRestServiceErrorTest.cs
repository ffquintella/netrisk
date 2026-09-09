using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// A regression test for a defect Track 8 found in the Track 3 client while building its own.
///
/// <see cref="FindingsAdminRestService"/>'s write path used RestSharp's <c>PostAsync</c>/<c>PutAsync</c>
/// extensions, which call <c>ThrowIfError</c> internally. A 400 or 422 therefore arrived as an
/// <c>HttpRequestException</c> with the response body already discarded — so the service's own
/// "pass the server's explanation through" branch was unreachable, and every rejected write surfaced
/// as a generic transport failure. An operator who typed an invalid dedup configuration was told the
/// server could not be reached.
///
/// The fix is <c>ExecuteAsync</c>, which hands the response back. This test fails on the pre-fix code.
/// </summary>
[TestSubject(typeof(FindingsAdminRestService))]
public class FindingsAdminRestServiceErrorTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IFindingsAdminService _service;

    public FindingsAdminRestServiceErrorTest()
    {
        _service = ResolveWith<IFindingsAdminService>(_backend);
    }

    [Fact]
    public async Task ARejectedWriteKeepsTheServersExplanationRatherThanBecomingATransportError()
    {
        _backend.On(Method.Put, "/DedupConfigurations/nessus",
            new
            {
                error = "invalid_parameter",
                parameterName = "StrategyChain",
                message = "A dedup chain needs at least one strategy."
            },
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = string.Empty
            }));

        Assert.Contains("at least one strategy", ex.Message);
    }

    [Fact]
    public async Task AnUnknownTargetIsStillReportedAsNotFound()
    {
        _backend.OnStatus(Method.Put, "/DedupConfigurations/unknown", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _service.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "unknown", StrategyChain = "HashBased"
            }));
    }

    [Fact]
    public async Task ARealTransportFailureIsStillATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/DedupConfigurations/nessus");

        await Assert.ThrowsAsync<RestComunicationException>(() =>
            _service.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = "HashBased"
            }));
    }

    /// <summary>
    /// The same assertion as the first test, against the client the application actually hands this
    /// service: <c>ThrowOnAnyError</c> on. That flag raises before <c>ExecuteAsync</c> returns and
    /// carries only "Request failed with status code BadRequest", so the service's
    /// pass-the-body-through branch ran in the tests above and nowhere else. Switching to
    /// <c>ExecuteAsync</c> was half the fix; asking for an error-reporting client is the other half.
    ///
    /// Fails on the pre-fix code with "Error calling /ApiTokens" — which is what an operator who
    /// named an unknown scope was actually told.
    /// </summary>
    [Fact]
    public async Task ARejectedIssueExplainsItselfOnTheClientTheApplicationUses()
    {
        using var production = new StubRestBackend { ThrowsOnErrorResponses = true };
        var service = ResolveWith<IFindingsAdminService>(production);

        production.On(Method.Post, "/ApiTokens",
            new
            {
                error = "invalid_parameter",
                parameterName = "scopes",
                message = "Unknown scope: vulnerabilities:delete. Available: vulnerabilities:import, "
                          + "vulnerabilities:read, vulnerabilities:write, risks:read."
            },
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            service.IssueApiTokenAsync("teste", "vulnerabilities:delete", null, null));

        Assert.Contains("Unknown scope: vulnerabilities:delete", ex.Message);
    }

    /// <summary>
    /// A 2xx whose body is not JSON: a proxy sign-in page, an SPA fallback, a server address that
    /// points at the website rather than the API. The observed report was System.Text.Json's
    /// "'&lt;' is an invalid start of a value. Path: $ | LineNumber: 1 | BytePositionInLine: 0",
    /// logged as "Could not issue an API token" — which names the wrong component. The message has
    /// to say that something other than the API answered, and what it answered with.
    /// </summary>
    [Fact]
    public async Task AnHtmlAnswerToAWriteNamesTheAnswerRatherThanTheJsonParser()
    {
        _backend.OnContent(Method.Post, "/ApiTokens",
            "\n<!DOCTYPE html><html><body><h1>Sign in to continue</h1></body></html>",
            "text/html");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.IssueApiTokenAsync("teste", "vulnerabilities:read", null, null));

        Assert.Contains("POST /ApiTokens", ex.Message);
        Assert.Contains("200", ex.Message);
        Assert.Contains("text/html", ex.Message);
        Assert.Contains("not the NetRisk API", ex.Message);
        Assert.Contains("Sign in to continue", ex.Message);
    }

    /// <summary>The same guard on a read, which is what leaves the token list mysteriously empty.</summary>
    [Fact]
    public async Task AnHtmlAnswerToAReadNamesTheAnswerRatherThanTheJsonParser()
    {
        _backend.OnContent(Method.Get, "/ApiTokens",
            "\n<!DOCTYPE html><html><body>gateway</body></html>", "text/html");

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.GetApiTokensAsync());

        Assert.Contains("GET /ApiTokens", ex.Message);
        Assert.Contains("text/html", ex.Message);
    }

    /// <summary>
    /// The failure the operator actually hit on 2026-09-09: an expired session. The API answers a
    /// 302 to the identity provider, which <c>AuthChallengeHandler</c> now translates into a 401
    /// whose body says the session expired. That sentence has to reach the tab — "Error calling
    /// /ApiTokens" sends the operator looking for a fault in the token issuer instead of pressing
    /// sign-in.
    /// </summary>
    [Fact]
    public async Task AnExpiredSessionSaysTheSessionExpired()
    {
        using var production = new StubRestBackend { ThrowsOnErrorResponses = true };
        var service = ResolveWith<IFindingsAdminService>(production);

        production.On(Method.Post, "/ApiTokens",
            new
            {
                error = "authentication_required",
                message = "The server asked for a new sign-in: the session is missing or has expired."
            },
            HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            service.IssueApiTokenAsync("teste", "vulnerabilities:read", null, null));

        Assert.Contains("session is missing or has expired", ex.Message);
    }

    /// <summary>
    /// And a well-formed issue still works — the guard must not turn a 201 into a failure. The
    /// controller answers 201 Created with a Location header, so this also pins that RestSharp does
    /// not chase that header instead of handing the body back.
    /// </summary>
    [Fact]
    public async Task AnIssuedTokenIsReturnedFromA201()
    {
        _backend.On(Method.Post, "/ApiTokens",
            new
            {
                id = 7,
                name = "teste",
                keyId = "a1b2c3d4",
                secret = "nrk_a1b2c3d4_deadbeef",
                scopes = new[] { "vulnerabilities:read" }
            },
            HttpStatusCode.Created);

        var issued = await _service.IssueApiTokenAsync("teste", "vulnerabilities:read", null, null);

        Assert.Equal(7, issued.Id);
        Assert.Equal("nrk_a1b2c3d4_deadbeef", issued.Secret);
        Assert.Equal(new[] { "vulnerabilities:read" }, issued.Scopes);
    }
}
