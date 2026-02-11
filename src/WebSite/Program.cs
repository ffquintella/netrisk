using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using WebSite;

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
    options.Listen(IPAddress.Any, httpsPort, listenOptions =>
    {
        listenOptions.UseHttps(certificateFile, certificatePassword);
    } );
});

// Add services to the container.
builder.Services.AddControllersWithViews();

Bootstrapper.Register(builder.Services, config);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

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

app.Run();
