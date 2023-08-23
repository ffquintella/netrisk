using System.Collections.Generic;
using DAL.Entities;

namespace GUIClient.Models.Entity;

public ref struct RefEntity
{
    private ref DAL.Entities.Entity _entity;
    
    
    public RefEntity(ref DAL.Entities.Entity entity)
    {
        _entity = ref entity;
    }
    
    public ICollection<EntitiesProperty> EntitiesProperties
    {
        get => _entity.EntitiesProperties;
        set => _entity.EntitiesProperties = value;
    }
}