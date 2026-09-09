using System.Reflection;
using System.Text.RegularExpressions;
using Mapster;
using DAL;
using DAL.Context;
using DAL.Entities;
using Model.Entities;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.EntityFrameworkCore;


namespace ServerServices.Services;

public class EntitiesService: ServiceBase, IEntitiesService
{
    
    public EntitiesService(ILogger logger, IDalService dalService
    ): base(logger, dalService)
    {
    }
    
    private EntitiesConfiguration? _entitiesConfiguration;
    
    public async Task<EntitiesConfiguration> GetEntitiesConfigurationAsync()
    {
        if (_entitiesConfiguration != null)
            return _entitiesConfiguration;
        
        //var currentDir = Assembly.GetExecutingAssembly().AssemblyDirectory();
        
        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        
        if(currentDir == null) currentDir = "/netrisk";
        
        var configPath = $"{currentDir}/EntitiesConfiguration.yaml";
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention( CamelCaseNamingConvention.Instance)  
            .Build();

        var yml = await System.IO.File.ReadAllTextAsync(configPath);
        
        var config = deserializer.Deserialize<EntitiesConfiguration>(yml);
        
        _entitiesConfiguration = config;

        return config;
    }

    private EntitiesConfiguration GetConfig()
    {
        return _entitiesConfiguration ??= GetEntitiesConfigurationAsync().Result;
    }

    public Entity CreateInstance(int userId, string entityDefinitionName, int parentEntityId = 0)
    {
        GetConfig();
        Entity entity;
        if (parentEntityId == 0)
        {
            entity = new Entity()
            {
                DefinitionName = entityDefinitionName,
                Created  = DateTime.Now,
                Updated = DateTime.Now,
                DefinitionVersion = _entitiesConfiguration!.Version,
                CreatedBy = userId,
                UpdatedBy = userId,
                Status = "active",
            };
        }
        else
        {
            entity = new Entity()
            {
                DefinitionName = entityDefinitionName,
                Created  = DateTime.Now,
                Updated = DateTime.Now,
                DefinitionVersion = _entitiesConfiguration!.Version,
                CreatedBy = userId,
                UpdatedBy = userId,
                Status = "active",
                Parent = parentEntityId
            };
        }


        using var dbContext = DalService.GetContext();
        
        var result = dbContext.Entities.Add(entity);

        dbContext.SaveChanges();

        return result.Entity;

    }

    public void ValidatePropertyList(string entityDefinitionName, List<EntitiesPropertyDto> properties)
    {
        GetConfig();
        var definition = 
            _entitiesConfiguration!.Definitions[entityDefinitionName];
        if(definition == null) throw new Exception($"Entity definition {entityDefinitionName} not found");
        
        // Check if all required properties are present

        foreach (var key in definition.Properties.Keys)
        {
            if (definition.Properties[key].Nullable == false)
            {
                if(properties.FirstOrDefault(p=> p.Type == key) == null)
                    throw new Exception($"Property {key} is required");
            }
        }

        foreach (var property in properties)
        {
            ValidateProperty(entityDefinitionName, property);
        }
        
        
    }

    private void ValidateProperty(string entityDefinitionName, EntitiesPropertyDto property)
    {
        GetConfig();
        var definition = 
            _entitiesConfiguration!.Definitions[entityDefinitionName];
        if(definition == null) throw new Exception($"Entity definition {entityDefinitionName} not found");
        
        var propType = definition.Properties[property.Type];
        
        if(propType == null) throw new Exception($"Property type {property.Type} not found");

        if(propType.Nullable == false && property.Value == null) throw new Exception("Value is required");
        
        switch (propType.Type)
        {
            case "String":
                break;
            case "Boolean":
                if(!bool.TryParse(property.Value, out _))
                    throw new Exception("Value must be a boolean");
                break;
            case "Integer":
                if(!Int32.TryParse(property.Value, out _))
                    throw new Exception("Value must be a integer");
                break;
            default:
                if (propType.Type.StartsWith("Definition"))
                {
                    var defType = Regex.Match(propType.Type, @"\(([^)]*)\)").Groups[1].Value;
                    if(!_entitiesConfiguration.Definitions.Keys.Contains(defType)) throw new Exception("Unknown definition type");
                    if (property.Value == "Parent") break;
                    if(!Int32.TryParse(property.Value, out _))
                        throw new Exception("Value must be a integer");
                    break;
                }
                
                throw new Exception("Unknown property type");
        }

        if (propType.MaxSize > 0 && property.Value.Length > propType.MaxSize)
            throw new Exception("Value is too long");
        
        if (property.Value.Length < propType.MinSize)
            throw new Exception("Value is too short");
        

    }

    public void TryDeleteEntitiesProperty(int propertyId)
    {
        using var dbContext = DalService.GetContext();
        
        var ep = dbContext.EntitiesProperties.FirstOrDefault(e => e.Id == propertyId);
        if (ep == null) return;
        DeleteEntitiesProperty(propertyId);
    }

    public void TryDeleteEntitiesProperty(string type, int entityId)
    {
        using var dbContext = DalService.GetContext();
        
        var epList = dbContext.EntitiesProperties.Where(e => e.Type == type && e.Entity == entityId).ToList();
        if (epList.Count == 0) return;

        foreach (var ep in epList)
        {
            DeleteEntitiesProperty(ep.Id);
        }
        
    }

    public void DeleteEntitiesProperty(int propertyId)
    {
        using var dbContext = DalService.GetContext();
        
        var ep = dbContext.EntitiesProperties.FirstOrDefault(e => e.Id == propertyId);
        if(ep == null) throw new DataNotFoundException("EntitiesProperty", propertyId.ToString(), new Exception("EntitiesProperty not found"));
        
        dbContext.EntitiesProperties.Remove(ep);
        dbContext.SaveChanges();
    }
    
    public EntitiesProperty CreateProperty(string entityDefinitionName, ref Entity entity, EntitiesPropertyDto property)
    {
        
        GetConfig();
        ValidateProperty(entityDefinitionName, property);

        // If we are creating this must be 0
        property.Id = 0; 
        
        var definition = 
            _entitiesConfiguration!.Definitions[entityDefinitionName];
        if(definition == null) throw new Exception($"Entity definition {entityDefinitionName} not found");

        var propType = definition.Properties[property.Type];
        
        if (!propType.Multiple)
        {
            if(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == property.Type) != null)
                throw new Exception("Property already exists");
        }
        
        using var dbContext = DalService.GetContext();
        
        var prop = property.Adapt<EntitiesProperty>();
        
        if(propType.Type.StartsWith("Definition") &&  property.Value == "Parent")
        {
            if(entity.Parent == null) throw new Exception("Parent is required");
            prop.Value = entity.Parent.ToString()!;
        }
        
        prop.OldValue = "";

        prop.Entity = entity.Id;
        
        var result = dbContext.EntitiesProperties.Add(prop);
        
        dbContext.SaveChanges();
        
        entity.EntitiesProperties.Add(result.Entity);

        return result.Entity;
    }

    public EntitiesProperty UpdateProperty(ref Entity entity, EntitiesPropertyDto property, bool save=true)
    {
        using var dbContext = DalService.GetContext();

        
        var oldProp = dbContext.EntitiesProperties.FirstOrDefault(p => p.Id == property.Id);
        if(oldProp == null) throw new DataNotFoundException("EntityProperty" , property.Id.ToString(), new Exception("EntityProperty not found"));

        var entityDefinitionName = entity.DefinitionName;
        
        GetConfig();
        ValidateProperty(entityDefinitionName, property);

        dbContext.EntitiesProperties.Update(oldProp);
        
        var oldVal = oldProp.Value;
        
        //oldProp = property.Adapt(oldProp);
        
        dbContext.Entry(oldProp).CurrentValues.SetValues(property);
        
        oldProp.OldValue = oldVal;

        if(save) dbContext.SaveChanges();
        
        //entity.EntitiesProperties.Add(oldProp);   

        return oldProp;
        
    }

    public void UpdateEntity(Entity entity)
    {
        using var dbContext = DalService.GetContext();

        var dbEntity = dbContext.Entities.FirstOrDefault(e => e.Id == entity.Id);
        if(dbEntity == null) throw new DataNotFoundException("Entity", entity.Id.ToString(), new Exception("Entity not found"));

        // Only the entity's own columns. This used to be entity.Adapt(dbEntity), which also copied
        // EntitiesProperties onto a *tracked* entity: Mapster built a fresh EntitiesProperty per
        // item, EF discovered them through the navigation as Added, and SaveChanges died with
        // "another instance with the same key value for {'Id'} is already being tracked" whenever
        // the incoming list held a row twice. The property bag is owned by ReplaceProperties and the
        // Create/UpdateProperty pair, which persist through their own contexts.
        dbEntity.DefinitionName = entity.DefinitionName;
        dbEntity.DefinitionVersion = entity.DefinitionVersion;
        dbEntity.Created = entity.Created;
        dbEntity.Updated = entity.Updated;
        dbEntity.CreatedBy = entity.CreatedBy;
        dbEntity.UpdatedBy = entity.UpdatedBy;
        dbEntity.Status = entity.Status;
        dbEntity.Parent = entity.Parent;

        // Distinct: a caller that appended the same row twice must not make this write it twice,
        // and a row that was never persisted has no id to look up.
        foreach (var propertyId in entity.EntitiesProperties
                     .Where(p => p.Id > 0).Select(p => p.Id).Distinct().ToList())
        {
            var property = entity.EntitiesProperties.First(p => p.Id == propertyId);
            UpdateEntitiesProperty(property);
        }
        
        dbContext.SaveChanges();
    }

    /// <summary>
    /// Reconciles <paramref name="entity"/>'s property bag against <paramref name="properties"/>,
    /// which is treated as the complete set of properties the entity should have.
    ///
    /// Matching is done by property <em>type and value</em>, never by the id the caller sent. A
    /// client holds the ids it read when it loaded the form, and those go stale — the previous
    /// update path deleted and re-inserted every row of a multi-valued property, so the next save
    /// from the same open form asked to update a row that no longer existed and the request failed
    /// with "EntityProperty not found". Reconciling by value also makes a repeated save a no-op
    /// instead of a churn of new row ids.
    /// </summary>
    public List<EntitiesProperty> ReplaceProperties(Entity entity, List<EntitiesPropertyDto> properties)
    {
        GetConfig();

        var definition = _entitiesConfiguration!.Definitions[entity.DefinitionName];
        if(definition == null) throw new Exception($"Entity definition {entity.DefinitionName} not found");

        foreach (var property in properties)
            ValidateProperty(entity.DefinitionName, property);

        // Whether a property holds one value or many is a fact about the definition. The old code
        // inferred it from how many rows the payload happened to carry, so a multi-valued property
        // with a single selection was treated as single-valued and one with none was ignored.
        foreach (var group in properties.GroupBy(p => p.Type))
        {
            if (!definition.Properties[group.Key].Multiple && group.Count() > 1)
                throw new Exception($"Property {group.Key} accepts a single value");
        }

        using var dbContext = DalService.GetContext();

        var existing = dbContext.EntitiesProperties.Where(p => p.Entity == entity.Id).ToList();
        var reconciled = new List<EntitiesProperty>();

        foreach (var (key, propType) in definition.Properties)
        {
            var current = existing.Where(p => p.Type == key).ToList();
            var desired = properties.Where(p => p.Type == key).ToList();

            if (!propType.Multiple)
            {
                var wanted = desired.FirstOrDefault();

                if (wanted == null)
                {
                    // Absent from a complete property set means cleared.
                    foreach (var row in current) dbContext.EntitiesProperties.Remove(row);
                    continue;
                }

                // In place, so the row keeps its id and OldValue records what it held before.
                var row0 = current.FirstOrDefault();
                if (row0 == null)
                {
                    reconciled.Add(NewRow(dbContext, entity, propType, wanted));
                }
                else
                {
                    var value = ResolveValue(entity, propType, wanted);
                    if (row0.Value != value) row0.OldValue = row0.Value;
                    row0.Value = value;
                    row0.Name = wanted.Name;
                    reconciled.Add(row0);

                    // Legacy drift: a single-valued type that somehow accumulated extra rows.
                    foreach (var extra in current.Skip(1)) dbContext.EntitiesProperties.Remove(extra);
                }

                continue;
            }

            // Multi-valued: the payload's set of values wins. Rows already holding a wanted value
            // are kept as they are, so their ids survive the save.
            var wantedValues = desired
                .Select(d => ResolveValue(entity, propType, d))
                .Distinct()
                .ToList();

            var kept = new List<EntitiesProperty>();

            foreach (var value in wantedValues)
            {
                var dto = desired.First(d => ResolveValue(entity, propType, d) == value);

                // "not already kept" collapses legacy drift: two rows of the same type holding the
                // same value leave one row, not two.
                var row = current.FirstOrDefault(p => p.Value == value && !kept.Contains(p));

                if (row == null)
                {
                    reconciled.Add(NewRow(dbContext, entity, propType, dto));
                }
                else
                {
                    row.Name = dto.Name;
                    kept.Add(row);
                    reconciled.Add(row);
                }
            }

            foreach (var row in current.Where(row => !kept.Contains(row)))
                dbContext.EntitiesProperties.Remove(row);
        }

        dbContext.SaveChanges();

        // Rows whose type the definition no longer declares are left alone: an entity saved against
        // an older definition version still carries them, and this is an edit, not a migration.
        entity.EntitiesProperties.Clear();
        foreach (var row in reconciled) entity.EntitiesProperties.Add(row);

        return reconciled;
    }

    private EntitiesProperty NewRow(
        AuditableContext dbContext, Entity entity, EntityType propType, EntitiesPropertyDto property)
    {
        var row = new EntitiesProperty
        {
            Type = property.Type,
            Value = ResolveValue(entity, propType, property),
            OldValue = "",
            Name = property.Name,
            Entity = entity.Id
        };

        return dbContext.EntitiesProperties.Add(row).Entity;
    }

    /// <summary>
    /// The stored value for <paramref name="property"/>. A <c>Definition(...)</c> property whose
    /// value is the literal "Parent" stores the parent entity's id, as CreateProperty does.
    /// </summary>
    private static string ResolveValue(Entity entity, EntityType propType, EntitiesPropertyDto property)
    {
        if (!propType.Type.StartsWith("Definition") || property.Value != "Parent") return property.Value;

        if (entity.Parent == null) throw new Exception("Parent is required");
        return entity.Parent.ToString()!;
    }

    public void UpdateEntitiesProperty(EntitiesProperty property)
    {
        using var dbContext = DalService.GetContext();

        var dbProperty = dbContext.EntitiesProperties.FirstOrDefault(ep => ep.Id == property.Id);
        if(dbProperty == null) throw new DataNotFoundException("EntitiesProperty", property.Id.ToString(), new Exception("EntitiesProperty not found"));
        
        property.Adapt(dbProperty);
        
        dbContext.SaveChanges();
    }
    
    public List<Entity> GetEntities(string? entityDefinitionName = null, bool propertyLoad = false)
    {
        using var dbContext = DalService.GetContext();
        List<Entity> entities;
        if(entityDefinitionName == null)
            entities = propertyLoad ? dbContext.Entities.Include(e => e.EntitiesProperties)
                .AsParallel().OrderBy(e => e.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value).ToList() : dbContext.Entities.ToList();
        else
        {
            GetConfig();
            var hasDefinition = _entitiesConfiguration!.Definitions.ContainsKey(entityDefinitionName);
            if(!hasDefinition) throw new EntityDefinitionNotFoundException(entityDefinitionName);
            entities = propertyLoad ? dbContext.Entities.Include(e => e.EntitiesProperties).Where(e => e.DefinitionName == entityDefinitionName).ToList() : dbContext.Entities.Where(e => e.DefinitionName == entityDefinitionName).ToList();
        }

        return entities;

    }

    public Entity GetEntity(int id)
    {
        using var dbContext = DalService.GetContext();

        var entity = dbContext.Entities.Include(e => e.EntitiesProperties).FirstOrDefault(e => e.Id == id);
        
        //dbContext.Entry(entity!).Collection(e => e.EntitiesProperties).Load();
        
        if(entity == null ) throw new DataNotFoundException("entities", id.ToString(), new Exception("Entity not found"));

        return entity; 
    }

    public Entity DeleteEntity(int id)
    {
        using var dbContext = DalService.GetContext();

        var entity = dbContext.Entities.FirstOrDefault(e => e.Id == id);
        
        if(entity == null ) throw new DataNotFoundException("entities", id.ToString(), new Exception("Entity not found"));

        dbContext.Entities.Remove(entity);
        
        dbContext.SaveChanges();

        return entity; 
    }
}