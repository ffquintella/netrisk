using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using API.Controllers;
using API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Track 7 milestone 7.3.1 — the controller authorization sweep, made permanent.
///
/// The audit that produced this file found <c>WebAuthnController</c> shipping with no class-level
/// <c>[Authorize]</c>, so four of its actions — including <c>register/begin</c> and
/// <c>register/complete</c> — carried no authorization metadata. They happened to fail closed,
/// because the base controller throws when there is no principal, but "happens to" is not an access
/// control. Nothing would have caught the next one.
///
/// So this test enumerates every action on every controller in the API assembly and requires each to
/// be covered by an <see cref="AuthorizeAttribute"/> (or <see cref="PermissionAuthorizeAttribute"/>,
/// which derives from it) on the action or its controller — unless it is named in
/// <see cref="IntentionallyAnonymous"/>, where each entry has to carry a reason.
///
/// Adding an endpoint to the allowlist is a deliberate, reviewable act. Forgetting to authorize one
/// is now a test failure.
/// </summary>
public class ControllerAuthorizationInventoryTest
{
    /// <summary>
    /// The endpoints that must remain reachable without a session, and why. Referenced from the
    /// findings register (NR-2026-009) so the justification lives with the audit as well as the code.
    /// </summary>
    private static readonly Dictionary<string, string> IntentionallyAnonymous = new(StringComparer.Ordinal)
    {
        ["AuthenticationController.CreateSamlRequestId"] =
            "Mints the SAML request id for the desktop client, which has no session yet. Gated on an "
            + "administrator-approved client registration instead (finding NR-2026-001).",
        ["AuthenticationController.SAMLRequest"] =
            "The browser's entry point into the SAML redirect; by definition there is no session yet. "
            + "Refuses any request id the server did not mint.",
        ["AuthenticationController.GetAppSAMLToken"] =
            "Hands the desktop client the token for a completed SAML sign-in. Single-use and keyed by "
            + "a server-minted 190-bit request id, and only to the client registration that minted it "
            + "(finding NR-2026-001).",
        ["AuthenticationController.GetAllAuthenticationMethods"] =
            "Tells the login screen which methods to offer, before any credential is presented.",

        ["RegistrationController.IsRegistred"] =
            "Client-registration handshake; runs before the client has any credential.",
        ["RegistrationController.IsAccepted"] =
            "Same handshake — asks whether an administrator has approved this client yet.",
        ["RegistrationController.Register"] =
            "Creates the pending client registration an administrator then approves.",

        ["SystemController.Ping"] = "Liveness probe. Answers the constant 'Pong' and reads nothing.",
        ["SystemController.Version"] =
            "The updater needs the current version before it can authenticate with this build.",
        ["SystemController.ClientDownloadLocation"] = "Public download URL for the desktop client.",
        ["SystemController.UpdateScript"] = "Public update script for the desktop client.",

        ["IdentityProvidersController.GetAvailable"] =
            "Lists the enabled enterprise providers for the login screen; no provider secrets.",
        ["IdentityProvidersController.BeginOidc"] = "Starts an OIDC sign-in — pre-session by nature.",
        ["IdentityProvidersController.CompleteOidc"] = "OIDC redirect callback from the provider.",
        ["IdentityProvidersController.BeginSaml"] = "Starts a SAML sign-in — pre-session by nature.",
        ["IdentityProvidersController.AssertionConsumer"] =
            "SAML assertion consumer; the assertion signature is the authentication.",

        ["WebAuthnController.BeginAssertion"] =
            "A hardware factor is presented during sign-in, when there is no session. The challenge "
            + "is single-use and the signature is verified against a stored public key.",
        ["WebAuthnController.CompleteAssertion"] = "The other half of the same ceremony.",
        ["WebAuthnController.RedeemRecoveryCode"] =
            "Recovery codes are redeemed during sign-in; single-use and hashed at rest.",

        ["IssueSyncWebhooksController.Receive"] =
            "The caller is GitHub, GitLab, Jira or Azure DevOps, which cannot hold a NetRisk session. "
            + "Authenticity rests on the per-connection shared secret, verified over the raw body "
            + "before the payload is acted on."
    };

    private static IEnumerable<Type> Controllers() =>
        typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ControllerBase).IsAssignableFrom(t));

    /// <summary>Public, non-inherited methods carrying an HTTP verb attribute.</summary>
    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

    private static string Key(Type controller, MethodInfo action) => $"{controller.Name}.{action.Name}";

    private static bool HasAuthorization(Type controller, MethodInfo action) =>
        action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
        || controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

    /// <summary>The sweep. Every action is authorized, or explicitly and justifiably not.</summary>
    [Fact]
    public void EveryActionIsEitherAuthorizedOrOnTheAnonymousAllowlist()
    {
        var unprotected = new List<string>();

        foreach (var controller in Controllers())
        foreach (var action in Actions(controller))
        {
            var key = Key(controller, action);
            if (HasAuthorization(controller, action)) continue;
            if (IntentionallyAnonymous.ContainsKey(key)) continue;

            unprotected.Add(key);
        }

        Assert.True(unprotected.Count == 0,
            "These API actions carry no [Authorize]/[PermissionAuthorize] and are not on the "
            + "reviewed anonymous allowlist in this test:\n  "
            + string.Join("\n  ", unprotected.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The mirror image: an action marked <c>[AllowAnonymous]</c> has to be on the allowlist too, so
    /// that a *new* anonymous endpoint cannot be introduced without the justification being written
    /// down.
    /// </summary>
    [Fact]
    public void EveryAllowAnonymousActionIsOnTheAllowlist()
    {
        var undocumented = new List<string>();

        foreach (var controller in Controllers())
        foreach (var action in Actions(controller))
        {
            var anonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                            || controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            if (!anonymous) continue;

            var key = Key(controller, action);
            if (!IntentionallyAnonymous.ContainsKey(key)) undocumented.Add(key);
        }

        Assert.True(undocumented.Count == 0,
            "These actions are [AllowAnonymous] but carry no recorded justification. Add them to "
            + "IntentionallyAnonymous with a reason, and to docs/security/FINDINGS.md if the reason "
            + "is not obvious:\n  "
            + string.Join("\n  ", undocumented.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Keeps the allowlist honest in the other direction: an entry for an endpoint that no longer
    /// exists (or is now authorized) would silently keep permission open for a future action that
    /// happens to reuse the name.
    /// </summary>
    [Fact]
    public void TheAnonymousAllowlistHasNoStaleEntries()
    {
        var live = Controllers()
            .SelectMany(c => Actions(c).Select(a => Key(c, a)))
            .ToHashSet(StringComparer.Ordinal);

        var stale = IntentionallyAnonymous.Keys.Where(k => !live.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            "These allowlist entries no longer name an existing action:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// Every allowlist entry states a reason. A bare "true" would make the list a rubber stamp.
    /// </summary>
    [Fact]
    public void EveryAllowlistEntryCarriesAReason() =>
        Assert.All(IntentionallyAnonymous,
            entry => Assert.True(entry.Value.Length > 20, $"{entry.Key} has no real justification"));

    /// <summary>
    /// The regression assertion for finding NR-2026-009 specifically: the WebAuthn enrolment
    /// endpoints must require a session. Written as its own test so the failure names the endpoint
    /// rather than appearing in a list of many.
    /// </summary>
    [Theory]
    [InlineData(nameof(WebAuthnController.BeginRegistration))]
    [InlineData(nameof(WebAuthnController.CompleteRegistration))]
    [InlineData(nameof(WebAuthnController.GetMine))]
    [InlineData(nameof(WebAuthnController.GetStatus))]
    public void WebAuthnEnrolmentRequiresASession(string actionName)
    {
        var action = typeof(WebAuthnController).GetMethod(actionName);

        Assert.NotNull(action);
        Assert.True(HasAuthorization(typeof(WebAuthnController), action!),
            $"WebAuthnController.{actionName} must be covered by an authorization policy");
        Assert.Empty(action!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    /// <summary>
    /// The fallback policy is the safety net behind all of the above: an action with no
    /// authorization metadata at all is evaluated against it, and it must require an authenticated,
    /// existing user. If this ever returned null, an unannotated endpoint would be wide open.
    /// </summary>
    [Fact]
    public void TheFallbackPolicyRequiresAnAuthenticatedValidUser()
    {
        var provider = new DefaultPolicyProvider(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var fallback = provider.GetFallbackPolicyAsync().GetAwaiter().GetResult();

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        Assert.Contains(fallback.Requirements, r => r is ValidUserRequirement);
    }

    // ---- The credential-path rate limiter (Track 7 milestone 7.3.2) --------------------------

    /// <summary>
    /// The limiter covers the paths that cost a password verification, a signature verification or a
    /// token mint — and only those. Limiting the whole API to a per-minute budget would break a bulk
    /// scan import, which legitimately makes thousands of calls.
    /// </summary>
    [Theory]
    [InlineData("/Authentication/GetToken", true)]
    [InlineData("/Authentication/AppSAMLToken", true)]
    [InlineData("/authentication/samlrequestid", true)]
    [InlineData("/Registration", true)]
    [InlineData("/WebAuthn/assert/begin", true)]
    [InlineData("/FaceID/transactions/1/start", true)]
    [InlineData("/BiometricTransaction", true)]
    [InlineData("/IdentityProviders/oidc/callback", true)]
    [InlineData("/Vulnerabilities/import/nessus/abc", false)]
    [InlineData("/Risks", false)]
    [InlineData("/Files/local/chunk", false)]
    [InlineData("/", false)]
    public void TheRateLimiterCoversTheCredentialPathsAndNothingElse(string path, bool limited) =>
        Assert.Equal(limited,
            AuthRateLimiting.IsCredentialPath(new Microsoft.AspNetCore.Http.PathString(path)));

    /// <summary>
    /// The budget has to clear the *legitimate* traffic. The desktop client polls
    /// <c>AppSAMLToken</c> once a second for up to five minutes while the user completes a
    /// single-sign-on in their browser; a limit at or below 300 per minute would cut a slow sign-in
    /// off partway through and present as "SAML authentication timed out". A limit that breaks the
    /// normal case is a limit somebody sets to zero, so this pins it.
    /// </summary>
    [Fact]
    public void TheRateLimitClearsTheSingleSignOnPollingRate()
    {
        // Read as JSON rather than through the configuration stack: this asserts on what ships in
        // the file, and a configuration builder here would also pick up whatever the test host has
        // in its environment.
        using var settings = System.Text.Json.JsonDocument.Parse(
            System.IO.File.ReadAllText(System.IO.Path.Combine(ApiProjectDirectory(), "appsettings.json")));

        var permits = settings.RootElement
            .GetProperty("Security").GetProperty("RateLimit").GetProperty("AuthRequestsPerMinute")
            .GetInt32();

        // 60 polls a minute, with headroom for the sign-in requests happening alongside them.
        Assert.True(permits >= 300,
            $"the configured limit of {permits}/minute would break a single sign-on that takes "
            + "longer than a minute");
    }

    /// <summary>Walks up to the API project, so the test does not depend on the runner's directory.</summary>
    private static string ApiProjectDirectory()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null
               && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "src", "netrisk.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, "src", "API");
    }
}
