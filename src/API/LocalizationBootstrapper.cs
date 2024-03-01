using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using ServerServices.Services;

namespace API;

public static class LocalizationBootstrapper
{
    public static void RegisterLocalization(IServiceCollection services, 
        IConfiguration config, 
        ILoggerFactory loggerFactory)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[]
            {
                new CultureInfo("en-US"),
                new CultureInfo("pt-BR"),
                // Add more cultures here
            };

            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });
        
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), loggerFactory);
        services.AddSingleton<IStringLocalizer>(factory.Create("SharedResource", "API"));
    }
    
}