using Model.Configuration;
using ClientServices.Interfaces;
using Model.Authentication;
using Microsoft.Extensions.Logging;
using Polly;
using ReliableRestClient;
using ReliableRestClient.Exceptions;
using RestSharp;
using RestSharp.Authenticators;
using Splat;
using Locator = Splat.Locator;

namespace ClientServices.Services;

public class RestService: IRestService
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
        _authenticationService = Locator.Current.GetService<IAuthenticationService>();
        //ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        var url = _mutableConfigurationService.GetConfigurationValue("Server");
        //var url = "https://127.0.0.1:5443";
        //_serverConfiguration.Url
        _options = new RestClientOptions(url!) {
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
            ThrowOnAnyError = true,
            MaxTimeout = 30000  // 10 second
        
        };
    }
    
    public RestClient GetClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false)
    {
        Initialize();

        if (autenticator != null)
        {
            _options!.Authenticator = autenticator;
        }
        
        
        if (_authenticationService == null)
        {
            var client = new RestClient(_options!);
            return client;
        }
        if (_authenticationService!.IsAuthenticated)
        {
            if (_authenticationService.AuthenticationCredential.AuthenticationType == AuthenticationType.JWT)
            {
                if(!ignoreTimeVerification && !_authenticationService.CheckTokenValidTime(_authenticationService.AuthenticationCredential.JWTToken!,
                   60 * 5))
                {
                    _authenticationService.RefreshToken();
                }
                _options!.Authenticator =  new JwtAuthenticator(_authenticationService.AuthenticationCredential.JWTToken!);
                var client = new RestClient(_options!);
                client.AddDefaultHeader("ClientId", _environmentService.DeviceID);
                
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
            .WaitAndRetryAsync(10, retryAttempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt))); 
        
        var reliableClient = new ReliableRestClientWrapper(GetClient(autenticator,ignoreTimeVerification), retryPolicy);
        return reliableClient;
    }
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}