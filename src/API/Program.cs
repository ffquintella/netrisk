using System;
using System.IO;
using System.Net;
using API;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

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
    options.Listen(IPAddress.Any, 5443, listenOptions =>
    {
        listenOptions.UseHttps(certificateFile, certificatePassword);
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