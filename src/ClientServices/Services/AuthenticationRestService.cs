﻿using System;
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

    public int RefreshToken()
    {
        using var client = RestService.GetClient(ignoreTimeVerification: true);
        var request = new RestRequest("/Authentication/GetToken");

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

    public bool CheckSamlAuthentication(string requestId)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Authentication/AppSAMLToken");
        request.AddParameter("requestId", requestId);
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