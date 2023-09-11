using System.Text.Json.Serialization;
using API.Security;
using API.Tools;
using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Logging;
using Model.Configuration;
using ServerServices.ClassMapping;
using ServerServices.Interfaces;
using ServerServices.Services;
using SharedServices.Interfaces;
using SharedServices.Services;

namespace API;

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
        //services.AddControllers();
        services.AddControllers().AddJsonOptions(x =>
            x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        //services.AddEndpointsApiExplorer();
        //services.AddSwaggerGen();
        services.AddAutoMapper(typeof(ClientProfile));
        services.AddAutoMapper(typeof(ObjectUpdateProfile));
        services.AddAutoMapper(typeof(UserProfile));
        services.AddAutoMapper(typeof(EntityProfile));
        services.AddAutoMapper(typeof(MgmtReviewProfile));
       
        services.AddFluentEmail(config["email:from"]!)
            .AddRazorRenderer()
            .AddSmtpSender(config["email:smtp:server"]!, Int32.Parse(config["email:smtp:port"]!));
        services.AddMemoryCache();
        services.AddMemoryCache(options =>
        {
            // Overall 1024 size (no unit)
            options.SizeLimit = 1024;
        });
        //services.AddResponseCaching();
    }

    private static void RegisterDependencyInjectionClasses(IServiceCollection services, IConfiguration config)
    {
        if(config == null) throw new Exception("Error loading configuration");
        
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddHostedService<SelfTest>();
        
        services.AddSingleton<IClientRegistrationService, ClientRegistrationService>();
        services.AddSingleton<IAuthorizationHandler, ValidUserRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, UserInRoleRequirementHandler>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IAssessmentsService, AssessmentsService>();
        services.AddSingleton<DALManager>(_ => new DALManager(config));

        var availableLocales = config.GetSection("languages:availableLocales");
        if (availableLocales == null) throw new Exception("Error invalid configuration");
        var defaultLocale = config.GetSection("languages:defaultLocale");
        if (defaultLocale == null) throw new Exception("Error invalid configuration");
        
        var langConf = new LanguagesConfiguration
        {
            AvailableLocales = availableLocales.Get<string[]>()!.ToList(),
            DefaultLocale = defaultLocale.Get<string>()!
        };

        services.AddSingleton<ILanguageManager>(_ => new LanguageManager(langConf));
        
        services.AddTransient<IEmailService, EmailService>();
        
        services.AddTransient<IMitigationsService, MitigationsService>();
        services.AddTransient<ITeamsService, TeamsService>();
        services.AddTransient<IRisksService, RisksService>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<ILinksService, LinksService>();
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<IAssetsService, AssetsService>();
        services.AddTransient<IFilesService, FilesService>();
        services.AddTransient<IEntitiesService, EntitiesService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        services.AddTransient<IMgmtReviewsService, MgmtReviewsService>();
        services.AddSingleton<ISystemService, SystemService>();
    }
}