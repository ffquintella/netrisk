using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Model.Authentication;
using RestSharp;
using RestSharp.Authenticators;
using Serilog;
using System.Text.Json;
using ClientServices.Http;
using ClientServices.Interfaces;
using Model.FaceID;

namespace ClientServices.Services;

public class AuthenticationRestService: RestServiceBase, IAuthenticationService
{
    //public bool IsAuthenticated { get; set; } = false;

    private bool _isAuthenticated = false;
    public bool IsAuthenticated
    {
        get
        {
            return _isAuthenticated;
        }

        set
        {
            _isAuthenticated = value;
        }
    }
    

    public bool IsFaceAuthenticated
    {
        get
        {
            if(_faceToken != null && _faceTokenExpiration > DateTime.Now)
            {
                return true;
            }
            else
            {
                _faceToken = null;
                _faceTokenExpiration = DateTime.MinValue;
                return false;
            }

        }
        
    }
    
    private FaceToken? _faceToken = null;
    private DateTime _faceTokenExpiration = DateTime.MinValue;

    private IRegistrationService _registrationService;
    private IMutableConfigurationService _mutableConfigurationService;
    private IEnvironmentService _environmentService;
    public AuthenticationCredential AuthenticationCredential { get; set; }
    public AuthenticatedUserInfo? AuthenticatedUserInfo { get; set; }
    private bool _authenticationVerified = false;

    public AuthenticationRestService( 
        IRegistrationService registrationService,
        IRestService restService,
        IMutableConfigurationService mutableConfigurationService,
        IEnvironmentService environmentService): base(restService)
    {
        AuthenticationCredential = new AuthenticationCredential
        {
            AuthenticationType = AuthenticationType.None
        };
        
        _registrationService = registrationService;
        _mutableConfigurationService = mutableConfigurationService;
        _environmentService = environmentService;
    }

    public async Task<bool> TryAuthenticateAsync()
    {
        if(_authenticationVerified) return true;
        
        Logger.Debug("Starting authentication procedures...");
        var isAuth = await _mutableConfigurationService.GetConfigurationValueAsync("IsAuthenticate");
        var token = await _mutableConfigurationService.GetConfigurationValueAsync("AuthToken");

        if (isAuth != "true" || !CheckTokenValidTime(token!)) return false;
        
        //Check connection 
        AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
        AuthenticationCredential.JWTToken = token;
        if (AuthenticationCredential.JWTToken == null) return false;

        try
        {

            using var client = RestService.GetClient(new JwtAuthenticator(this.AuthenticationCredential.JWTToken!));
            client.AddDefaultHeader("ClientId", _environmentService.DeviceID);
            var request = new RestRequest("/Authentication/AuthenticatedUserInfo");

            var response = await client.GetAsync<AuthenticatedUserInfo>(request);
            if (response != null)
            {
                Logger.Information("User {UserAccount} is logged", response.UserAccount);
                
                IsAuthenticated = true;
                AuthenticatedUserInfo = response;
                _authenticationVerified = true;
                NotifyAuthenticationSucceeded();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
    public bool TryAuthenticate()
    {
        if(_authenticationVerified) return true;
        
        Logger.Debug("Starting authentication procedures...");
        var isAuth = _mutableConfigurationService.GetConfigurationValue("IsAuthenticate");
        var token = _mutableConfigurationService.GetConfigurationValue("AuthToken");

        if (isAuth != "true" || !CheckTokenValidTime(token!)) return false;
        
        //Check connection 
        AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
        AuthenticationCredential.JWTToken = token;
        if (AuthenticationCredential.JWTToken == null) return false;

        try
        {
            using var client = RestService.GetClient(new JwtAuthenticator(this.AuthenticationCredential.JWTToken!));
            client.AddDefaultHeader("ClientId", _environmentService.DeviceID);
            var request = new RestRequest("/Authentication/AuthenticatedUserInfo");

            var response = client.Get<AuthenticatedUserInfo>(request);
            if (response != null)
            {
                Logger.Information("User {UserAccount} is logged", response.UserAccount);

                IsAuthenticated = true;
                //AuthenticatedUserInfo = _mutableConfigurationService.GetAuthenticatedUser()!;
                AuthenticatedUserInfo = response;
                _authenticationVerified = true;
                NotifyAuthenticationSucceeded();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }


    }

    public bool CheckTokenValidTime(string token, int minutesToExpire = 0)
    {
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

        if (jwtToken == null)
        {
            Logger.Error("Invalid token format");
            return false;
        }

        if (jwtToken.ValidTo > DateTime.UtcNow.AddMinutes(minutesToExpire))
        {
            //Logger.Debug("Token is valid");
            return true;
        }
        else
        {
            Logger.Debug("Token is expired");
            return false;
        }
        
    }

    /// <summary>The endpoint that mints and renews a session token.</summary>
    private const string TokenPath = "/Authentication/GetToken";

    /// <summary>
    /// The rate limiter on <see cref="RefreshToken"/>. Init-only so a test can supply one with a
    /// controllable clock; the container gets the default.
    /// </summary>
    internal TokenRefreshBackoff RefreshBackoff { get; init; } = new();

    /// <summary>
    /// Where a failed refresh reports itself.
    ///
    /// A test seam, for the same reason <see cref="RestService.AuthenticationServiceOverride"/> is
    /// one: <see cref="ServiceBase.Logger"/> is resolved from the process-wide
    /// <see cref="ServiceProviderAccessor"/> at construction, so it is whichever logger some other
    /// test class registered last and a test cannot read back what was written to it.
    /// </summary>
    internal Serilog.ILogger? LoggerOverride { get; init; }

    private Serilog.ILogger RefreshLogger => LoggerOverride ?? Logger;

    /// <summary>
    /// Renews the session token, unless a recent refresh already failed.
    ///
    /// Called from <see cref="RestService.GetClient"/> on every REST call whose token is inside its
    /// renewal window — which, with the notification timer in <c>NavigationBarViewModel</c>, is every
    /// ten seconds for as long as the client is open. That is why a failure here has to be both rate
    /// limited and described properly; see <see cref="TokenRefreshBackoff"/> and
    /// <see cref="ServerResponseDescription"/> for the 4,537-line log file that says why.
    /// </summary>
    /// <returns>0 on success, 1 when the server refused, -1 otherwise. A suppressed attempt replays
    /// the last attempt's result, which is never 0 while the backoff is active.</returns>
    public int RefreshToken()
    {
        if (!RefreshBackoff.ShouldAttempt())
        {
            RefreshLogger.Debug(
                "Not refreshing the session token: {Failures} consecutive failures, next attempt in {RetryAfter}",
                RefreshBackoff.ConsecutiveFailures, RefreshBackoff.RetryAfter);
            return RefreshBackoff.LastResult;
        }

        // reportErrorResponses: a refresh that cannot read the server's answer cannot describe it.
        // On the throwing client every non-2xx arrived as "Request failed with status code X" with no
        // body and no status to branch on — see RestService's comment on _reportingOptions.
        using var client = RestService.GetClient(ignoreTimeVerification: true, reportErrorResponses: true);
        var request = new RestRequest(TokenPath);

        RestResponse? response = null;
        var renewed = false;

        try
        {
            // Execute, not Get: RestSharp's Get/Post/... extensions call ThrowIfError() themselves,
            // *regardless* of ThrowOnAnyError, so asking for the reporting client and then calling
            // Get() still raises HttpRequestException("Request failed with status code X") before the
            // status or the body can be read. Only the Execute family honours the option.
            response = client.Execute(request);

            // The markup check is what stops the JSON reader from being the one to report an HTML
            // page: its complaint is about byte 0 of a body it will not name, ours names the
            // endpoint, the status and the proxy that is probably answering.
            if (response is { IsSuccessful: true, StatusCode: HttpStatusCode.OK }
                && ServerResponseDescription.MarkupKindOf(response.Content, response.ContentType) == null)
            {
                var token = JsonSerializer.Deserialize<string>(response.Content!);

                if (!string.IsNullOrWhiteSpace(token))
                {
                    _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                    _mutableConfigurationService.SetConfigurationValue("AuthToken", token);
                    _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                    AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                    AuthenticationCredential.JWTToken = token;
                    IsAuthenticated = true;
                    RefreshBackoff.RecordSuccess();
                    renewed = true;
                }
            }

            if (!renewed)
            {
                var refused = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
                return ReportRefreshFailure(response, null, refused ? 1 : -1);
            }
        }
        catch (Exception ex)
        {
            return ReportRefreshFailure(response, ex, -1);
        }

        // Outside the catch: the token has been renewed and stored by this point, and a failure
        // fetching the user's profile is not a failed refresh. Reporting it as one would put the
        // backoff into a failing state — and replay a failure to the next caller — over a token that
        // is perfectly good. GetAuthenticatedUserInfo logs and swallows its own problems.
        GetAuthenticatedUserInfo();
        return 0;
    }

    /// <summary>
    /// Records a failed refresh against the backoff and logs it — loudly the first time, quietly
    /// while the same failure keeps repeating.
    /// </summary>
    private int ReportRefreshFailure(RestResponse? response, Exception? error, int result)
    {
        var problem = response == null && error != null
            ? ServerResponseDescription.DescribeTransportFailure(TokenPath, error.Message)
            : ServerResponseDescription.Describe(TokenPath, response, error);

        var failure = RefreshBackoff.RecordFailure(problem.Key, result);

        if (failure.ShouldReport)
            RefreshLogger.Error(
                "Could not refresh the session token: {Problem}. Retrying in {RetryAfter}",
                problem.Message, failure.RetryAfter);
        else
            RefreshLogger.Debug(
                "Could not refresh the session token ({Failures} consecutive failures): {Problem}. Retrying in {RetryAfter}",
                failure.ConsecutiveFailures, problem.Message, failure.RetryAfter);

        return result;
    }

    /// <summary>
    /// Asks the server to mint the identifier for a SAML sign-in (Track 7 finding NR-2026-001).
    ///
    /// The client used to generate this itself and put it in the browser URL, which meant anybody
    /// could choose one, hand a victim the link and redeem the victim's completed sign-in. The server
    /// mints it now, only for an approved client registration, and only that registration can collect
    /// the resulting token.
    /// </summary>
    /// <returns>The request id, or null when the server refused — the caller must not fall back to
    /// generating one locally, because that is exactly the removed behaviour.</returns>
    public async Task<string?> CreateSamlRequestIdAsync()
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Authentication/SAMLRequestId");
        request.AddHeader("ClientId", _environmentService.DeviceID);

        try
        {
            var response = await client.GetAsync(request);

            if (response is { IsSuccessful: true, StatusCode: HttpStatusCode.OK }
                && !string.IsNullOrWhiteSpace(response.Content))
            {
                return JsonSerializer.Deserialize<string>(response.Content!);
            }

            Logger.Error("The server refused to start a SAML sign-in: {Code}", response.StatusCode);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not start a SAML sign-in: {Message}", ex.Message);
        }

        return null;
    }

    public bool CheckSamlAuthentication(string requestId)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Authentication/AppSAMLToken");
        request.AddParameter("requestId", requestId);
        // The server hands a SAML session token only to the client registration that asked for the
        // sign-in (Track 7 finding NR-2026-001). This request is unauthenticated by nature — there is
        // no session yet — so the ClientId header is the only thing identifying us.
        request.AddHeader("ClientId", _environmentService.DeviceID);
        try
        {
            var response = client.Get(request);

            if (response is { IsSuccessful: true, StatusCode: HttpStatusCode.OK })
            {
                if (response.Content == "Not accepted")
                {
                    return false;
                }
                else
                {
                    var token = JsonSerializer.Deserialize<string>(response.Content!);

                    _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                    _mutableConfigurationService.SetConfigurationValue("AuthToken", token!);
                    _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                    AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                    AuthenticationCredential.JWTToken = token;
                    IsAuthenticated = true;
                    NotifyAuthenticationSucceeded();
                    return true;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            if(ex.StatusCode != HttpStatusCode.Unauthorized) Logger.Error("Unknown error {Message}", ex.Message);
        }

        return false;
    }

    public async Task<bool> CheckSamlAuthenticationAsync(string requestId)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Authentication/AppSAMLToken");
        request.AddParameter("requestId", requestId);
        request.AddHeader("ClientId", _environmentService.DeviceID);
        try
        {
            var response = await client.GetAsync(request);

            if (response is { IsSuccessful: true, StatusCode: HttpStatusCode.OK })
            {
                if (response.Content == "Not accepted")
                {
                    return false;
                }
                else
                {
                    var token = JsonSerializer.Deserialize<string>(response.Content!);

                    _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                    _mutableConfigurationService.SetConfigurationValue("AuthToken", token!);
                    _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                    AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                    AuthenticationCredential.JWTToken = token;
                    IsAuthenticated = true;
                    await GetAuthenticatedUserInfoAsync();
                    NotifyAuthenticationSucceeded();
                    return true;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            if(ex.StatusCode != HttpStatusCode.Unauthorized) Logger.Error("Unknown error {Message}", ex.Message);
        }

        return false;
    }

    public void DiscardAuthenticationToken()
    {
        _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "false");
        _mutableConfigurationService.RemoveConfigurationValue("AuthToken");
        _mutableConfigurationService.RemoveConfigurationValue("AuthTokenTime");
    }
    public int DoServerAuthentication(string user, string password)
    {
        using var client = RestService.GetClient(new HttpBasicAuthenticator(user, password));
        
        var request = new RestRequest("/Authentication/GetToken");
        
        request.AddHeader("ClientId", _environmentService.DeviceID);
        
        try
        {
            var response = client.Get(request);

            if (response is { IsSuccessful: true, StatusCode: HttpStatusCode.OK })
            {
                var token = JsonSerializer.Deserialize<string>(response.Content!);

                _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                _mutableConfigurationService.SetConfigurationValue("AuthToken", token!);
                _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                AuthenticationCredential.JWTToken = token;
                IsAuthenticated = true;
                GetAuthenticatedUserInfo();
                Logger.Information("User {UserName} authenticated", user);
                //NotifyAuthenticationSucceeded();
                return 0;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Error("Authentication Error response code: {Code}", response.StatusCode);
                return 1;
            }
            
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error {Message}", ex.Message);
            
        }
        
        return -1;
    }

    public async Task<int> DoServerAuthenticationAsync(string user, string password)
    {
        using var client = RestService.GetClient(new HttpBasicAuthenticator(user, password));
        
        var request = new RestRequest("/Authentication/GetToken");
        
        request.AddHeader("ClientId", _environmentService.DeviceID);
        
        try
        {
            var response = await client.GetAsync(request);

            if (response is { IsSuccessful: true, StatusCode: HttpStatusCode.OK })
            {
                var token = JsonSerializer.Deserialize<string>(response.Content!);

                _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                _mutableConfigurationService.SetConfigurationValue("AuthToken", token!);
                _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                AuthenticationCredential.JWTToken = token;
                IsAuthenticated = true;
                GetAuthenticatedUserInfo();
                Logger.Information("User {UserName} authenticated", user);
                //NotifyAuthenticationSucceeded();
                return 0;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Error("Authentication Error response code: {Code}", response.StatusCode);
                return 1;
            }
            
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error {Message}", ex.Message);
            
        }
        
        return -1;
    }

    public int GetAuthenticatedUserInfo()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Authentication/AuthenticatedUserInfo");
        
        try
        {
            var response = client.Get<AuthenticatedUserInfo>(request);

            if (response != null)
            {
                AuthenticatedUserInfo = response;
                Logger.Information("User {UserAccount} is logged", response.UserAccount);
                _mutableConfigurationService.SaveAuthenticatedUser(AuthenticatedUserInfo);
                return 0;
            }

            return 1;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting user info {ExMessage}", ex.Message);
        }
        
        return -1;
    }

    public async Task<int> GetAuthenticatedUserInfoAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Authentication/AuthenticatedUserInfo");
        
        try
        {
            var response = await client.GetAsync<AuthenticatedUserInfo>(request);

            if (response != null)
            {
                AuthenticatedUserInfo = response;
                Logger.Information("User {UserAccount} is logged", response.UserAccount);
                _mutableConfigurationService.SaveAuthenticatedUser(AuthenticatedUserInfo);
                return 0;
            }

            return 1;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting user info {ExMessage}", ex.Message);
        }
        
        return -1;
    }

    public List<AuthenticationMethod> GetAuthenticationMethods()
    {
        var defaultResponse = new List<AuthenticationMethod>
        {
            new AuthenticationMethod
            {
                Name= "Error",
                Type = "Basic"
            }
        };

        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Authentication/AuthenticationMethods");
        try
        {
            var response = client.Get<List<AuthenticationMethod>>(request);

            if (response != null)
            {
                Logger.Debug("Listing authentication methods");
                return response;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error {Message}", ex.Message);
            
        }
        return defaultResponse;
    
    }

    public async Task RegisterFaceAuthenticationTokenAsync(FaceToken token)
    {
        _faceToken = token;
        //_mutableConfigurationService.SetConfigurationValue("FaceAuthenticated", "true");
        _faceTokenExpiration = DateTime.Now.AddMinutes(10);

    }

    public FaceToken? GetFaceToken()
    {
        if(!IsFaceAuthenticated) return null;
        return _faceToken;
    }

    public void Logout()
    {
        _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "false");
        _mutableConfigurationService.RemoveConfigurationValue("AuthToken");
        _mutableConfigurationService.RemoveConfigurationValue("AuthTokenTime");

        IsAuthenticated = false;
    }
    
    public void NotifyAuthenticationSucceeded()
    {
        if(AuthenticationSucceeded != null) AuthenticationSucceeded(this, new EventArgs());
    }
    public event EventHandler? AuthenticationSucceeded;
}