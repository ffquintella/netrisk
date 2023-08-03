using AutoMapper;
using DAL.Entities;
using Model.Entities;

namespace ServerServices.ClassMapping;

public class EntityProfile: Profile
{
    public EntityProfile()
    {
        CreateMap<EntitiesPropertyDto, EntitiesProperty>();
        CreateMap<Entity, Entity>();
    }
}