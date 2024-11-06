using AutoMapper;
using DAL.Entities;

namespace ServerServices.ClassMapping;

public class RiskProfile: Profile
{
    public RiskProfile()
    {
        CreateMap<Risk, Risk>()
            .ForMember(r => r.RiskCatalogs, opt => opt.Ignore())
            .ForMember(r => r.CategoryNavigation, opt => opt.Ignore())
            .ForMember(r => r.SourceNavigation, opt => opt.Ignore());
            
            

    }
}