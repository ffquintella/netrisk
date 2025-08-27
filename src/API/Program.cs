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

#if DEBUG
var configuration =  new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>()
    .AddJsonFile($"appsettings.json");
#else 
var configuration =  new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile($"appsettings.json");
#endif

MapsterConfiguration.RegisterMappings();

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

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Listen(IPAddress.Any, httpsPort, listenOptions =>
    {
        listenOptions.UseHttps(certificateFile, certificatePassword);
        listenOptions.KestrelServerOptions.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.SslProtocols = SslProtocols.Tls13;
            
            
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}


app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information("Application started");
    }
);

app.Run();