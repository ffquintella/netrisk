using System;
using API;
using Mapster;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Model.DTO;
using Serilog;
using Serilog.Extensions.Logging;
using ServerServices.Governance;
using ServerServices.Integrations;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Sieve.Models;
using Sieve.Services;
using HostsService = ServerServices.Services.HostsService;
using ILogger = Serilog.ILogger;

namespace ServerServices.Tests.DI;

public class ServiceRegistration
{
    public static IServiceProvider GetServiceProvider()
    {

        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        
        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;
        
        var services = new ServiceCollection();
        
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());
        services.AddSingleton(MockDalService.Create());
        
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<ICommentsService, CommentsService>();
        services.AddTransient<IHostsService, HostsService>();
        services.AddTransient<IClientRegistrationService, ClientRegistrationService>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<IRisksService, RisksService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        services.AddTransient<IMessagesService, MessagesService>();
        services.AddTransient<IIncidentResponsePlansService, IncidentResponsePlansService>();
        services.AddTransient<IIrpAutomationService, IrpAutomationService>();
        services.AddTransient<IIncidentsService, IncidentsService>();
        services.AddTransient<IAssessmentsService, AssessmentsService>();
        services.AddTransient<IEmailService, EmailMock>();
        services.AddTransient<IFilesService, FilesServiceMock>();
        services.AddTransient<IEntitiesService, EntitiesService>();
        services.AddScoped<ISieveProcessor, ApplicationSieveProcessor>();
        services.AddSingleton(MockConfiguration.Create());
        services.AddSingleton<ILocalizationService>(new LocalizationService(factory, typeof(ApplicationSieveProcessor).Assembly));

        // Track 4 (Integrations): the domain services now raise notification events, so the graph has
        // to resolve here too. The outbound HTTP client is a fake and the protector uses a fixed root
        // secret, so nothing reaches a real host and nothing writes to the install's key file.
        services.AddSingleton<IOutboundHttpClient>(new Mock.FakeOutboundHttpClient());
        services.AddSingleton<ISecretProtector>(new SecretProtector(logger, "netrisk-test-root-secret"));
        services.AddTrack4Integrations(includeOutboundHttp: false);

        // Track 8 (Risk governance). Registered as the hosts do, so a test exercises the same
        // enforcement graph the API and the job host resolve.
        services.AddTrack8Governance();
        
        
        services.Configure<SieveOptions>((sieveOptions =>
        {
            sieveOptions.DefaultPageSize = 100;
            sieveOptions.MaxPageSize = 1000;
            sieveOptions.ThrowExceptions = true;
            sieveOptions.CaseSensitive = false;
            sieveOptions.IgnoreNullsOnNotEqual = true;
        }));
        
        
        // Registrar AutoMapper usando os perfis do assembly principal e outros perfis relevantes

        return services.BuildServiceProvider();
    }
}
