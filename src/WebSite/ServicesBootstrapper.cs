using System.Globalization;
using DAL;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServerServices.ClassMapping;
using ServerServices.Interfaces;
using ServerServices.Services;
using WebSite.Tools;

namespace WebSite;

public static class ServicesBootstrapper
{
    public static void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        AddGeneralServices(services, config);
        RegisterDependencyInjectionClasses(services, config);
    }
    
    private static void AddGeneralServices(IServiceCollection services,  IConfiguration config)
    {
        // Add services to the container.
        services.AddControllers();
        services.AddMemoryCache();
        services.AddMemoryCache(options =>
        {
            // Overall 1024 size (no unit)
            options.SizeLimit = 1024;
        });
        services.AddAutoMapper(typeof(ClientProfile));
        services.AddAutoMapper(typeof(ObjectUpdateProfile));
        services.AddAutoMapper(typeof(UserProfile));
        
        services.AddSingleton < LanguageService > ();
        services.AddLocalization(opts => { opts.ResourcesPath = "Resources"; });

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new List<CultureInfo>()
            {
                new CultureInfo("pt-BR"),
                new CultureInfo("en-US"),
            };
            options.DefaultRequestCulture = new RequestCulture(supportedCultures.First());
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        
    }
    
     private static void RegisterDependencyInjectionClasses(IServiceCollection services, IConfiguration config)
    {
        if(config == null) throw new Exception("Error loading configuration");
        
        services.AddSingleton<DALService>(_ => new DALService(config));
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<ILinksService, LinksService>();
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        //services.AddHostedService<SelfTest>();
        

    }
}