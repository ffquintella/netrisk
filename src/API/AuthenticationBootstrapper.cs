using System;
using System.Security.Claims;
using System.Text;
using API.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Saml2.Authentication.Core.Configuration;
using Serilog;
using ServerServices;
using ServerServices.Services;

namespace API;

public static class AuthenticationBootstrapper
{
    public static void RegisterAuthentication(IServiceCollection services, IConfiguration config)
    {
        var envService = new EnvironmentService();
        var key = Convert.FromBase64String(envService.ServerSecretToken);
        
        var saml2Configuration = config.GetSection("Saml2");
        
        if(saml2Configuration["Enabled"] == "True")
        {
            Log.Information("SAML2 Enabled");
            services.Configure<Saml2Configuration>(saml2Configuration);
            services.AddSaml();
        }
        else
        {
            Log.Information("SAML2 Disabled");
        }
        
        
        var authenticationBuilder = services.AddAuthentication(options =>
            {
                //options.DefaultScheme = "saml2";
                options.DefaultScheme = "headerSelector";
                options.DefaultChallengeScheme = "headerSelector";
            })
            .AddPolicyScheme("headerSelector", "this will select SAML or Basic Authentication", options =>
            {
                options.ForwardDefaultSelector = (context) =>
                {
                    if (context.Request.Headers.ContainsKey("Authorization"))
                    {
                        if(context.Request.Headers["Authorization"].ToString().StartsWith("Bearer "))
                        {
                            // A CI API token is also a bearer token, so the two are told apart by
                            // the nrk_ prefix rather than by the header name (Track 3 milestone
                            // 3.5.1). Selecting on the prefix here keeps each handler responsible
                            // for exactly one credential shape.
                            if (context.Request.Headers["Authorization"].ToString()
                                .StartsWith("Bearer " + DAL.Entities.ApiToken.SecretPrefix, StringComparison.Ordinal))
                            {
                                Log.Debug("Authenticating using an API token");
                                return ApiTokenAuthenticationHandler.SchemeName;
                            }

                            // Track 4.3.2: a SCIM provisioning token is also a bearer token, told apart
                            // by its scim_ prefix for the same reason as the CI token — one handler per
                            // credential shape, so a scope check cannot be skipped for one of them.
                            if (context.Request.Headers["Authorization"].ToString()
                                .StartsWith("Bearer " + DAL.Entities.ScimToken.SecretPrefix, StringComparison.Ordinal))
                            {
                                Log.Debug("Authenticating using a SCIM provisioning token");
                                return ScimAuthenticationHandler.SchemeName;
                            }

                            Log.Debug("Authenticating using Jwt");
                            return "Bearer";
                        }
                        //Log.Debug("Authenticating using Basic");
                        return "BasicAuthentication";
                    }
                    else if(config["Saml2:Enabled"] == "True")
                    {
                        Log.Debug("Authenticating using SAML");
                        return "saml2";
                    }
                    else
                    {
                        return "BasicAuthentication";
                    }
        
                };
                
            })
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenAuthenticationHandler.SchemeName, null)
            .AddScheme<AuthenticationSchemeOptions, ScimAuthenticationHandler>(
                ScimAuthenticationHandler.SchemeName, null)
            .AddScheme<JwtBearerOptions, JwtAuthenticationHandler>("Bearer",
                x =>
                {
                    x.RequireHttpsMetadata = true;
                    x.TokenValidationParameters = new TokenValidationParameters
                    {
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key),

                        // Track 7 finding NR-2026-012. These were both false, which meant the only
                        // thing a presented token had to satisfy was "signed with this
                        // installation's key". That is enough on its own today, but it also means a
                        // token minted for any other purpose under the same key — a future
                        // service-to-service token, a token from a shared secret-store entry — would
                        // be accepted as a user session. Naming the issuer and audience makes the
                        // token say what it is for.
                        ValidateIssuer = true,
                        ValidIssuer = config["JWT:Issuer"] ?? JwtDefaults.Issuer,
                        ValidateAudience = true,
                        ValidAudience = config["JWT:Audience"] ?? JwtDefaults.Audience,

                        // The default five minutes of tolerance is generous for a token whose whole
                        // lifetime is measured in minutes.
                        ClockSkew = TimeSpan.FromSeconds(30),

                        // Only HMAC-SHA256 is ever issued, so accepting anything else would only
                        // ever help an attacker who found an algorithm-confusion trick.
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
                    };
                });

            if (saml2Configuration["Enabled"] == "True")
            {
                authenticationBuilder
                    .AddCookie("saml2.cookies", options =>
                    {
                        options.Cookie.HttpOnly = true;
                        // SameSite=None is required: the cookie has to survive the cross-site
                        // POST back from the identity provider. That is exactly why the rest has to
                        // be tight — SameSite=None without Secure is rejected by modern browsers
                        // anyway, and "SameAsRequest" meant a plain-HTTP hop shipped the session
                        // cookie in clear (Track 7 finding NR-2026-016).
                        options.Cookie.SameSite = SameSiteMode.None;
                        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                        options.Cookie.IsEssential = true;
                    })
                    .AddSaml("saml2", "saml2", options =>
                    {
                        options.DefaultRedirectUrl = "/Authentication/SAMLSingIn";
                        options.SignInScheme = "saml2.cookies";
                        options.IdentityProviderName = "saml2.provider";

                    });
            }
            

            services.AddAuthorization();
    }
}