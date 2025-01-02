using System.Reflection;
using System.Text.RegularExpressions;
using AutoMapper;
using DAL;
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
    
    readonly IMapper _mapper;
    public EntitiesService(ILogger logger, IDalService dalService,
        IMapper mapper
    ): base(logger, dalService)
    {
        _mapper = mapper;
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
        
        var prop = _mapper.Map(property, new EntitiesProperty());
        
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
        
        oldProp = _mapper.Map(property, oldProp);
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
        
        _mapper.Map(entity, dbEntity);

        foreach (var property in entity.EntitiesProperties)
        {
            UpdateEntitiesProperty(property);
        }
        
        dbContext.SaveChanges();
    }

    public void UpdateEntitiesProperty(EntitiesProperty property)
    {
        using var dbContext = DalService.GetContext();

        var dbProperty = dbContext.EntitiesProperties.FirstOrDefault(ep => ep.Id == property.Id);
        if(dbProperty == null) throw new DataNotFoundException("EntitiesProperty", property.Id.ToString(), new Exception("EntitiesProperty not found"));
        
        _mapper.Map(property, dbProperty);
        
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