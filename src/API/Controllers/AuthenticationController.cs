using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
using Tools;
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
    private readonly IClientRegistrationService _clientRegistrationService;
    public AuthenticationController(ILogger<AuthenticationController> logger, 
        IConfiguration configuration,
        IEnvironmentService environmentService,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IRolesService rolesService,
        IMemoryCache memoryCache,
        IDalService dalService,
        IClientRegistrationService clientRegistrationService
        )
    {
        _clientRegistrationService = clientRegistrationService;
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
                new Claim(ClaimTypes.Name, username),
                // A per-token identifier. Track 8 turned this into a working revocation key:
                // POST /Sessions/Logout records the jti in `revoked_tokens` and the authentication
                // handler refuses it on the next request (finding NR-2026-028).
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),

            // Track 7 milestone 7.3.2: named so the validator can insist on them. See
            // API.Security.JwtDefaults for why the two ends share the constants.
            Issuer = _configuration["JWT:Issuer"] ?? API.Security.JwtDefaults.Issuer,
            Audience = _configuration["JWT:Audience"] ?? API.Security.JwtDefaults.Audience,

            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(ResolveTokenLifetimeMinutes()),

            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(symmetricKey), 
                SecurityAlgorithms.HmacSha256Signature)
        };

        var stoken = tokenHandler.CreateToken(tokenDescriptor);
        var token = tokenHandler.WriteToken(stoken);
        return token;
    }

    /// <summary>
    /// The access-token lifetime, in minutes.
    ///
    /// The shipped default used to be 1440 — a bearer token valid for a day, with nothing able to
    /// revoke it. This resolves the configured value, falls back to an hour and refuses to honour
    /// anything longer than a day, because a value like that is a mistake rather than a policy and
    /// silently obeying it is how a "short-lived token" turns out to last a month.
    /// </summary>
    private int ResolveTokenLifetimeMinutes()
    {
        var configured = _configuration["JWT:Timeout"];

        if (!int.TryParse(configured, out var minutes) || minutes <= 0)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                _logger.LogWarning("JWT:Timeout '{Configured}' is not a positive number of minutes; "
                                   + "using {Default}", configured, API.Security.JwtDefaults.TimeoutMinutes);
            return API.Security.JwtDefaults.TimeoutMinutes;
        }

        if (minutes > API.Security.JwtDefaults.MaxTimeoutMinutes)
        {
            _logger.LogWarning("JWT:Timeout of {Configured} minutes exceeds the {Max}-minute ceiling; "
                               + "clamping", minutes, API.Security.JwtDefaults.MaxTimeoutMinutes);
            return API.Security.JwtDefaults.MaxTimeoutMinutes;
        }

        return minutes;
    }
    
    /// <summary>
    /// Mints the identifier for a desktop SSO sign-in. Called by the desktop client, not the browser.
    ///
    /// Track 7 finding NR-2026-001. The identifier used to be chosen by whoever called
    /// <c>SAMLRequest</c>, which is the browser-facing endpoint, and which is anonymous. An attacker
    /// could therefore pick a value, send a victim
    /// <c>/Authentication/SAMLRequest?requestId=&lt;their-value&gt;</c>, let the victim's existing
    /// single-sign-on session complete the flow, and then redeem the victim's identity from
    /// <c>AppSAMLToken</c> — a one-click account takeover that no amount of entropy in the identifier
    /// would have prevented, because the attacker was not guessing it.
    ///
    /// So the server owns the identifier now, and minting one requires an approved client
    /// registration. An anonymous outsider cannot create a pending sign-in at all: they would need an
    /// administrator to have approved their device first (the same gate every other authenticated
    /// call already passes through). The endpoint stays <c>[AllowAnonymous]</c> because the client has
    /// no session yet — the client *registration* is the credential here, not a user.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("SAMLRequestId")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<string>> CreateSamlRequestId()
    {
        var clientId = Request.Headers["ClientId"].ToString();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("A SAML sign-in was requested with no ClientId header from {Ip}",
                _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress);
            return Unauthorized("A registered client is required");
        }

        var registration = await _clientRegistrationService.FindApprovedRegistrationAsync(clientId);

        if (registration == null)
        {
            _logger.LogWarning("A SAML sign-in was requested by the unapproved client {ClientId} from {Ip}",
                clientId, _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress);
            return Unauthorized("A registered client is required");
        }

        // 32 characters of the 65-symbol CSPRNG alphabet — about 190 bits. Alphanumeric rather than
        // base64url because the id travels in a URL that SanitizeRequestId reduces to
        // [A-Za-z0-9-], and a '_' would be silently stripped.
        var requestId = RandomGenerator.RandomString(32);

        _memoryCache.Set("SAML_REQ_" + requestId, new SAMLRequest
        {
            RequestToken = requestId,
            Status = "requested",
            ClientId = clientId,
            ClientHostname = registration.Hostname ?? "(unknown)"
        }, new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));

        _logger.LogInformation("Minted a SAML sign-in request for client {ClientId} ({Hostname})",
            clientId, registration.Hostname);

        return Ok(requestId);
    }

    /// <summary>
    /// The browser's entry point into the SAML flow.
    /// </summary>
    /// <remarks>
    /// It no longer *creates* anything: the id must already have been minted by
    /// <see cref="CreateSamlRequestId"/>, and an id this server did not mint is refused. That is the
    /// half of finding NR-2026-001 that matters — the identifier is now unguessable *and*
    /// unchooseable.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [Route("SAMLRequest")]
    public ActionResult SAMLRequest([FromQuery] string requestId)
    {
        var sanitized = SanitizeRequestId(requestId);
        if (sanitized == null) return BadRequest("Invalid request id");

        if (!_memoryCache.TryGetValue("SAML_REQ_" + sanitized, out SAMLRequest? pending) || pending == null)
        {
            _logger.LogWarning(
                "Refused a SAML sign-in for an unminted request id from {Ip}. The desktop client must "
                + "call /Authentication/SAMLRequestId first",
                _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress);

            return BadRequest("Unknown request id");
        }

        Response.Cookies.Append("SAMLReqID", sanitized, new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            // Has to survive the identity provider's cross-site POST back, so None is forced. That is
            // exactly why the approval form below carries its own anti-forgery token.
            SameSite = SameSiteMode.None,
            IsEssential = true,
            Expires = DateTimeOffset.Now.AddMinutes(15)
        });

        _logger.LogDebug("Starting SAML REQUEST for id:{RequestId}", sanitized);

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

                // The identity is established. What is NOT established is that the person in front of
                // this browser meant to sign a *particular machine* in.
                //
                // Track 7 finding NR-2026-001 (residual). This used to flip the request to "accepted"
                // on sight, which made the whole flow a one-click grant: anyone who could get this URL
                // in front of an already-signed-in colleague harvested their session. So the request is
                // left pending and an approval page is rendered instead, naming the machine that asked
                // — the same consent step the OAuth device-authorization grant (RFC 8628) exists for,
                // and the reason a victim who was merely sent a link declines instead of proceeding.
                if (samlRequest.Status != "requested")
                {
                    return base.Content(ApprovalPage(
                        "This sign-in request has already been dealt with.", null, null), "text/html");
                }

                // A single-use anti-forgery token. The SAML cookie is SameSite=None by necessity, so
                // without this a cross-site page could auto-submit the approval and the consent screen
                // would be decorative.
                samlRequest.ApprovalToken = RandomGenerator.RandomString(32);
                samlRequest.UserName = _httpContextAccessor.HttpContext!.User!.Identity!.Name!;

                _memoryCache.Set("SAML_REQ_" + requestId, samlRequest, new MemoryCacheEntryOptions()
                    .SetSize(1)
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));

                _logger.LogInformation(
                    "SAML identity established for {Name} from {Ip}; awaiting approval for client {Hostname}",
                    samlRequest.UserName, _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress,
                    samlRequest.ClientHostname);

                return base.Content(
                    ApprovalPage(null, samlRequest.ClientHostname, samlRequest.ApprovalToken),
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

    
    /// <summary>
    /// Completes the approval the person in the browser just gave.
    ///
    /// Three things have to line up: the browser holds the <c>SAMLReqID</c> cookie for this request,
    /// it is authenticated as the same identity that reached the approval page, and it presents the
    /// single-use approval token that only appeared inside that page. The third is what makes this
    /// safe against a cross-site auto-submitted form, which the <c>SameSite=None</c> session cookie
    /// would otherwise carry (Track 7 finding NR-2026-001).
    /// </summary>
    [HttpPost]
    [Route("SAMLApprove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult ApproveSamlSignIn([FromForm] string approvalToken)
    {
        var requestId = SanitizeRequestId(Request.Cookies["SAMLReqID"]);

        if (requestId == null
            || !_memoryCache.TryGetValue("SAML_REQ_" + requestId, out SAMLRequest? samlRequest)
            || samlRequest == null)
            return BadRequest("No SAML request id found");

        if (string.IsNullOrEmpty(samlRequest.ApprovalToken)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(samlRequest.ApprovalToken),
                Encoding.UTF8.GetBytes(approvalToken ?? "")))
        {
            _logger.LogWarning(
                "Rejected a SAML approval with a wrong or missing anti-forgery token from {Ip}",
                _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress);

            return BadRequest("Invalid approval");
        }

        // The browser has to still be the authenticated identity the page was rendered for; a token
        // replayed from a different session must not approve somebody else's request.
        var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name;

        if (!string.Equals(currentUser, samlRequest.UserName, StringComparison.Ordinal))
        {
            _logger.LogWarning("A SAML approval was presented by a different identity than the one it was issued to");
            return BadRequest("Invalid approval");
        }

        samlRequest.Status = "accepted";
        // Burned: the page is single-use, so a re-post cannot re-approve an already-redeemed request.
        samlRequest.ApprovalToken = "";

        _memoryCache.Set("SAML_REQ_" + requestId, samlRequest, new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));

        var account = UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
        var dbUser = account == null ? null : _usersService.GetUser(account);
        if (dbUser != null)
            _usersService.RegisterLoginAsync(dbUser.Value,
                _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");

        _logger.LogInformation("SAML sign-in approved for {User} on client {Hostname}",
            samlRequest.UserName, samlRequest.ClientHostname);

        return base.Content(ApprovalPage(
            "Sign-in approved. It is now safe to close this window.", null, null), "text/html");
    }

    /// <summary>
    /// Renders the approval page, or a plain message when there is nothing to approve.
    ///
    /// Hand-built HTML rather than a view, because the API project has no view engine and pulling one
    /// in for one page is not a trade worth making. Every interpolated value goes through
    /// <see cref="HtmlEncode"/> — the hostname comes from a client registration, which is a value an
    /// unauthenticated caller supplied at registration time, so it is untrusted text.
    /// </summary>
    private static string ApprovalPage(string? message, string? clientHostname, string? approvalToken)
    {
        const string style =
            "font-family:system-ui,-apple-system,Segoe UI,sans-serif;max-width:34rem;margin:4rem auto;"
            + "padding:0 1rem;line-height:1.5";

        if (message != null || approvalToken == null)
            return $"<!doctype html><html><head><meta charset=\"utf-8\"><title>NetRisk</title></head>"
                   + $"<body style=\"{style}\"><h1>NetRisk</h1><p>{HtmlEncode(message ?? "Nothing to approve.")}</p>"
                   + "</body></html>";

        return $"<!doctype html><html><head><meta charset=\"utf-8\"><title>NetRisk sign-in</title></head>"
               + $"<body style=\"{style}\">"
               + "<h1>Approve this sign-in?</h1>"
               + "<p>A NetRisk desktop client asked to sign in as you from:</p>"
               + $"<p><strong>{HtmlEncode(clientHostname ?? "(unknown machine)")}</strong></p>"
               + "<p>If you did not just start a sign-in on that machine, close this window. "
               + "Approving it would give it access to NetRisk as you.</p>"
               + "<form method=\"post\" action=\"/Authentication/SAMLApprove\">"
               + $"<input type=\"hidden\" name=\"approvalToken\" value=\"{HtmlEncode(approvalToken)}\">"
               + "<button type=\"submit\" style=\"padding:.6rem 1.2rem;font-size:1rem\">Approve</button>"
               + "</form></body></html>";
    }

    /// <summary>
    /// Minimal HTML entity encoding for the values interpolated into the approval page.
    /// </summary>
    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    /// <summary>
    /// Hands the desktop client the session token for a SAML sign-in it started.
    ///
    /// Track 7 finding NR-2026-001. This endpoint is necessarily anonymous — the client has no
    /// token yet, which is the whole point — so the request id *is* the bearer credential for a
    /// freshly minted JWT. Two things were wrong with that:
    ///
    ///  * the id was chosen by the caller, so an attacker did not have to guess it at all — they
    ///    picked one, sent a victim the browser link, and collected the result;
    ///  * the id was accepted at any length, and the client generated it from a predictable
    ///    generator (see <see cref="Tools.RandomGenerator"/>, finding NR-2026-002);
    ///  * the cache entry survived redemption, so anyone who later learned an id — from a proxy log,
    ///    a shoulder-surfed URL, shared browser history — could mint another token for that user.
    ///
    /// All three are closed: the id is minted by the server for an approved client
    /// (<see cref="CreateSamlRequestId"/>), redemption requires the same client registration that
    /// minted it, and the entry is removed before the token is written so redemption is single-use.
    /// The person in the browser also has to approve the request explicitly, naming the machine that
    /// asked — see <see cref="ApproveSamlSignIn"/>.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("AppSAMLToken")]
    public ActionResult GetAppSAMLToken([FromQuery] string requestId)
    {
        var sanitized = SanitizeRequestId(requestId);
        if (sanitized == null) return NotFound("Token not found");

        if (!_memoryCache.TryGetValue("SAML_REQ_" + sanitized, out SAMLRequest? samlRequest)
            || samlRequest == null)
            return NotFound("Token not found");

        // The token goes back to the machine that asked for the sign-in and to no other. Without
        // this, anybody who learned an id could collect a session that somebody else approved — the
        // second half of finding NR-2026-001.
        var clientId = Request.Headers["ClientId"].ToString();

        if (!string.Equals(clientId, samlRequest.ClientId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Refused to hand a SAML session token to client {Presented}; the request was minted by {Owner}",
                string.IsNullOrEmpty(clientId) ? "(none)" : clientId, samlRequest.ClientId);

            return Unauthorized("Not accepted");
        }

        if (samlRequest.Status != "accepted")
        {
            return Unauthorized("Not accepted");
        }

        // Removed *before* the token is written, so a replay of the same id finds nothing even if
        // two requests race.
        _memoryCache.Remove("SAML_REQ_" + sanitized);

        _logger.LogInformation("Authentication token created for user: {0} fromip: {1}",
            samlRequest.UserName,
            _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress);

        return Ok(GenerateToken(samlRequest.UserName));
    }

    /// <summary>
    /// The minimum length of a SAML request id, in characters of the sanitized alphabet.
    ///
    /// The client sends 20, which at 65 possible characters is around 120 bits. Sixteen is the floor
    /// this endpoint will look up at all: shorter than that and a caller could enumerate the cache.
    /// </summary>
    private const int MinimumRequestIdLength = 16;

    /// <summary>
    /// Reduces a request id to the safe alphabet and rejects it if what is left is too short to be
    /// unguessable. Returning null rather than an empty string keeps callers from turning a rejected
    /// id into a lookup of <c>"SAML_REQ_"</c>.
    /// </summary>
    private string? SanitizeRequestId(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return null;

        var sanitized = RequestIdPattern.Replace(requestId, "");

        if (sanitized.Length < MinimumRequestIdLength)
        {
            _logger.LogWarning(
                "Rejected a SAML request id of {Length} usable characters from {Ip}; the minimum is {Minimum}",
                sanitized.Length, _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress,
                MinimumRequestIdLength);
            return null;
        }

        return sanitized;
    }

    private static readonly Regex RequestIdPattern = new("[^a-zA-Z0-9-]", RegexOptions.Compiled);

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
            UserEmail = user.Email,
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