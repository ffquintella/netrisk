using DAL;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServerServices.ClassMapping;
using ServerServices.Interfaces;
using ServerServices.Services;

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
    }
    
     private static void RegisterDependencyInjectionClasses(IServiceCollection services, IConfiguration config)
    {
        if(config == null) throw new Exception("Error loading configuration");
        
        services.AddSingleton<DALManager>(_ => new DALManager(config));
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<ILinksService, LinksService>();
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        //services.AddHostedService<SelfTest>();
        

    }
}