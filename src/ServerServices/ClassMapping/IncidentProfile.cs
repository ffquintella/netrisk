using AutoMapper;
using DAL.Entities;

namespace ServerServices.ClassMapping;

public class IncidentProfile: Profile
{
    public IncidentProfile()
    {
        CreateMap<Incident, Incident>();
    }

}