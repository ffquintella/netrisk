using AutoMapper;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.DTO;
using Splat;

namespace GUIClient;

public class MapperBootstrapper: BaseBootstrapper
{
    public static void RegisterServices(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {
        
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
        services.RegisterLazySingleton<IMapper>(() => mapper);
        
    }
}