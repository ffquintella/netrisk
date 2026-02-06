using System;
using System.Net;
using Model.Configuration;
using ClientServices.Interfaces;
using Model.Authentication;
using Microsoft.Extensions.Logging;
using Polly;
using ReliableRestClient;
using ReliableRestClient.Exceptions;
using RestSharp;
using RestSharp.Authenticators;

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

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _authenticationService = ServiceProviderAccessor.GetRequiredService<IAuthenticationService>();
        var url = _mutableConfigurationService.GetConfigurationValue("Server");

        _options = new RestClientOptions(url!)
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true, //TODO: Remove this line
            ThrowOnAnyError = true,
            Timeout = TimeSpan.FromHours(1)
        };
    }

    public RestClient GetClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false)
    {
        Initialize();

        if (autenticator != null)
        {
            _options!.Authenticator = autenticator;
        }

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            var useProxy = Environment.GetEnvironmentVariable("USE_PROXY");
            if (useProxy != null && useProxy == "true")
            {
                var proxy = WebRequest.DefaultWebProxy;

                if (proxy != null)
                    _options!.Proxy = new WebProxy("http://127.0.0.1:8888", false);
            }
        }

        if (_authenticationService == null)
        {
            var client = new RestClient(_options!);
            return client;
        }
        if (_authenticationService!.IsAuthenticated)
        {
            if (_authenticationService.AuthenticationCredential == null)
            {
                return new RestClient(_options!);
            }

            if (_authenticationService.AuthenticationCredential.AuthenticationType == AuthenticationType.JWT)
            {
                var jwtToken = _authenticationService.AuthenticationCredential.JWTToken;
                if (string.IsNullOrWhiteSpace(jwtToken))
                {
                    return new RestClient(_options!);
                }

                if (!ignoreTimeVerification && !_authenticationService.CheckTokenValidTime(jwtToken, 60 * 5))
                {
                    _authenticationService.RefreshToken();
                }
                _options!.Authenticator = new JwtAuthenticator(jwtToken);
                var client = new RestClient(_options!);
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
            var client = new RestClient(_options!);
            return client;
        }
    }

    public IRestClient GetReliableClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false)
    {
        var retryPolicy = Policy
            .Handle<RestServerSideException>()
            .WaitAndRetryAsync(10, retryAttempt => TimeSpan.FromMilliseconds(1000 * Math.Pow(2, retryAttempt)));

        var reliableClient = new ReliableRestClientWrapper(GetClient(autenticator, ignoreTimeVerification), retryPolicy);
        return reliableClient;
    }
}
