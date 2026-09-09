using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RestSharp;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using ILogger = Serilog.ILogger;

namespace ClientServices.Tests.Services;

/// <summary>
/// Regression cover for the refresh loop in nr-gui20260909.log.
///
/// A reverse proxy in front of the API answered <c>/Authentication/GetToken</c> with an HTML page.
/// <see cref="AuthenticationRestService.RefreshToken"/> handed that body to
/// <see cref="JsonSerializer"/>, logged the reader's complaint —
/// <c>Unknown error '&lt;' is an invalid start of a value…</c> — and was called again on the next
/// 10-second notification tick, forever: 4,537 identical Error lines in one day's log, and 4,537
/// requests at a server that was already answering wrongly.
///
/// These tests drive the real service over <see cref="StubRestBackend"/>, so the RestSharp client,
/// the status handling and the body parsing are production's.
/// </summary>
public class TokenRefreshLoopTest
{
    private const string TokenPath = "/Authentication/GetToken";
    private const string UserInfoPath = "/Authentication/AuthenticatedUserInfo";

    private const string ProxyErrorPage =
        "<html>\n<head><title>502 Bad Gateway</title></head>\n<body>\n<center><h1>502 Bad Gateway</h1></center>\n<hr><center>nginx/1.24.0</center>\n</body>\n</html>\n";

    /// <summary>
    /// The 2026-09-09 failure: HTML on a 200, which is what a proxy or an error page returns and what
    /// the JSON reader was choking on.
    /// </summary>
    [Fact]
    public void AnHtmlRefreshResponseIsReportedAsANonJsonResponseNamingTheEndpoint()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        Assert.Equal(-1, harness.Service.RefreshToken());

        var reported = Assert.Single(harness.Errors);
        Assert.Contains("non-JSON response (HTML)", reported);
        Assert.Contains(TokenPath, reported);
        Assert.Contains("HTTP 200 (OK)", reported);
        Assert.Contains("text/html", reported);
        Assert.Contains("502 Bad Gateway", reported);
    }

    /// <summary>The JSON reader's complaint is what this whole change exists to stop logging.</summary>
    [Fact]
    public void TheJsonReadersComplaintIsNotWhatGetsLogged()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        harness.Service.RefreshToken();

        Assert.DoesNotContain(harness.AllMessages, m => m.Contains("invalid start of a value"));
        Assert.DoesNotContain(harness.AllMessages, m => m.Contains("BytePositionInLine"));
        Assert.DoesNotContain(harness.AllMessages, m => m.Contains("Unknown error"));
    }

    /// <summary>
    /// The loop itself. Ten notification ticks against a server answering HTML must produce one
    /// request and one Error line, not ten of each.
    /// </summary>
    [Fact]
    public void TenNotificationTicksAgainstAFailingServerSendOneRequestAndLogOneError()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        for (var tick = 0; tick < 10; tick++)
        {
            Assert.Equal(-1, harness.Service.RefreshToken());
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
        }

        // Three of the ten ticks fall inside the 30-second first delay, so tick 3 and tick 6 do go
        // out — what must not happen is one request per tick.
        Assert.InRange(harness.RequestsTo(TokenPath), 1, 3);
        Assert.Single(harness.Errors);
    }

    /// <summary>
    /// With the clock frozen — the case the log file actually shows, where the ticks arrive faster
    /// than the backoff — nothing after the first attempt reaches the network at all.
    /// </summary>
    [Fact]
    public void RepeatedRefreshesInsideTheBackoffWindowNeverReachTheServer()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        for (var i = 0; i < 6; i++) Assert.Equal(-1, harness.Service.RefreshToken());

        Assert.Equal(1, harness.RequestsTo(TokenPath));
        Assert.Single(harness.Errors);
    }

    /// <summary>
    /// A suppressed refresh must not report success: <see cref="RestService.GetClient"/> reads a 0
    /// as "use the token the refresh produced", and there is no new token.
    /// </summary>
    [Fact]
    public void ASuppressedRefreshDoesNotReportSuccess()
    {
        using var harness = new Harness();
        harness.Backend.OnStatus(Method.Get, TokenPath, HttpStatusCode.Forbidden);

        Assert.Equal(1, harness.Service.RefreshToken());
        Assert.Equal(1, harness.Service.RefreshToken());
        Assert.Equal(1, harness.RequestsTo(TokenPath));
    }

    /// <summary>The failure is still reported, just quietly, so a debug log keeps every occurrence.</summary>
    [Fact]
    public void ASuppressedRefreshIsStillRecordedAtDebugLevel()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        harness.Service.RefreshToken();
        harness.Service.RefreshToken();

        Assert.Single(harness.Errors);
        Assert.Contains(harness.Messages(LogEventLevel.Debug), m => m.Contains("Not refreshing the session token"));
    }

    /// <summary>Once the window has elapsed the refresh is tried again — the backoff is a delay, not a stop.</summary>
    [Fact]
    public void TheRefreshIsRetriedOnceTheWindowHasElapsed()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        harness.Service.RefreshToken();
        harness.Clock.Advance(TokenRefreshBackoff.FirstDelay);
        harness.Service.RefreshToken();

        Assert.Equal(2, harness.RequestsTo(TokenPath));

        // The second attempt is the same failure inside the report interval, so it is not another
        // Error line.
        Assert.Single(harness.Errors);
    }

    /// <summary>
    /// A persistent failure is re-reported at a much lower rate, so the operator is told again
    /// without the log becoming 4,537 lines.
    /// </summary>
    [Fact]
    public void APersistentFailureIsReportedAgainAfterTheReportInterval()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        harness.Service.RefreshToken();
        harness.Clock.Advance(TokenRefreshBackoff.ReportInterval + TimeSpan.FromMinutes(1));
        harness.Service.RefreshToken();

        Assert.Equal(2, harness.Errors.Count);
    }

    /// <summary>
    /// A refusal the API itself sends is described with its status and its body. On the throwing
    /// client this branch was unreachable, which is why the refresh asks for a reporting one.
    /// </summary>
    [Fact]
    public void AServerRefusalIsDescribedWithItsStatusAndBody()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath,
            "{\"title\":\"client registration is not approved\"}", "application/json",
            HttpStatusCode.Forbidden);

        Assert.Equal(1, harness.Service.RefreshToken());

        var reported = Assert.Single(harness.Errors);
        Assert.Contains("HTTP 403 (Forbidden)", reported);
        Assert.Contains("client registration is not approved", reported);
        Assert.DoesNotContain("Request failed with status code", reported);
    }

    /// <summary>A distinct failure is new information: it is reported immediately, not collapsed into the previous one.</summary>
    [Fact]
    public void ADifferentFailureIsReportedImmediately()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");

        harness.Service.RefreshToken();

        harness.Backend.OnStatus(Method.Get, TokenPath, HttpStatusCode.BadGateway);
        harness.Clock.Advance(TokenRefreshBackoff.FirstDelay);
        harness.Service.RefreshToken();

        Assert.Equal(2, harness.Errors.Count);
        Assert.Contains("HTTP 502 (BadGateway)", harness.Errors[1]);
    }

    /// <summary>A transport failure says so rather than inventing a status code.</summary>
    [Fact]
    public void ATransportFailureIsDescribedAsOne()
    {
        using var harness = new Harness();
        harness.Backend.OnTransportFailure(Method.Get, TokenPath,
            new System.Net.Http.HttpRequestException("No such host is known. (netrisk.example:5443)"));

        Assert.Equal(-1, harness.Service.RefreshToken());

        var reported = Assert.Single(harness.Errors);
        Assert.Contains("did not complete", reported);
        Assert.Contains("No such host is known.", reported);
    }

    /// <summary>
    /// The success path is unchanged: the token is stored and the credential updated. Asserted here
    /// because the backoff sits in front of it.
    /// </summary>
    [Fact]
    public void ASuccessfulRefreshStoresTheNewToken()
    {
        using var harness = new Harness();
        harness.Backend
            .On(Method.Get, TokenPath, JsonSerializer.Serialize("renewed-token"))
            .On(Method.Get, UserInfoPath, "{}");

        Assert.Equal(0, harness.Service.RefreshToken());

        Assert.Equal("renewed-token", harness.Service.AuthenticationCredential.JWTToken);
        Assert.True(harness.Service.IsAuthenticated);
        harness.Configuration.Received().SetConfigurationValue("AuthToken", "renewed-token");
        Assert.Empty(harness.Errors);
    }

    /// <summary>
    /// A server that comes back clears the backoff, so the next failure is reported at once instead
    /// of inheriting the previous outage's silence.
    /// </summary>
    [Fact]
    public void ASuccessClearsTheBackoff()
    {
        using var harness = new Harness();
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");
        harness.Backend.On(Method.Get, UserInfoPath, "{}");

        harness.Service.RefreshToken();
        harness.Clock.Advance(TokenRefreshBackoff.FirstDelay);

        harness.Backend.On(Method.Get, TokenPath, JsonSerializer.Serialize("renewed-token"));
        Assert.Equal(0, harness.Service.RefreshToken());

        // No clock advance: a refresh right after a success must go out.
        harness.Backend.OnContent(Method.Get, TokenPath, ProxyErrorPage, "text/html");
        Assert.Equal(-1, harness.Service.RefreshToken());

        Assert.Equal(2, harness.Errors.Count);
    }

    /// <summary>
    /// The profile fetch that follows a successful refresh is not part of the refresh.
    ///
    /// It runs on the throwing client and reaches a different endpoint, so it can fail on its own —
    /// and if that failure were recorded against the backoff, a perfectly good new token would put
    /// the client into a 30-second suppression and replay a failure to the next caller.
    /// </summary>
    [Fact]
    public void AFailingProfileFetchDoesNotTurnASuccessfulRefreshIntoAFailure()
    {
        using var harness = new Harness();

        // UserInfoPath is left unstubbed, so the backend answers it 501.
        harness.Backend.On(Method.Get, TokenPath, JsonSerializer.Serialize("renewed-token"));

        Assert.Equal(0, harness.Service.RefreshToken());
        Assert.Equal("renewed-token", harness.Service.AuthenticationCredential.JWTToken);
        Assert.Empty(harness.Errors);

        // No clock advance: the backoff must not have engaged.
        Assert.Equal(0, harness.Service.RefreshToken());
        Assert.Equal(2, harness.RequestsTo(TokenPath));
    }

    /// <summary>
    /// A 200 whose body is JSON but empty is not a token. It used to be stored as one, which put an
    /// empty string in the Authorization header on every later call.
    /// </summary>
    [Fact]
    public void AnEmptyTokenIsNotAccepted()
    {
        using var harness = new Harness();
        harness.Backend.On(Method.Get, TokenPath, JsonSerializer.Serialize(""));

        Assert.Equal(-1, harness.Service.RefreshToken());
        Assert.False(harness.Service.IsAuthenticated);
        harness.Configuration.DidNotReceive().SetConfigurationValue("AuthToken", Arg.Any<string>());
        Assert.Single(harness.Errors);
    }

    /// <summary>
    /// The real service over a stub backend, with a controllable clock and a readable log.
    ///
    /// <c>ThrowsOnErrorResponses</c> is on, so the stub models the production client: a non-2xx is
    /// raised before the caller can read it *unless* the caller asked for an error-reporting client.
    /// That is what makes the status assertions above meaningful rather than a test-only path.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private static readonly object AccessorGate = new();

        private readonly CapturingSink _sink = new();

        public StubRestBackend Backend { get; }
        public AuthenticationRestService Service { get; }
        public IMutableConfigurationService Configuration { get; }
        public TestClock Clock { get; } = new();

        public Harness()
        {
            EnsureServiceProviderAccessor();

            Backend = new StubRestBackend { ThrowsOnErrorResponses = true };
            Configuration = Substitute.For<IMutableConfigurationService>();

            var environment = Substitute.For<IEnvironmentService>();
            environment.DeviceID.Returns("B0804F34");

            Service = new AuthenticationRestService(
                Substitute.For<IRegistrationService>(),
                Backend,
                Configuration,
                environment)
            {
                RefreshBackoff = new TokenRefreshBackoff(() => Clock.Now),
                LoggerOverride = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.Sink(_sink)
                    .CreateLogger()
            };
        }

        public IReadOnlyList<string> Errors => Messages(LogEventLevel.Error);

        public IReadOnlyList<string> AllMessages =>
            _sink.Events.Select(Render).ToList();

        public IReadOnlyList<string> Messages(LogEventLevel level) =>
            _sink.Events.Where(e => e.Level == level).Select(Render).ToList();

        public int RequestsTo(string path) => Backend.Requests.Count(r => r.Path == path);

        private static string Render(LogEvent e) => e.RenderMessage();

        /// <summary>
        /// <c>ServiceBase</c> resolves a Serilog logger from the process-wide
        /// <see cref="ServiceProviderAccessor"/> in a field initializer, so constructing the service
        /// needs one to exist. The log this test reads is <c>LoggerOverride</c>, not that one —
        /// the accessor is shared with every other test class and cannot be relied on.
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

        public void Dispose() => Backend.Dispose();
    }

    private sealed class TestClock
    {
        public DateTime Now { get; private set; } = new(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan by) => Now += by;
    }

    /// <summary>Collects what the service logged, which is the thing under test.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = new();

        public IReadOnlyList<LogEvent> Events
        {
            get { lock (_events) return _events.ToList(); }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events) _events.Add(logEvent);
        }
    }
}
