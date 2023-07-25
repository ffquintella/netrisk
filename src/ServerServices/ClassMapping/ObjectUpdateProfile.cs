using AutoMapper;
using DAL.Entities;
using File = DAL.Entities.File;

namespace ServerServices.ClassMapping;

public class ObjectUpdateProfile: Profile
{
    public ObjectUpdateProfile()
    {
        CreateMap<Risk, Risk>();
        CreateMap<RiskScoring, RiskScoring>();
        CreateMap<Mitigation, Mitigation>();
        CreateMap<File, File>();
    }
}