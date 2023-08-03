using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using DAL;
using DAL.Entities;
using Model.Entities;
using Serilog;
using ServerServices.Interfaces;
using Tools.Extensions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerServices.Services;

public class EntitiesService: ServiceBase, IEntitiesService
{
    
    IMapper _mapper;
    public EntitiesService(ILogger logger, DALManager dalManager,
        IMapper mapper
    ): base(logger, dalManager)
    {
        _mapper = mapper;
    }
    
    private EntitiesConfiguration? _entitiesConfiguration;
    
    public async Task<EntitiesConfiguration> GetEntitiesConfigurationAsync()
    {
        if (_entitiesConfiguration != null)
            return _entitiesConfiguration;
        
        var currentDir = Assembly.GetExecutingAssembly().AssemblyDirectory();
        
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

    public Entity CreateInstance(int userId, string entityDefinitionName)
    {
        GetConfig();
        
        var entity = new Entity()
        {
            DefinitionName = entityDefinitionName,
            Created  = DateTime.Now,
            Updated = DateTime.Now,
            DefinitionVersion = _entitiesConfiguration.Version,
            CreatedBy = userId,
            UpdatedBy = userId,
            Status = "active",
        };

        using var dbContext = DALManager.GetContext();
        
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

        foreach (var key in definition.Keys)
        {
            if (definition[key].Nullable == false)
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
        
        var propType = definition[property.Type];
        
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
                }
                
                throw new Exception("Unknown property type");
        }

        if (propType.MaxSize > 0 && property.Value.Length > propType.MaxSize)
            throw new Exception("Value is too long");
        
        if (property.Value.Length < propType.MinSize)
            throw new Exception("Value is too short");
        

    }
    
    public EntitiesProperty CreateProperty(string entityDefinitionName, ref Entity entity, EntitiesPropertyDto property)
    {
        
        GetConfig();
        
        var definition = 
            _entitiesConfiguration!.Definitions[entityDefinitionName];
        if(definition == null) throw new Exception($"Entity definition {entityDefinitionName} not found");
        
        var propType = definition[property.Type];
        
        ValidateProperty(entityDefinitionName, property);
        
        if (!propType.Multiple)
        {
            if(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == property.Type) != null)
                throw new Exception("Property already exists");
        }
        
        using var dbContext = DALManager.GetContext();
        
        var prop = _mapper.Map(property, new EntitiesProperty());
        prop.OldValue = "";

        prop.Entity = entity.Id;
        
        var result = dbContext.EntitiesProperties.Add(prop);
        
        dbContext.SaveChanges();
        
        entity.EntitiesProperties.Add(result.Entity);

        return result.Entity;
    }

    public List<Entity> GetEntities()
    {
        using var dbContext = DALManager.GetContext();
        
        var entities = dbContext.Entities.ToList();

        return entities;

    }
}