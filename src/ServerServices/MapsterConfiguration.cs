using Mapster;
using System.Text;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.DTO;

namespace ServerServices;

public static class MapsterConfiguration
{
    public static void RegisterMappings()
    {
        TypeAdapterConfig<User, UserDto>.NewConfig()
            .Map(dest => dest.Email,
                src => src.Email != null
                    ? Encoding.UTF8.GetString(src.Email)
                    : null)
            .Map(dest => dest.UserName, src => src.Login)
            .Map(dest => dest.Id, src => src.Value);
        
        TypeAdapterConfig<UserDto, User>.NewConfig()
            .Map(dest => dest.Email, src => src.Email != null
                ? Encoding.UTF8.GetBytes(src.Email)
                : null)
            .Map(dest => dest.Login, src => src.UserName)
            .Map(dest => dest.Value, src => src.Id);
        
        TypeAdapterConfig<AssessmentAnswer, AssessmentAnswerDto>.NewConfig()
            .Map(dest => dest.Assessment, src => src.Assessment)
            .Map(dest => dest.Question, src => src.Question);

        TypeAdapterConfig<AssessmentAnswerDto, AssessmentAnswer>.NewConfig()
            .Map(dest => dest.Assessment, src => src.Assessment)
            .Map(dest => dest.Question, src => src.Question);

        TypeAdapterConfig<AssessmentQuestion, AssessmentQuestionDto>.NewConfig()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.Question, src => src.Question)
            .Map(dest => dest.AssessmentId, src => src.AssessmentId);
        
        TypeAdapterConfig<AssessmentQuestionDto, AssessmentQuestion>.NewConfig()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.Question, src => src.Question)
            .Map(dest => dest.AssessmentId, src => src.AssessmentId);




        // Avoid recursive/adverse navigation mapping on EF entities
        _ = TypeAdapterConfig<Risk, Risk>.NewConfig()
            .Ignore(dest => dest.CategoryNavigation!)
            .Ignore(dest => dest.SourceNavigation!)
            .Ignore(dest => dest.Mitigation!)
            .Ignore(dest => dest.Mitigations)
            .Ignore(dest => dest.Comments)
            .Ignore(dest => dest.MgmtReviews)
            .Ignore(dest => dest.Entities)
            .Ignore(dest => dest.RiskCatalogs)
            .Ignore(dest => dest.Vulnerabilities)
            .Ignore(dest => dest.IncidentResponsePlan!);

        _ = TypeAdapterConfig<Category, Category>.NewConfig()
            .Ignore(dest => dest.Risks);

        _ = TypeAdapterConfig<Source, Source>.NewConfig()
            .Ignore(dest => dest.Risks);
    }
}
