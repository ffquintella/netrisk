using System;
using API;
using AutoMapper;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Model.DTO;
using Serilog;
using Serilog.Extensions.Logging;
using ServerServices.Interfaces;
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
        services.AddTransient<IPermissionsService, PermissionsService>();
        services.AddTransient<IMessagesService, MessagesService>();
        services.AddTransient<IIncidentResponsePlansService, IncidentResponsePlansService>();
        services.AddTransient<IIncidentsService, IncidentsService>();
        services.AddScoped<ISieveProcessor, ApplicationSieveProcessor>();
        
        services.AddSingleton<ILocalizationService>(new LocalizationService(factory, typeof(ApplicationSieveProcessor).Assembly));
        
        
        services.Configure<SieveOptions>((sieveOptions =>
        {
            sieveOptions.DefaultPageSize = 100;
            sieveOptions.MaxPageSize = 1000;
            sieveOptions.ThrowExceptions = true;
            sieveOptions.CaseSensitive = false;
            sieveOptions.IgnoreNullsOnNotEqual = true;
        }));
        
        
        var configuration = new MapperConfiguration(cfg =>
        {
            //cfg.CreateMap<Cliente, ClienteListViewModel>();
            cfg.CreateMap<Mitigation, MitigationDto>();
            cfg.CreateMap<AssessmentRun, AssessmentRunDto>();
            cfg.CreateMap<AssessmentQuestion, AssessmentQuestionDto>();
            cfg.CreateMap<AssessmentAnswer, AssessmentAnswerDto>();
            cfg.CreateMap<Report, ReportDto>();
        });

        var mapper = configuration.CreateMapper();
        services.AddSingleton<IMapper>(mapper);

        return services.BuildServiceProvider();
    }
}