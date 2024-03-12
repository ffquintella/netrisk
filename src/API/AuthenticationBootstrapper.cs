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
            .AddScheme<JwtBearerOptions, JwtAuthenticationHandler>("Bearer",
                x =>
                {
                    x.RequireHttpsMetadata = true;
                    x.TokenValidationParameters = new TokenValidationParameters
                    {
                        RequireExpirationTime = true,
                        //ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key),
                        ValidateIssuer = false,
                        ValidateAudience = false
                    };
                });

            if (saml2Configuration["Enabled"] == "True")
            {
                authenticationBuilder
                    .AddCookie("saml2.cookies", options =>
                    {
                        options.Cookie.HttpOnly = true;
                        options.Cookie.SameSite = SameSiteMode.None;
                        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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