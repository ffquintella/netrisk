using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace API;

public static class LocalizationBootstrapper
{
    public static void RegisterLocalization(IServiceCollection services, 
        IConfiguration config, 
        ILoggerFactory loggerFactory)
    {
        services.AddSingleton<ILocalizationService>(new LocalizationService(loggerFactory, typeof(LocalizationBootstrapper).Assembly));
    }
    
}