using AutoMapper;
using DAL.Entities;
using DAL.EntitiesDto;
using Model.DTO;


namespace ServerServices.ClassMapping;

public class ObjectUpdateProfile: Profile
{
    public ObjectUpdateProfile()
    {
        //CreateMap<Risk, Risk>();
        CreateMap<RiskScoring, RiskScoring>();
        CreateMap<Mitigation, Mitigation>();
        CreateMap<NrFile, NrFile>();
        CreateMap<Host, Host>();
        CreateMap<Vulnerability, Vulnerability>().ForMember(e => e.Actions, opt => opt.Ignore());
        CreateMap<HostsService, HostsService>();
        CreateMap<Assessment, Assessment>();
        CreateMap<AssessmentRun, AssessmentRun>();
        CreateMap<AssessmentRunDto, AssessmentRun>();
        CreateMap<AssessmentQuestionDto, AssessmentQuestion>();
        CreateMap<AssessmentAnswerDto, AssessmentAnswer>();
        CreateMap<Message, Message>();
    }
}