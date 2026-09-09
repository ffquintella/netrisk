using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Model.Configuration;
using NSubstitute;
using RestSharp;
using Serilog;
using Serilog.Extensions.Logging;
using Xunit;
using ILogger = Serilog.ILogger;

namespace ClientServices.Tests.Services;

/// <summary>
/// The wiring half of the 2026-09-09 "error saving here" report: that a client handed out by
/// <see cref="RestService"/> really does stop at the API's authentication challenge instead of
/// following it into the identity provider.
///
/// <see cref="Http.AuthChallengeHandlerTest"/> covers the translation itself. This one goes over a
/// loopback listener answering the way the homolog API does — <c>302</c> with a
/// <c>Location</c> of <c>https://minhaconta.fgv.br/iamapps/ssologin/…</c> and an HTML body — because
/// the defect was not in the translation but in the client never being asked to make it:
/// <c>RestClientOptions</c> follows redirects by default, so nothing downstream could tell an
/// expired session from a refused write.
/// </summary>
public class RestServiceAuthChallengeTest
{
    /// <summary>
    /// The whole report in one assertion: the write that produced "Request failed with status code
    /// BadRequest" now fails as Unauthorized, which is what it always was.
    ///
    /// The listener stands in for both halves of the exchange — it challenges with a 302 whose
    /// Location is a second loopback path that answers a JSON write the way the identity provider
    /// does, with 400 and a login page. So a client that started following redirects again would
    /// fail this test with the original symptom rather than by reaching out to the network.
    /// </summary>
    [Fact]
    public async Task AChallengedWriteIsReportedAsUnauthorizedRatherThanBadRequest()
    {
        using var harness = new Harness();
        var client = harness.RestService.GetClient(reportErrorResponses: true);

        var request = new RestRequest("/IrpTemplates/1/Tasks/2");
        request.AddJsonBody(new { title = "sdfadsfasdf" });

        var response = await client.ExecuteAsync(request, Method.Put);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("<", response.Content ?? "");
    }

    /// <summary>
    /// A challenged read used to deserialize the login page, which is the
    /// "'&lt;' is an invalid start of a value" in the operator's log.
    /// </summary>
    [Fact]
    public async Task AChallengedReadDoesNotHandBackTheLoginPage()
    {
        using var harness = new Harness();
        var client = harness.RestService.GetClient(reportErrorResponses: true);

        var response = await client.ExecuteAsync(new RestRequest("/IrpTemplates"), Method.Get);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Minha Conta", response.Content ?? "");
    }

    /// <summary>
    /// The token that earned the challenge is dropped. Most services in this assembly have no 401
    /// branch of their own, so before this the stale token was presented again on every later call.
    /// </summary>
    [Fact]
    public async Task AChallengeDiscardsTheStaleToken()
    {
        using var harness = new Harness();
        var client = harness.RestService.GetClient(reportErrorResponses: true);

        await client.ExecuteAsync(new RestRequest("/IrpTemplates"), Method.Get);

        harness.Authentication.Received(1).DiscardAuthenticationToken();
    }

    /// <summary>
    /// A <see cref="RestService"/> pointed at a loopback listener that challenges every request the
    /// way the API's SAML policy scheme does.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private const string LoginPage =
            "<!DOCTYPE html><html><head><title>Minha Conta FGV</title></head><body></body></html>";

        private const string IdentityProviderPath = "/iamapps/ssologin/custom_saml_app/77510b7b";

        private readonly HttpListener _listener;
        private readonly string _baseUrl;
        private readonly CancellationTokenSource _stopping = new();

        public IAuthenticationService Authentication { get; }
        public RestService RestService { get; }

        private static readonly object AccessorGate = new();

        public Harness()
        {
            EnsureServiceProviderAccessor();

            var port = FreePort();
            _baseUrl = $"http://127.0.0.1:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{_baseUrl}/");
            _listener.Start();
            _ = Task.Run(ChallengeEverything);

            // Not authenticated, so no authenticator is attached and the test measures the redirect
            // policy rather than the token-renewal path (RestServiceTokenRenewalTest covers that).
            Authentication = Substitute.For<IAuthenticationService>();
            Authentication.IsAuthenticated.Returns(false);

            var environment = Substitute.For<IEnvironmentService>();
            environment.DeviceID.Returns("B0804F34");

            var configuration = Substitute.For<IMutableConfigurationService>();
            configuration.GetConfigurationValue("Server").Returns(_baseUrl);

            RestService = new RestService(
                new SerilogLoggerFactory(new LoggerConfiguration().CreateLogger()),
                new ServerConfiguration(),
                environment,
                configuration)
            {
                AuthenticationServiceOverride = Authentication
            };
        }

        private async Task ChallengeEverything()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return; // the listener was closed
                }

                if (context.Request.Url!.AbsolutePath == IdentityProviderPath)
                {
                    // What minhaconta.fgv.br answers a JSON PUT re-sent to its login URL, verified
                    // on 2026-09-09: 400, with the login page as the body. This is the "BadRequest"
                    // the operator was shown for a save the API never saw.
                    await Answer(context, HttpStatusCode.BadRequest, "text/html", LoginPage);
                    continue;
                }

                context.Response.RedirectLocation = $"{_baseUrl}{IdentityProviderPath}";
                await Answer(context, HttpStatusCode.Found, "text/html", LoginPage);
            }
        }

        private async Task Answer(HttpListenerContext context, HttpStatusCode status,
            string contentType, string body)
        {
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = contentType;

            var bytes = Encoding.UTF8.GetBytes(body);
            await context.Response.OutputStream.WriteAsync(bytes, _stopping.Token);
            context.Response.Close();
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>
        /// <c>ServiceBase</c> resolves its Serilog logger from the process-wide
        /// <see cref="ServiceProviderAccessor"/>, so one has to be there. Only filled in when no
        /// other test class has set one already.
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

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Close();
            _stopping.Dispose();
        }
    }
}
