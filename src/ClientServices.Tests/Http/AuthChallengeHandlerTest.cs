using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Http;
using JetBrains.Annotations;
using Xunit;

namespace ClientServices.Tests.Http;

/// <summary>
/// Regression cover for the 2026-09-09 report "error saving here" on the IRP templates screen.
///
/// The save had never reached the API: the operator's session had expired, the API answered the PUT
/// with a 302 to the SAML identity provider, RestSharp followed it, and the identity provider
/// answered a JSON PUT to its login URL with 400. The operator was told
/// "Error updating IRP template task: Request failed with status code BadRequest" — a rejected save
/// — for a request the server never authenticated. The same redirect is why unauthenticated GETs
/// reported "'&lt;' is an invalid start of a value": they were parsing the login page.
/// </summary>
[TestSubject(typeof(AuthChallengeHandler))]
public class AuthChallengeHandlerTest
{
    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task AnyRedirectIsReportedAsUnauthorized(HttpStatusCode redirect)
    {
        var response = await SendThrough(Answering(redirect,
            location: "https://minhaconta.fgv.br/iamapps/ssologin/custom_saml_app/77510b7b"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The identity provider's login page must not travel on: deserializing it is what produced the
    /// "'&lt;' is an invalid start of a value" errors.
    /// </summary>
    [Fact]
    public async Task TheIdentityProvidersLoginPageIsNotPassedOn()
    {
        var response = await SendThrough(Answering(HttpStatusCode.Found,
            location: "https://minhaconta.fgv.br/iamapps/ssologin/custom_saml_app/77510b7b",
            body: "<!DOCTYPE html><html><head><title>Minha Conta FGV</title></head></html>",
            contentType: "text/html"));

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<", body);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// The point of the translation: the stale token is dropped, so the next sign-in does not reuse
    /// the credential the server has just refused.
    /// </summary>
    [Fact]
    public async Task AChallengeNotifiesTheCaller()
    {
        var notified = 0;

        await SendThrough(Answering(HttpStatusCode.Found, location: "https://idp.example/login"),
            onChallenge: () => notified++);

        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task ASuccessfulAnswerIsUntouchedAndNotifiesNothing()
    {
        var notified = 0;

        var response = await SendThrough(
            Answering(HttpStatusCode.OK, body: """[{"id":1}]"""),
            onChallenge: () => notified++);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""[{"id":1}]""", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, notified);
    }

    /// <summary>
    /// A genuine refusal must still arrive as itself — this handler exists to stop an expired
    /// session masquerading as a 400, not to start masking real ones. The IRP template task
    /// endpoint answers 400 when the chosen predecessor would close a dependency cycle, and that
    /// sentence has to reach the operator.
    /// </summary>
    [Fact]
    public async Task ARealRefusalIsNotRewritten()
    {
        var notified = 0;

        var response = await SendThrough(
            Answering(HttpStatusCode.BadRequest, body: "\"That predecessor would create a dependency cycle\""),
            onChallenge: () => notified++);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("dependency cycle", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, notified);
    }

    private static async Task<HttpResponseMessage> SendThrough(HttpMessageHandler inner,
        Action? onChallenge = null)
    {
        using var client = new HttpClient(new AuthChallengeHandler(inner, onChallenge));

        return await client.PutAsync("https://netrisk.example/IrpTemplates/1/Tasks/2",
            new StringContent("""{"title":"sdfadsfasdf"}""", Encoding.UTF8, "application/json"));
    }

    private static HttpMessageHandler Answering(HttpStatusCode status, string? location = null,
        string body = "", string contentType = "application/json")
        => new StubHandler(status, location, body, contentType);

    private sealed class StubHandler(HttpStatusCode status, string? location, string body, string contentType)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };

            if (location != null) response.Headers.Location = new Uri(location);

            return Task.FromResult(response);
        }
    }
}
