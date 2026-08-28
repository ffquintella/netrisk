using System;
using System.Threading.Tasks;
using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Model.Authentication;
using Model.Configuration;
using NSubstitute;
using RestSharp;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Regression cover for the sign-in loop reported on 2026-08-28: the desktop client logged
/// "Token is expired" and the API logged "Authentication token created" several times a second,
/// forever, because <see cref="RestService.GetClient"/> demanded 300 minutes of remaining validity
/// from tokens the API mints with a 60-minute lifetime. Each call refreshed the token, judged the
/// new one expired too, and sent the request with the old token anyway.
/// </summary>
public class RestServiceTokenRenewalTest
{
    /// <summary>
    /// The loop itself: a token that has only just been issued must be used as it is.
    /// </summary>
    [Fact]
    public void AFreshlyIssuedServerDefaultTokenIsNotRenewed()
    {
        var token = TestTokens.Create(lifetimeMinutes: 60);
        var harness = new Harness(token);

        harness.RestService.GetClient();

        harness.Authentication.DidNotReceive().RefreshToken();
    }

    /// <summary>
    /// The shortest lifetime an operator can configure must not reintroduce the loop either.
    /// </summary>
    [Fact]
    public void AFreshlyIssuedOneMinuteTokenIsNotRenewed()
    {
        var harness = new Harness(TestTokens.Create(lifetimeMinutes: 1));

        harness.RestService.GetClient();

        harness.Authentication.DidNotReceive().RefreshToken();
    }

    /// <summary>
    /// Renewal still happens inside the window — and the request goes out with the token the
    /// renewal produced, not the one we just rejected.
    /// </summary>
    [Fact]
    public async Task ATokenInsideTheRenewalWindowIsReplacedAndTheNewOneIsSent()
    {
        var expiring = TestTokens.Create(lifetimeMinutes: 60, issuedAt: DateTime.UtcNow.AddMinutes(-58));
        var renewed = TestTokens.Create(lifetimeMinutes: 60);
        var harness = new Harness(expiring);
        harness.RenewsTo(renewed);

        var client = harness.RestService.GetClient();

        harness.Authentication.Received(1).RefreshToken();
        Assert.Equal($"Bearer {renewed}", await AuthorizationHeaderOf(client));
    }

    /// <summary>
    /// A refresh that fails leaves the old token in place rather than clearing the header — the
    /// server answers 401 and the sign-in flow takes over, which is the pre-existing behaviour.
    /// </summary>
    [Fact]
    public async Task AFailedRenewalStillSendsTheOldToken()
    {
        var expiring = TestTokens.Create(lifetimeMinutes: 60, issuedAt: DateTime.UtcNow.AddMinutes(-58));
        var harness = new Harness(expiring);
        harness.Authentication.RefreshToken().Returns(-1);

        var client = harness.RestService.GetClient();

        Assert.Equal($"Bearer {expiring}", await AuthorizationHeaderOf(client));
    }

    /// <summary>
    /// <c>RefreshToken</c> itself asks for a client with the verification turned off; that path must
    /// not call back into a refresh, which is the recursion half of the loop.
    /// </summary>
    [Fact]
    public void IgnoringTheTimeVerificationNeverRenews()
    {
        var expiring = TestTokens.Create(lifetimeMinutes: 60, issuedAt: DateTime.UtcNow.AddMinutes(-59));
        var harness = new Harness(expiring);

        harness.RestService.GetClient(ignoreTimeVerification: true);

        harness.Authentication.DidNotReceive().RefreshToken();
    }

    private static async Task<string?> AuthorizationHeaderOf(RestClient client)
    {
        var request = new RestRequest("/Authentication/AuthenticatedUserInfo");
        await client.Options.Authenticator!.Authenticate(client, request);
        return request.Parameters.TryFind("Authorization")?.Value as string;
    }

    /// <summary>
    /// A <see cref="RestService"/> wired to a substituted authentication service whose
    /// <c>CheckTokenValidTime</c> is the real implementation — the expiry comparison under test has
    /// to be the production one.
    /// </summary>
    private sealed class Harness
    {
        public IAuthenticationService Authentication { get; }
        public RestService RestService { get; }

        private static readonly object AccessorGate = new();

        private readonly AuthenticationCredential _credential;

        public Harness(string token)
        {
            EnsureServiceProviderAccessor();

            _credential = new AuthenticationCredential
            {
                AuthenticationType = AuthenticationType.JWT,
                JWTToken = token
            };

            var real = new AuthenticationRestService(
                Substitute.For<IRegistrationService>(),
                Substitute.For<IRestService>(),
                Substitute.For<IMutableConfigurationService>(),
                Substitute.For<IEnvironmentService>());

            Authentication = Substitute.For<IAuthenticationService>();
            Authentication.IsAuthenticated.Returns(true);
            Authentication.AuthenticationCredential.Returns(_ => _credential);
            Authentication.CheckTokenValidTime(Arg.Any<string>(), Arg.Any<int>())
                .Returns(call => real.CheckTokenValidTime(call.ArgAt<string>(0), call.ArgAt<int>(1)));

            var environment = Substitute.For<IEnvironmentService>();
            environment.DeviceID.Returns("B0804F34");

            var configuration = Substitute.For<IMutableConfigurationService>();
            configuration.GetConfigurationValue("Server").Returns("https://localhost:5443");

            RestService = new RestService(
                new SerilogLoggerFactory(new LoggerConfiguration().CreateLogger()),
                new ServerConfiguration(),
                environment,
                configuration)
            {
                AuthenticationServiceOverride = Authentication
            };
        }

        /// <summary>
        /// <c>ServiceBase</c> resolves its Serilog logger from the process-wide
        /// <see cref="ServiceProviderAccessor"/> at construction time, so constructing the real
        /// authentication service needs one to be there. Only filled in when no other test class has
        /// already set one — theirs is a superset of this.
        /// </summary>
        private static void EnsureServiceProviderAccessor()
        {
            lock (AccessorGate)
            {
                if (ServiceProviderAccessor.Provider != null) return;

                var services = new ServiceCollection();
                services.AddSingleton<ILogger>(new LoggerConfiguration().CreateLogger());
                ServiceProviderAccessor.Provider = services.BuildServiceProvider();
            }
        }

        /// <summary>Makes a refresh behave like the real one: it stores the new token and reports success.</summary>
        public void RenewsTo(string token)
        {
            Authentication.RefreshToken().Returns(_ =>
            {
                _credential.JWTToken = token;
                return 0;
            });
        }
    }
}
