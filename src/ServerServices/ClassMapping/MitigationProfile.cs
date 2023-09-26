using AutoMapper;
using DAL.Entities;
using Model.DTO;

namespace ServerServices.ClassMapping;

public class MitigationProfile: Profile
{
    public MitigationProfile()
    {
        CreateMap<MitigationDto, Mitigation>();
    }
}