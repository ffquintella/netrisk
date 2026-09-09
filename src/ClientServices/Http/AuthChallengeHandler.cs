using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ClientServices.Http;

/// <summary>
/// Turns the API's authentication challenge into the 401 the rest of this assembly is written
/// against.
///
/// The API's default challenge scheme is the SAML policy scheme, so an unauthenticated or expired
/// request is not refused with a 401 — it is answered with a <b>302 to the identity provider</b>
/// (verified against the homolog installation on 2026-09-09: every one of "no Authorization
/// header", "Bearer &lt;garbage&gt;" and "Bearer &lt;expired jwt&gt;" answered
/// <c>302 https://minhaconta.fgv.br/iamapps/ssologin/…</c>). RestSharp follows redirects by
/// default, so the challenge never reached the client layer as a challenge at all:
///
/// <list type="bullet">
/// <item>a GET followed the redirect and deserialized the identity provider's <b>login page</b>,
/// which is the "'&lt;' is an invalid start of a value" the operator sees on half the screens;</item>
/// <item>a PUT or POST was re-sent to that login URL, which answers <b>400</b> for a JSON body on a
/// write verb — reported to the operator as "Error updating IRP template task: Request failed with
/// status code BadRequest", i.e. as if the server had rejected what they typed.</item>
/// </list>
///
/// So the ~40 <c>if (ex.StatusCode == HttpStatusCode.Unauthorized)
/// authenticationService.DiscardAuthenticationToken()</c> blocks in this assembly had never once
/// run against a SAML installation, and an expired session was indistinguishable from invalid
/// input. This handler restores that distinction at the transport, once, for every service: the
/// client no longer follows redirects (see <c>RestService.Initialize</c>) and a redirect answer is
/// reported as <see cref="HttpStatusCode.Unauthorized"/>.
///
/// A redirect is a safe signal to key on: the only redirecting action in the API is
/// <c>Authentication/SAMLRequest</c>, which the desktop client opens in the operator's browser and
/// never calls through RestSharp.
/// </summary>
public sealed class AuthChallengeHandler : DelegatingHandler
{
    /// <summary>The 401 body, so a caller that deserializes an error gets JSON rather than HTML.</summary>
    internal const string ChallengeBody =
        """{"error":"authentication_required","message":"The server asked for a new sign-in: the session is missing or has expired."}""";

    private readonly Action? _onChallenge;

    /// <param name="innerHandler">The handler RestSharp built, which performs the request.</param>
    /// <param name="onChallenge">
    /// Invoked when a challenge is seen, before the 401 is returned. <c>RestService</c> uses it to
    /// drop the stale token; keeping the decision there rather than here leaves this handler a pure
    /// translation, and covers the services that have no 401 branch of their own.
    /// </param>
    public AuthChallengeHandler(HttpMessageHandler innerHandler, Action? onChallenge = null)
        : base(innerHandler)
    {
        _onChallenge = onChallenge;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!IsAuthenticationChallenge(response)) return response;

        // The identity provider's location and its login page are of no use to a JSON client, and
        // carrying the body forward is what produced the misleading parse errors.
        response.Dispose();

        _onChallenge?.Invoke();

        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            ReasonPhrase = "Authentication required",
            Content = new StringContent(ChallengeBody, System.Text.Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Whether the answer is the API asking for a sign-in rather than a reply to the request.
    ///
    /// Every redirect status counts, not just 302: which one an ASP.NET Core challenge emits depends
    /// on the handler, and a JSON API has no other reason to redirect this client.
    /// </summary>
    private static bool IsAuthenticationChallenge(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
