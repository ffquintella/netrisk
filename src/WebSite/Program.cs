using System.Net;
using System.Security.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using WebSite;
using WebSiteData;

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

// Track 7 finding NR-2026-003, the WebSite half. src/WebSite/Certificates holds the same committed,
// self-signed, private-key-included material as the API's, and appsettings.json pointed at it with
// the password "pass".
{
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

    var problem = Tools.Security.CommittedCertificates.Inspect(certificateFile, certificatePassword,
        allowDevelopmentCertificate);

    if (problem != null) throw new InvalidOperationException(problem);
}

builder.Services.Configure<KestrelServerOptions>(options =>
{
    // Track 7 milestone 7.4.3. SecurityHeadersMiddleware removes "Server" from the response header
    // collection, but that is not enough on its own: Kestrel writes its own Server header at the
    // transport layer, after the middleware pipeline has finished with the response. A live header
    // scan of the running site is what showed the middleware alone leaving `server: Kestrel` in
    // place. This is the switch that actually suppresses it.
    options.AddServerHeader = false;

    options.Listen(IPAddress.Any, httpsPort, listenOptions =>
    {
        listenOptions.UseHttps(certificateFile, certificatePassword);
        listenOptions.KestrelServerOptions.ConfigureHttpsDefaults(httpsOptions =>
        {
            // Track 7 milestone 7.4.1: the WebSite inherited whatever the host OS offered, which on
            // an older distribution still includes TLS 1.0 and 1.1. The API already pinned its own
            // floor; this brings the download site in line.
            httpsOptions.SslProtocols =
                string.Equals(config["Security:Tls:MinimumVersion"], "Tls13", StringComparison.OrdinalIgnoreCase)
                    ? SslProtocols.Tls13
                    : SslProtocols.Tls12 | SslProtocols.Tls13;
        });
    } );
});

// Add services to the container.
builder.Services.AddControllersWithViews();

Bootstrapper.Register(builder.Services, config);

// Track 7 milestone 7.4.3 — resolved before the app is built so a bad configuration value surfaces
// at startup.
var securityHeaderPolicy = WebSite.Middleware.SecurityHeadersMiddleware.PolicyFrom(config);

var app = builder.Build();

// Provision the local SQLite store (schema + WAL) before serving requests.
WebSiteDataBootstrapper.InitializeDatabase(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Ahead of static files, so the headers land on the installer downloads and the error pages too.
app.UseMiddleware<WebSite.Middleware.SecurityHeadersMiddleware>(securityHeaderPolicy);

// Ensure installer artifacts are served with a known content type.
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".dmg"] = "application/x-apple-diskimage";
contentTypeProvider.Mappings[".pkg"] = "application/octet-stream";
contentTypeProvider.Mappings[".sha256"] = "text/plain";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Attribute-routed API controllers (the /sync endpoints).
app.MapControllers();

app.Run();
