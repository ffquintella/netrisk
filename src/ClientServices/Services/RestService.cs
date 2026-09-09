using System;
using System.Net;
using Model.Configuration;
using ClientServices.Http;
using ClientServices.Interfaces;
using Model.Authentication;
using Microsoft.Extensions.Logging;
using Polly;
using ReliableRestClient;
using ReliableRestClient.Exceptions;
using RestSharp;
using RestSharp.Authenticators;
using Tools.Security;

namespace ClientServices.Services;

public class RestService : ServiceBase, IRestService
{
    private IAuthenticationService? _authenticationService;
    private ILogger<RestService> _logger;
    private ServerConfiguration _serverConfiguration;
    private bool _initialized = false;
    private IEnvironmentService _environmentService;
    private IMutableConfigurationService _mutableConfigurationService;

    private RestClientOptions? _options;

    /// <summary>
    /// The same options with <c>ThrowOnAnyError</c> off, for callers that need to read a refusal.
    ///
    /// <c>ThrowOnAnyError</c> makes RestSharp raise <see cref="System.Net.Http.HttpRequestException"/>
    /// for any non-2xx *before* the caller can look at the response, and that exception carries only
    /// "Request failed with status code X" — not the body. Every service in this layer that inspects
    /// <c>response.StatusCode</c> to surface the server's explanation is therefore unreachable code on
    /// the throwing client, which is how a 400 naming the invalid parameter reached the operator as
    /// "Error calling /TrendMicro/1". A separate options instance rather than flipping the shared one:
    /// the throwing behaviour is what every other caller in this assembly is written against, and
    /// changing it wholesale is a much larger change than the one that is needed.
    ///
    /// One trap: this option is only honoured by RestSharp's <c>Execute</c> family. The
    /// <c>Get</c>/<c>Post</c>/<c>Put</c>/<c>Delete</c> extension methods call
    /// <c>ResponseThrowExtension.ThrowIfError()</c> themselves, unconditionally, so a caller that
    /// asks for this client and then calls <c>client.Get(request)</c> gets
    /// <c>HttpRequestException("Request failed with status code X")</c> anyway and reads nothing.
    /// A caller that needs the body must call <c>Execute</c>/<c>ExecuteAsync</c>.
    /// </summary>
    private RestClientOptions? _reportingOptions;
    public RestService(ILoggerFactory loggerFactory,
        ServerConfiguration serverConfiguration,
        IEnvironmentService environmentService,
        IMutableConfigurationService mutableConfigurationService
    )
    {
        _logger = loggerFactory.CreateLogger<RestService>();
        _serverConfiguration = serverConfiguration;
        _environmentService = environmentService;
        _mutableConfigurationService = mutableConfigurationService;
    }

    /// <summary>
    /// The authentication service to use instead of the one in <see cref="ServiceProviderAccessor"/>.
    ///
    /// A test seam. <see cref="IAuthenticationService"/> cannot be a constructor dependency here —
    /// <c>AuthenticationRestService</c> takes an <see cref="IRestService"/>, so the container would
    /// see a cycle — and the static accessor is process-wide, which makes it whichever provider a
    /// test class happened to build last.
    /// </summary>
    internal IAuthenticationService? AuthenticationServiceOverride { get; set; }

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _authenticationService = AuthenticationServiceOverride
                                ?? ServiceProviderAccessor.GetRequiredService<IAuthenticationService>();
        var url = _mutableConfigurationService.GetConfigurationValue("Server");

        _options = new RestClientOptions(url!)
        {
            ThrowOnAnyError = true,
            Timeout = TimeSpan.FromHours(1),

            // The API answers an unauthenticated or expired request with a 302 to the identity
            // provider, not a 401. Following it sent every call on to a login page — see
            // AuthChallengeHandler, which turns the challenge back into the 401 this assembly
            // reacts to.
            FollowRedirects = false,
            ConfigureMessageHandler = WrapForAuthChallenges
        };

        _reportingOptions = new RestClientOptions(url!)
        {
            ThrowOnAnyError = false,
            Timeout = TimeSpan.FromHours(1),
            FollowRedirects = false,
            ConfigureMessageHandler = WrapForAuthChallenges
        };

        // Track 7 finding NR-2026-004. This was an unconditional `=> true`, carrying its own
        // "//TODO: Remove this line". Certificate validation is now on unless the installation has
        // explicitly asked for it to be off, and asking for it logs a warning every start-up. A null
        // callback means "use the platform's validation", which is what we want in the normal case —
        // see Tools.Security.ServerCertificatePolicy.
        var certificateCallback = ServerCertificatePolicy.CreateCallback(
            AllowsInvalidCertificate(), message => _logger.LogWarning("{Message}", message));

        if (certificateCallback != null)
        {
            _options.RemoteCertificateValidationCallback = certificateCallback;
            _reportingOptions.RemoteCertificateValidationCallback = certificateCallback;
        }
    }

    /// <summary>
    /// Reports an authentication challenge as a 401 and drops the token that earned it.
    ///
    /// The discard lives here rather than in the handler because this class is what owns the
    /// authentication wiring, and because most services in this assembly have no 401 branch of
    /// their own — leaving the stale token in place meant the next sign-in reused it.
    /// </summary>
    private HttpMessageHandler WrapForAuthChallenges(HttpMessageHandler inner) =>
        new AuthChallengeHandler(inner, () => _authenticationService?.DiscardAuthenticationToken());

    /// <summary>
    /// The installation's opt-in to accepting an unvalidatable server certificate.
    ///
    /// Read from the persisted client configuration first so that it can be changed without an
    /// application-settings edit, then from the bound <see cref="ServerConfiguration"/>. Anything
    /// other than a literal "true" is false — including a missing value, which is the case that has
    /// to default to secure.
    /// </summary>
    private bool AllowsInvalidCertificate() =>
        ServerCertificatePolicy.Resolve(
            _mutableConfigurationService.GetConfigurationValue("AllowInvalidCertificate"),
            _serverConfiguration.AllowInvalidCertificate);

    public RestClient GetClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false,
        bool reportErrorResponses = false)
    {
        Initialize();

        var options = reportErrorResponses ? _reportingOptions : _options;

        if (autenticator != null)
        {
            options!.Authenticator = autenticator;
        }

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            var useProxy = Environment.GetEnvironmentVariable("USE_PROXY");
            if (useProxy != null && useProxy == "true")
            {
                var proxy = WebRequest.DefaultWebProxy;

                if (proxy != null)
                    options!.Proxy = new WebProxy("http://127.0.0.1:8888", false);
            }
        }

        if (_authenticationService == null)
        {
            var client = new RestClient(options!);
            return client;
        }
        if (_authenticationService!.IsAuthenticated)
        {
            if (_authenticationService.AuthenticationCredential == null)
            {
                return new RestClient(options!);
            }

            if (_authenticationService.AuthenticationCredential.AuthenticationType == AuthenticationType.JWT)
            {
                var jwtToken = _authenticationService.AuthenticationCredential.JWTToken;
                if (string.IsNullOrWhiteSpace(jwtToken))
                {
                    return new RestClient(options!);
                }

                // The slack comes from the token's own lifetime (see TokenRenewalPolicy) — a fixed
                // window here was what turned a shortened server-side JWT:Timeout into an endless
                // renewal loop.
                if (!ignoreTimeVerification
                    && !_authenticationService.CheckTokenValidTime(jwtToken, TokenRenewalPolicy.SlackMinutesFor(jwtToken)))
                {
                    if (_authenticationService.RefreshToken() == 0)
                    {
                        // Use what the refresh produced. This request used to go out carrying the
                        // token we had just decided was too old to use.
                        jwtToken = _authenticationService.AuthenticationCredential?.JWTToken ?? jwtToken;
                    }
                }
                options!.Authenticator = new JwtAuthenticator(jwtToken);
                var client = new RestClient(options!);
                client.AddDefaultHeader("ClientId", _environmentService.DeviceID);

                if (_authenticationService.IsFaceAuthenticated)
                {
                    var faceToken = _authenticationService.GetFaceToken();
                    if (faceToken?.Token != null)
                    {
                        client.AddDefaultHeader("FaceId", faceToken.Token);
                    }
                }

                return client;
            }
            throw new NotImplementedException();
        }
        else
        {
            var client = new RestClient(options!);
            return client;
        }
    }

    public IRestClient GetReliableClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false,
        bool reportErrorResponses = false)
    {
        var retryPolicy = Policy
            .Handle<RestServerSideException>()
            .WaitAndRetryAsync(10, retryAttempt => TimeSpan.FromMilliseconds(1000 * Math.Pow(2, retryAttempt)));

        var reliableClient = new ReliableRestClientWrapper(
            GetClient(autenticator, ignoreTimeVerification, reportErrorResponses), retryPolicy);

        return reliableClient;
    }
}
