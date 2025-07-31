using AutoMapper;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.Extensions.Logging;
using Model.DTO;
using Serilog;
using Splat;
using ILogger = Splat.ILogger;

namespace GUIClient;

public class MapperBootstrapper: BaseBootstrapper
{
    public static void RegisterServices(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {
        
        var iLoggerFactory = resolver.GetService<ILoggerFactory>();
        
        var configuration = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Mitigation, MitigationDto>();
            cfg.CreateMap<AssessmentRun, AssessmentRunDto>();
            cfg.CreateMap<AssessmentQuestion, AssessmentQuestionDto>();
            cfg.CreateMap<AssessmentAnswer, AssessmentAnswerDto>();
            cfg.CreateMap<Report, ReportDto>();
        }, iLoggerFactory);

        var mapper = configuration.CreateMapper();
        services.RegisterLazySingleton<IMapper>(() => mapper);
        
    }
}