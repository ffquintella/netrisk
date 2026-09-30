using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using Microsoft.Extensions.DependencyInjection;
using Model.Authentication;
using NSubstitute;
using Serilog;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Regression cover for the session-recovery bug reported 2026-09-30: a token that expired while the
/// machine was asleep left <see cref="AuthenticationRestService.DiscardAuthenticationToken"/> clearing
/// only the persisted LiteDB values. <see cref="AuthenticationRestService.IsAuthenticated"/> and
/// <see cref="AuthenticationRestService.AuthenticationCredential"/> stayed stale in memory, so every
/// call after waking kept resending the same rejected token, kept 401'ing, and the app had no way back
/// to a login screen short of Logout (which exits) or a restart.
/// </summary>
public class AuthenticationRestServiceTest
{
    [Fact]
    public void DiscardingAnAuthenticatedSessionClearsTheInMemoryState()
    {
        var service = CreateAuthenticatedService(out _);

        service.DiscardAuthenticationToken();

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.AuthenticationCredential.JWTToken);
        Assert.Equal(AuthenticationType.None, service.AuthenticationCredential.AuthenticationType);
    }

    [Fact]
    public void DiscardingAnAuthenticatedSessionRaisesSessionExpiredExactlyOnce()
    {
        var service = CreateAuthenticatedService(out _);
        var raised = 0;
        service.SessionExpired += (_, _) => raised++;

        service.DiscardAuthenticationToken();

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Several REST services can each observe the same 401 and call
    /// <see cref="AuthenticationRestService.DiscardAuthenticationToken"/> independently; only the
    /// first should prompt the GUI to re-show the login dialog.
    /// </summary>
    [Fact]
    public void DiscardingAnAlreadyUnauthenticatedSessionDoesNotRaiseSessionExpiredAgain()
    {
        var service = CreateAuthenticatedService(out _);
        service.DiscardAuthenticationToken();
        var raised = 0;
        service.SessionExpired += (_, _) => raised++;

        service.DiscardAuthenticationToken();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void DiscardingStillClearsThePersistedConfiguration()
    {
        var service = CreateAuthenticatedService(out var configuration);

        service.DiscardAuthenticationToken();

        configuration.Received(1).SetConfigurationValue("IsAuthenticate", "false");
        configuration.Received(1).RemoveConfigurationValue("AuthToken");
        configuration.Received(1).RemoveConfigurationValue("AuthTokenTime");
    }

    private static AuthenticationRestService CreateAuthenticatedService(
        out IMutableConfigurationService configuration)
    {
        EnsureServiceProviderAccessor();

        configuration = Substitute.For<IMutableConfigurationService>();

        return new AuthenticationRestService(
            Substitute.For<IRegistrationService>(),
            Substitute.For<IRestService>(),
            configuration,
            Substitute.For<IEnvironmentService>())
        {
            IsAuthenticated = true,
            AuthenticationCredential = new AuthenticationCredential
            {
                AuthenticationType = AuthenticationType.JWT,
                JWTToken = "a-token"
            }
        };
    }

    private static readonly object AccessorGate = new();

    /// <summary>
    /// <c>ServiceBase</c> resolves its Serilog logger from the process-wide
    /// <see cref="ServiceProviderAccessor"/> at construction time — see the same guard in
    /// <c>RestServiceTokenRenewalTest</c>.
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
}
