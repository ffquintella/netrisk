using System;
using ClientServices.Interfaces;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Model.DTO;
using NSubstitute;
using RestSharp;
using Serilog;
using Serilog.Extensions.Logging;
using Splat;
using ILogger = Serilog.ILogger;

namespace ClientServices.Tests.DI;

public class ServiceRegistration
{
    public static IServiceProvider GetServiceProvider()
    {

        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        
        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;
        
        var splat = Locator.CurrentMutable;
        
        var services = new ServiceCollection();
        
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());
        
        splat.RegisterLazySingleton<ILoggerFactory>(() => factory);
        splat.RegisterLazySingleton<ILogger>(() => new LoggerConfiguration().WriteTo.Console().CreateLogger());
        
        var mockClient = MockSetup.GetRestClient();
        services.AddSingleton<IRestClient>(mockClient);
        splat.RegisterLazySingleton<IRestClient>(() => mockClient);
        
        services.AddSingleton<IRestService>(MockSetup.GetRestService());
        services.AddTransient<IHostsService, ClientServices.Services.HostsRestService>();
        
        //services.AddSingleton(MockDalService.Create());
        
        //services.AddTransient<IRolesService, RolesService>();

        /*
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
        services.AddSingleton<IMapper>(mapper);*/

        return services.BuildServiceProvider();
    }
}