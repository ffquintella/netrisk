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
using ClientServices.Interfaces;

namespace ClientServices.Services;

public class AuthenticationService: ServiceBase, IAuthenticationService
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

    
    private IRegistrationService _registrationService;
    private IMutableConfigurationService _mutableConfigurationService;
    private IEnvironmentService _environmentService;
    public AuthenticationCredential AuthenticationCredential { get; set; }
    public AuthenticatedUserInfo? AuthenticatedUserInfo { get; set; }

    public AuthenticationService( 
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
    
    public bool TryAuthenticate()
    {
        var isauth = _mutableConfigurationService.GetConfigurationValue("IsAuthenticate");
        var token = _mutableConfigurationService.GetConfigurationValue("AuthToken");
        
        if (isauth == "true" && CheckTokenValidTime(token!))
        {
            _logger.Debug("User is authenticated");
            AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
            AuthenticationCredential.JWTToken = token;
            IsAuthenticated = true;
            AuthenticatedUserInfo = _mutableConfigurationService.GetAuthenticatedUser()!;
            NotifyAuthenticationSucceeded();
            return true;
        }
        else
        {
            return false;
            /*_logger.Debug("Starting authentication");
            var dialog = new Login();
            dialog.ShowDialog( parentWindow );*/
        }
    }

    public bool CheckTokenValidTime(string token, int minutesToExpire = 0)
    {
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

        if (jwtToken == null)
        {
            _logger.Error("Invalid token format");
            return false;
        }

        if (jwtToken.ValidTo > DateTime.UtcNow.AddMinutes(minutesToExpire))
        {
            _logger.Debug("Token is valid");
            return true;
        }
        else
        {
            _logger.Debug("Token is expired");
            return false;
        }
        
    }

    public int RefreshToken()
    {
        var client = _restService.GetClient(ignoreTimeVerification: true);
        var request = new RestRequest("/Authentication/GetToken");

        try
        {
            var response = client.Get(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                var token = JsonSerializer.Deserialize<string>(response.Content!);
                //var token = response.Content;
                _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                _mutableConfigurationService.SetConfigurationValue("AuthToken", token!);
                _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                AuthenticationCredential.JWTToken = token;
                IsAuthenticated = true;
                GetAuthenticatedUserInfo();
                return 0;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Error("Authentication Error response code: {0}", response.StatusCode);
                return 1;
            }
            
        }
        catch (Exception ex)
        {
            _logger.Error("Unkown error {0}", ex.Message);
        }
        
        return -1;
    }

    public bool CheckSamlAuthentication(string requestId)
    {
        var client = _restService.GetClient();
        var request = new RestRequest("/Authentication/AppSAMLToken");
        request.AddParameter("requestId", requestId);
        try
        {
            var response = client.Get(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                if (response.Content == "Not accepted")
                {
                    return false;
                }
                else
                {
                    var token = JsonSerializer.Deserialize<string>(response.Content!);
                    //var token = response.Content;
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
            if(ex.StatusCode != HttpStatusCode.Unauthorized) _logger.Error("Unkown error {0}", ex.Message);
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
        var client = _restService.GetClient(new HttpBasicAuthenticator(user, password));

        
        var request = new RestRequest("/Authentication/GetToken");
        

        //client.Authenticator = new HttpBasicAuthenticator(user, password);
        client.AddDefaultHeader("ClientId", _environmentService.DeviceID);
        
        try
        {
            var response = client.Get(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                var token = JsonSerializer.Deserialize<string>(response.Content!);
                //var token = response.Content;
                _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "true");
                _mutableConfigurationService.SetConfigurationValue("AuthToken", token!);
                _mutableConfigurationService.SetConfigurationValue("AuthTokenTime", DateTime.Now.Ticks.ToString());
                AuthenticationCredential.AuthenticationType = AuthenticationType.JWT;
                AuthenticationCredential.JWTToken = token;
                IsAuthenticated = true;
                GetAuthenticatedUserInfo();
                _logger.Information("User {0} authenticated", user);
                NotifyAuthenticationSucceeded();
                return 0;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Error("Authentication Error response code: {0}", response.StatusCode);
                return 1;
            }
            
        }
        catch (Exception ex)
        {
            _logger.Error("Unkown error {0}", ex.Message);
            
        }
        
        return -1;
    }

    public int GetAuthenticatedUserInfo()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Authentication/AuthenticatedUserInfo");
        
        try
        {
            var response = client.Get<AuthenticatedUserInfo>(request);

            if (response != null)
            {
                AuthenticatedUserInfo = response;
                _mutableConfigurationService.SaveAuthenticatedUser(AuthenticatedUserInfo);
                return 0;
            }

            return 1;
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting user info {ExMessage}", ex.Message);
        }
        
        return -1;
    }

    public List<AuthenticationMethod> GetAuthenticationMethods()
    {
        var defaultResponse = new List<AuthenticationMethod>();
        defaultResponse.Add(new AuthenticationMethod
        {
            Name= "Error",
            Type = "Basic"
        });
        
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Authentication/AuthenticationMethods");
        try
        {
            var response = client.Get<List<AuthenticationMethod>>(request);

            if (response != null)
            {
                _logger.Debug("Listing authentication methods");
                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Unkown error {0}", ex.Message);
            
        }
        return defaultResponse;
    
    }

    public void Logout()
    {
        _mutableConfigurationService.SetConfigurationValue("IsAuthenticate", "false");
        _mutableConfigurationService.RemoveConfigurationValue("AuthToken");
        _mutableConfigurationService.RemoveConfigurationValue("AuthTokenTime");

        IsAuthenticated = false;
    }
    
    private void NotifyAuthenticationSucceeded()
    {
        if(AuthenticationSucceeded != null) AuthenticationSucceeded(this, new EventArgs());
    }
    public event EventHandler? AuthenticationSucceeded;
}