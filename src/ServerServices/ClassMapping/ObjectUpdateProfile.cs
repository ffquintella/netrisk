using AutoMapper;
using DAL.Entities;


namespace ServerServices.ClassMapping;

public class ObjectUpdateProfile: Profile
{
    public ObjectUpdateProfile()
    {
        CreateMap<Risk, Risk>();
        CreateMap<RiskScoring, RiskScoring>();
        CreateMap<Mitigation, Mitigation>();
        CreateMap<NrFile, NrFile>();
    }
}