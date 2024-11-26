using AutoMapper;
using DAL.Entities;

namespace ServerServices.ClassMapping;

public class IncidentResposePlanProfile: Profile
{
    public IncidentResposePlanProfile()
    {
        CreateMap<IncidentResponsePlan, IncidentResponsePlan>()
            .ForMember(irp => irp.CreatedBy, opt => opt.Ignore())
            .ForMember(irp => irp.CreatedById, opt => opt.Ignore())
            .ForMember(irp => irp.Attachments, opt => opt.Ignore())
            .ForMember(irp => irp.Executions, opt => opt.Ignore())
            .ForMember(irp => irp.Id, opt => opt.Ignore())
            .ForMember(irp => irp.UpdatedBy, opt => opt.Ignore())
            .ForMember(irp => irp.UpdatedById, opt => opt.Ignore())
            ;


        CreateMap<IncidentResponsePlanTask, IncidentResponsePlanTask>();
        
        CreateMap<IncidentResponsePlanExecution, IncidentResponsePlanExecution>()
            .ForMember(e => e.CreatedBy, opt => opt.Ignore())
            .ForMember(e => e.ExecutedBy, opt => opt.Ignore())
            .ForMember(e => e.LastUpdatedBy, opt => opt.Ignore());
    }
}