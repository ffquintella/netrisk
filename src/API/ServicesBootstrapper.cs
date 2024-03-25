using System.Text.Json.Serialization;
using API.Security;
using API.Tools;
using Microsoft.AspNetCore.Authorization;
//using Microsoft.Extensions.DependencyInjection.Extensions;
using Model.Configuration;
using ServerServices.ClassMapping;
using ServerServices.Interfaces;
using ServerServices.Interfaces.Importers;
using ServerServices.Services;
using ServerServices.Services.Importers;
using SharedServices.Interfaces;
using SharedServices.Services;
using Sieve.Models;
using Sieve.Services;

namespace API;

public static class ServicesBootstrapper
{
    public static void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        AddGeneralServices(services, config);
        RegisterDependencyInjectionClasses(services, config);
        ConfigureServices(services, config);
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
        services.AddAutoMapper(typeof(MitigationProfile));
        services.AddAutoMapper(typeof(RiskProfile));
        services.AddAutoMapper(typeof(HostsServiceProfile));
       
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
        
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddHostedService<SelfTest>();
        
        services.AddSingleton<IClientRegistrationService, ClientRegistrationService>();
        services.AddSingleton<IAuthorizationHandler, ValidUserRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, UserInRoleRequirementHandler>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IAssessmentsService, AssessmentsService>();
        services.AddSingleton<IConfiguration>(config);
        
        services.AddSingleton<DALService>();
        
        //services.AddTransient<DALService>();

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
        
        //services.AddScoped<ISieveCustomSortMethods, SieveCustomSortMethods>();
        //services.AddScoped<ISieveCustomFilterMethods, SieveCustomFilterMethods>();
        services.AddScoped<ISieveProcessor, ApplicationSieveProcessor>();
        
        services.AddTransient<IEmailService, EmailService>();
        services.AddTransient<IConfigurationsService, ConfigurationsService>();
        
        services.AddTransient<IMitigationsService, MitigationsService>();
        services.AddTransient<ITeamsService, TeamsService>();
        services.AddTransient<IRisksService, RisksService>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<ILinksService, LinksService>();
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<IFilesService, FilesService>();
        services.AddTransient<IEntitiesService, EntitiesService>();
        services.AddTransient<IReportsService, ReportsService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        services.AddTransient<IMgmtReviewsService, MgmtReviewsService>();
        services.AddTransient<IHostsService, HostsService>();
        services.AddTransient<IVulnerabilitiesService, VulnerabilitiesService>();
        services.AddTransient<ITechnologiesService, TechnologiesService>();
        services.AddTransient<IImpactsService, ImpactsService>();
        services.AddTransient<IVulnerabilityImporterFactory, ImporterFactory>();
        services.AddSingleton<ISystemService, SystemService>();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration config)
    {


        services.Configure<SieveOptions>((sieveOptions =>
        {
            sieveOptions.DefaultPageSize = 100;
            sieveOptions.MaxPageSize = 1000;
            sieveOptions.ThrowExceptions = true;
            sieveOptions.CaseSensitive = false;
            sieveOptions.IgnoreNullsOnNotEqual = true;
        }));
    }
}