﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using API.Tools;
using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Model.Authentication;
using Model.Exceptions;
using ServerServices;
using System.Linq;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.User;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class AuthenticationController : ControllerBase
{
    private readonly ILogger<AuthenticationController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IEnvironmentService _environmentService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUsersService _usersService;
    private readonly IRolesService _rolesService;
    private readonly IMemoryCache _memoryCache;
    private readonly IDalService _dalService;
    public AuthenticationController(ILogger<AuthenticationController> logger, 
        IConfiguration configuration,
        IEnvironmentService environmentService,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IRolesService rolesService,
        IMemoryCache memoryCache,
        IDalService dalService
        )
    {
        _logger = logger;
        _configuration = configuration;
        _environmentService = environmentService;
        _httpContextAccessor = httpContextAccessor;
        _usersService = usersService;
        _rolesService = rolesService;
        _memoryCache = memoryCache;
        _dalService = dalService;
    }

    [HttpGet]
    [Route("GetToken")]
    public ActionResult<string> GetToken()
    {
        
        var token = GenerateToken(_httpContextAccessor.HttpContext!.User!.Identity!.Name!);

        _logger.LogInformation("Authentication token created for user: {0} fromip: {1}", 
            _httpContextAccessor.HttpContext!.User!.Identity!.Name!,
            _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress);
        
        return token;
    }

    private string GenerateToken(string username)
    {
        var symmetricKey = Convert.FromBase64String(_environmentService.ServerSecretToken);
        var tokenHandler = new JwtSecurityTokenHandler();

        var now = DateTime.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, username)
            }),

            Expires = now.AddMinutes(Convert.ToInt32(_configuration["JWT:Timeout"])),
        
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(symmetricKey), 
                SecurityAlgorithms.HmacSha256Signature)
        };

        var stoken = tokenHandler.CreateToken(tokenDescriptor);
        var token = tokenHandler.WriteToken(stoken);
        return token;
    }
    
    /// <summary>
    /// The request to this endpoint will start the SAML authentication process.
    /// </summary>
    /// <param name="requestId">Unique id of this request</param>
    /// <returns></returns>
    [HttpGet]
    [AllowAnonymous]
    [Route("SAMLRequest")]
    public ActionResult SAMLRequest([FromQuery] string requestId)
    {
        Regex rgx = new Regex("[^a-zA-Z0-9 -]");
        requestId = rgx.Replace(requestId, "");
        
        Response.Cookies.Append("SAMLReqID", requestId, new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.Now.AddMinutes(15)
        });
        
        _memoryCache.Set("SAML_REQ_"+requestId, new SAMLRequest
        {
            RequestToken = requestId
        }, new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));
        
        _logger.LogDebug("Starting SAML REQUEST for id:{RequestId}", requestId);
        
        return Redirect("/Authentication/SAMLSingIn");
    }

    [HttpGet]
    [Route("SAMLSingIn")]
    public ActionResult SAMLSingIn()
    {

        if (!Request.Cookies.ContainsKey("SAMLReqID"))
        {
            _logger.LogError("No SAML request id found");
            return BadRequest("No SAML request id found");
        }
        
        string? requestId = Request.Cookies["SAMLReqID"];  
        if (requestId == null)
        {
            _logger.LogError("No SAML request id found");
            return BadRequest("No SAML request id found");
        }
        
        if(_memoryCache.TryGetValue("SAML_REQ_"+requestId, out SAMLRequest? samlRequest))
        {
            try
            {

                if (samlRequest == null) throw new Exception("Error loading SAML Request");

                //First we need to know if the user exists on the database and if it´s a SAML user
                var dbContext = _dalService.GetContext(false);
                var reqUser = _httpContextAccessor.HttpContext!.User!.Identity!.Name!;

                if (!reqUser.Contains('@'))
                {
                    _logger.LogError("User not in email format");
                    return BadRequest("SAML user not in email format");
                }

                var user = reqUser.Split('@')[0];
                user = user.ToLower();
                
                _logger.LogInformation("Processing SAML request for user {User}", user);

                var dbUser = dbContext?.Users?
                    .Where(u => u.Type == "saml" && u.Enabled == true && u.Lockout == 0 &&
                                u.Login.ToLower() == user)
                    .FirstOrDefault();

                if (dbUser is null)
                {
                    _logger.LogWarning("SAML request for invalid user {User}", user);
                    return Unauthorized("Invalid user");
                }

                //Now we know the user is valid, we can issue the token.
                if (samlRequest.Status == "requested")
                {
                    _logger.LogInformation("SAML request accepted for user {User}", dbUser.Name);
                    samlRequest.Status = "accepted";
                    samlRequest.UserName = _httpContextAccessor.HttpContext!.User!.Identity!.Name!;

                    // Updates the Last Login date
                    _usersService.RegisterLoginAsync(dbUser.Value, _httpContextAccessor.HttpContext.Connection.RemoteIpAddress!.ToString());

                    //_memoryCache.Set("SAML_REQ_"+requestId, samlRequest, TimeSpan.FromMinutes(5) );
                    _memoryCache.Set("SAML_REQ_" + requestId, samlRequest, new MemoryCacheEntryOptions()
                        .SetSize(1)
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));
                }

                _logger.LogInformation("SAML Authentication for user: {Name} fromip: {Ip}",
                    _httpContextAccessor.HttpContext!.User!.Identity!.Name!,
                    _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress);

                return base.Content(
                    "<html><body><h1>Authentication successful</h1> <br/>It is now safe to close this window.</body></html>",
                    "text/html");

            }
            catch (UserNotFoundException ex)
            {
                Log.Error("Unable to find user:{Message}", ex.Message);
                return Unauthorized("Invalid user");
            }
            catch (Exception ex)
            {
                Log.Error("Unkown error on SAML authentication :{Message}", ex.Message);
                return StatusCode(500);
            }
            
            //return Ok("<html><body><h1>Authentication successful</h1> <br/>It is now safe to close this window.</body></html>");
            //return Redirect("/Authentication/SAMLResponse?requestId="+requestId);
        }
        else
        {
            return BadRequest("Invalid request");
        }
        
    }

    
    [HttpGet]
    [AllowAnonymous]
    [Route("AppSAMLToken")]
    public ActionResult GetAppSAMLToken([FromQuery] string requestId)
    {
        Regex rgx = new Regex("[^a-zA-Z0-9 -]");
        requestId = rgx.Replace(requestId, "");
        if (_memoryCache.TryGetValue("SAML_REQ_" + requestId, out SAMLRequest? samlRequest))
        {
            if (samlRequest == null) throw new Exception("Error loading SAML request");
            if(samlRequest.Status != "accepted")
            {
                return Unauthorized("Not accepted");
            }
            _logger.LogInformation("Authentication token created for user: {0} fromip: {1}", 
                samlRequest.UserName,
                _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress);
            return Ok(GenerateToken(samlRequest.UserName));
        }
        else
        {
            return NotFound("Token not found");
        }
    }

    [HttpGet]
    [Route("SAMLLogout")]
    public ActionResult SAMLLogout()
    {
        return Ok("Teste");
    }
    
    [HttpGet]
    [Route("AuthenticatedUserInfo")]
    public async Task<AuthenticatedUserInfo> GetAuthenticatedUserInfo()
    {
        var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
        
        if (userAccount == null)
        {
            _logger.LogError("Authenticated userAccount not found");
            throw new UserNotFoundException();
        }
        
        var user = await _usersService.GetUserAsync(userAccount);
        if (user == null )
        {
            _logger.LogError("Authenticated user not found");
            throw new UserNotFoundException();
        }

        string? userRole = null;
        if (user.RoleId > 0)
        {

            var role = await _rolesService.GetRoleAsync(user.RoleId);
            if (role == null)
            {
                _logger.LogError("Invalid role reference");
                throw new InvalidReferenceException($"Invalid role reference for id: {user.RoleId}");
            }
            userRole = role!.Name;
        }
        
        var permissions = await _usersService.GetUserPermissionsAsync(user.Value);
        
        
        var info = new AuthenticatedUserInfo
        {  
            UserAccount = userAccount,
            UserName = user.Name,
            UserId = user.Value,
            UserEmail = Encoding.UTF8.GetString(user.Email),
            UserRole = userRole,
            UserPermissions = permissions,
            IsAdmin = user.Admin
        };
        
        _logger.LogDebug("User info requested for user: {0} fromip: {1}", 
            _httpContextAccessor.HttpContext!.User!.Identity!.Name!,
            _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress);

        
        return info;
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("AuthenticationMethods")]
    public IEnumerable<AuthenticationMethod> GetAllAuthenticationMethods()
    {
        var result = new List<AuthenticationMethod>();

        var basic = new AuthenticationMethod
        {
            Name = "Local",
            Description = "Internal DB Authentication",
            Type = "Basic"
            
        };
        if (_configuration["Saml2:Enabled"] == "True")
        {
            var saml = new AuthenticationMethod
            {
                Name = "SAML",
                Description = "SAML Authentication",
                Type = "SAML"
            };
            result.Add(saml);

        }
        
        result.Add(basic);
        
        _logger.LogDebug("User methods requested fromip: {0}",
            _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress);

        
        return result;
    }
    
    
}