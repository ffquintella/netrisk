using System.Globalization;
using System.Reflection;
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
        services.AddAutoMapper(cfg => { },
            typeof(ClientProfile),
            typeof(ObjectUpdateProfile),
            typeof(UserProfile)
        );
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

        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<IDalService, DalService>();
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<ILinksService, LinksService>();
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        services.AddTransient<IFixRequestsService, FixRequestsService>();
        services.AddTransient<ITeamsService, TeamsService>();
        services.AddTransient<ICommentsService, CommentsService>();
        services.AddTransient<IMessagesService, MessagesService>();
        services.AddTransient<IEntitiesService, EntitiesService>();
        services.AddTransient<IEmailService, EmailService>();
        services.AddFluentEmail("no@mail.com")
            .AddRazorRenderer()
            .AddSmtpSender("no.smtp.srv", 25);
        
        services.AddSingleton<ILocalizationService>(new LocalizationService(services.BuildServiceProvider().GetService<ILoggerFactory>(), Assembly.GetExecutingAssembly()));
        services.AddTransient<IIncidentResponsePlansService, IncidentResponsePlansService>();
        //services.AddHostedService<SelfTest>();
        

    }
}