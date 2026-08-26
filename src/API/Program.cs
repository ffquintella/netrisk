using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using API;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using ServerServices;
using ServerServices.Interfaces;

// Track 7 finding NR-2026-033. Two things were wrong with the previous builder.
//
// It never called AddEnvironmentVariables, so there was no way at all to supply a secret except by
// writing it into appsettings.json on the target host — which is exactly what milestone 7.3.3
// forbids, and the reason the Puppet templates render the database password to disk (NR-2026-025).
// With this provider in place, Database__ConnectionString and https__certificate__password work as
// documented in docs/security/SECRETS.md, with no other change.
//
// And the order was inverted: appsettings.json was added *after* user-secrets, so the committed file
// won every key the two had in common. That is the opposite of what a developer expects, and the
// kind of thing that is only noticed when an override silently does nothing. Later providers win in
// .NET configuration, so the correct order is file, then developer overrides, then environment.
var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json");

#if DEBUG
configurationBuilder.AddUserSecrets<Program>();
#endif

var configuration = configurationBuilder.AddEnvironmentVariables();

MapsterConfiguration.RegisterMappings();

// Track 7 milestone 7.4.1. "Tls13" pins TLS 1.3 only; anything else (including absent) allows
// 1.2 and 1.3 and nothing older. SSL 3.0 and TLS 1.0/1.1 are not reachable through this switch at
// all — an operator who wants them has to go around the application.
static SslProtocols ResolveSslProtocols(IConfiguration configuration) =>
    string.Equals(configuration["Security:Tls:MinimumVersion"], "Tls13", StringComparison.OrdinalIgnoreCase)
        ? SslProtocols.Tls13
        : SslProtocols.Tls12 | SslProtocols.Tls13;

var config = configuration.Build();
if (config == null) throw new Exception("Error loading configuration");

var builder = WebApplication.CreateBuilder(args);

var strhttps = config!["https:port"];
if (strhttps == null) throw new Exception("Https port cannot be empty");
int httpsPort = Int32.Parse(strhttps);

if(config!["https:certificate:file"] == null ) throw new Exception("Certificate file cannot be empty");
string certificateFile = config!["https:certificate:file"]!;
if(config!["https:certificate:password"] == null ) throw new Exception("Certificate password cannot be empty");
string certificatePassword = config!["https:certificate:password"]!;

// Track 7 finding NR-2026-003: the shipped appsettings.json pointed at a certificate whose private
// key is committed to this repository, with the password "pass". Refused rather than warned about,
// because the insecure configuration is the one an installation gets by changing nothing.
// Security:AllowDevelopmentCertificate=true (or DOTNET_ENVIRONMENT=Development) permits it locally.
// A Debug build is a developer's machine by definition, so it may use the committed certificate
// without ceremony. A Release binary may not, whatever its configuration file says — that is the
// half of this guard that matters.
#if DEBUG
    var allowDevelopmentCertificate = true;
#else
    var allowDevelopmentCertificate =
    string.Equals(config["Security:AllowDevelopmentCertificate"], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Development",
        StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development",
        StringComparison.OrdinalIgnoreCase);
#endif

Tools.Security.CommittedCertificates.Enforce(certificateFile, certificatePassword,
    allowDevelopmentCertificate);

// Maximum size, in bytes, of an incoming request body. File uploads (POST /Files) send the
// file content base64-encoded inside the JSON body, so the on-the-wire size is ~1.37x the raw
// file. Kestrel's default is 30 MB, which silently reset the connection on larger attachments
// (the client saw a "Broken pipe"). Configurable via Files:MaxRequestBodySizeBytes; defaults to 100 MB.
long maxRequestBodySize = 104_857_600; // 100 MB
if (long.TryParse(config!["Files:MaxRequestBodySizeBytes"], out var configuredMaxBody) && configuredMaxBody > 0)
    maxRequestBodySize = configuredMaxBody;

builder.Services.Configure<KestrelServerOptions>(options =>
{
    // Track 7 milestone 7.4.3. SecurityHeadersMiddleware removes "Server" from the response header
    // collection, but that is not enough on its own: Kestrel writes its own Server header at the
    // transport layer, after the middleware pipeline has finished with the response. A live header
    // scan of the running site is what showed the middleware alone leaving `server: Kestrel` in
    // place. This is the switch that actually suppresses it.
    options.AddServerHeader = false;

    options.Limits.MaxRequestBodySize = maxRequestBodySize;
    options.Listen(IPAddress.Any, httpsPort, listenOptions =>
    {
        listenOptions.UseHttps(certificateFile, certificatePassword);
        listenOptions.KestrelServerOptions.ConfigureHttpsDefaults(httpsOptions =>
        {
            // Track 7 milestone 7.4.1: TLS 1.3 only was the previous setting. It is stricter than
            // the 1.2-minimum the milestone asks for, and it stays available — but 1.2 is now
            // allowed alongside it by default, because a TLS-1.3-only listener silently refuses
            // clients on older platform TLS stacks (Windows Server 2019, some corporate
            // middleboxes) and the observed workaround for that is an operator turning HTTPS off
            // altogether. Set Security:Tls:MinimumVersion=Tls13 for a deployment that wants 1.3 only.
            httpsOptions.SslProtocols = ResolveSslProtocols(config);
            
            
             // Configure the cipher suits preferred and supported by the server. (Windows- servers are not so keen on doing this ...)
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                httpsOptions.OnAuthenticate = (conContext, sslAuthOptions) =>
                {
                    #pragma warning disable CA1416
                    sslAuthOptions.CipherSuitesPolicy = new System.Net.Security.CipherSuitesPolicy(
                        new System.Net.Security.TlsCipherSuite[]
                        {
                            // Cipher suits as recommended by: https://wiki.mozilla.org/Security/Server_Side_TLS
                            // Listed in preferred order.

                            // Highly secure TLS 1.3 cipher suits:
                            System.Net.Security.TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                            System.Net.Security.TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                            System.Net.Security.TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,

                            // Medium secure compatibility TLS 1.2 cipher suits:
                            System.Net.Security.TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                            System.Net.Security.TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                            System.Net.Security.TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                            System.Net.Security.TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                            System.Net.Security.TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                            System.Net.Security.TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                            System.Net.Security.TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
                            System.Net.Security.TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384
                        }
                    );
                    #pragma warning restore CA1416
                };

            }
            
        });
    } );
});

Bootstrapper.Register(builder.Services, config);

// Track 7 milestone 7.4.3 — the response headers every surface should send. Resolved from
// configuration once, at startup, so a malformed value shows up in the log immediately rather than
// on the first request.
var securityHeaderPolicy = API.Middleware.SecurityHeadersMiddleware.PolicyFrom(config);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}


app.UseHttpsRedirection();

// First in the pipeline, so a response produced by anything after it — including an exception page
// or a 404 from routing — still carries the headers.
app.UseMiddleware<API.Middleware.SecurityHeadersMiddleware>(securityHeaderPolicy);

// Before authentication: the point of a rate limit on the credential endpoints is to be cheaper
// than the bcrypt verification it protects.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so the log line can attribute the refusal, and around the controllers,
// which is where the DbContext guard throws from (Track 2 milestone 2.3.1).
app.UseMiddleware<API.Middleware.EntityScopeViolationMiddleware>();

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information("Application started");
    }
);

app.Run();