using System.Security.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RiskPortal.Services;
using Serilog;

// Configuration precedence is the same as everywhere else in this product: file, then user-secrets in
// a Debug build, then environment. Later providers win, so an operator's `Server__Url` overrides the
// committed appsettings.json rather than being silently ignored by it (the mistake Track 7's finding
// NR-2026-033 was about).
var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true);

#if DEBUG
configurationBuilder.AddUserSecrets<Program>();
#endif

var configuration = configurationBuilder.AddEnvironmentVariables().Build();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddConfiguration(configuration);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

var apiUrl = configuration["Server:Url"];
if (string.IsNullOrWhiteSpace(apiUrl))
    throw new InvalidOperationException(
        "Server:Url is not configured. The portal is a client of the NetRisk API and has nothing to " +
        "talk to without it. Set it in appsettings.json, in user-secrets, or as Server__Url.");

var options = new PortalOptions();
configuration.GetSection(PortalOptions.SectionName).Bind(options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IPortalRegistration, PortalRegistration>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPortalSession, PortalSession>();

builder.Services.AddHttpClient<IPortalApiClient, PortalApiClient>(client =>
    {
        client.BaseAddress = new Uri(apiUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler
        {
            // TLS 1.2 as the floor, matching the API's own listener.
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

#if DEBUG
        // A development API serves a self-signed certificate. Debug-only *and* behind a configuration
        // flag, so a Release binary cannot be talked into trusting anything — a portal that accepts
        // any certificate is a portal whose session token can be read off the wire.
        if (options.AllowUntrustedApiCertificate)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif

        return handler;
    });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.LoginPath = "/SignIn";
        cookie.LogoutPath = "/SignOut";
        cookie.AccessDeniedPath = "/SignIn";
        cookie.Cookie.Name = "netrisk.portal";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // Matched to the API's own token lifetime. A cookie that outlives the token it carries would
        // present a signed-in reviewer with an authenticated page whose every request fails.
        cookie.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        cookie.SlidingExpiration = false;
    });

builder.Services.AddAuthorization(auth =>
{
    // Nothing in this application is anonymous except the sign-in page, which opts out explicitly.
    // A fallback policy rather than per-page attributes: a new page that forgets its attribute is the
    // exact failure Track 7's controller-authorization sweep found in the API.
    auth.FallbackPolicy = auth.DefaultPolicy;
});

builder.Services.AddRazorPages(razor =>
{
    razor.Conventions.AllowAnonymousToPage("/SignIn");
    razor.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddAntiforgery(anti => anti.HeaderName = "RequestVerificationToken");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// The page text is English and the product ships English and Brazilian Portuguese resources, so the
// portal honours Accept-Language across those two and falls back to en-US. Without this the numbers
// and dates take the *host's* culture while the labels stay English — a reviewer on an English page
// reading "5,4" for a score of five point four, which is the state this was found in.
var supportedCultures = new[] { "en-US", "pt-BR" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

app.UseStaticFiles();
app.UseRouting();

// The same header set the API sends, for the same reasons. A portal is a browser-facing surface with
// write access to the risk register, so the clickjacking and MIME-sniffing defences matter here more
// than they do on an API.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; " +
        "form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// A liveness probe, so a load balancer has something to poll that does not need a session.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

Log.Information("NetRisk Risk Portal starting; API at {ApiUrl}", apiUrl);

app.Run();
