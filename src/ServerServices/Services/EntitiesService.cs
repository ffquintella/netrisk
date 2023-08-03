using System.Reflection;
using System.Text.Json;
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
    public EntitiesService(ILogger logger, DALManager dalManager
    ): base(logger, dalManager)
    {
        
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

    public Entity CreateEntity(int userId, string entityDefinitionName)
    {
        if (_entitiesConfiguration == null) _entitiesConfiguration = GetEntitiesConfigurationAsync().Result;
        
        var entity = new Entity()
        {
            DefinitionName = entityDefinitionName,
            Created  = DateTime.Now,
            Updated = DateTime.Now,
            DefinitionVersion = _entitiesConfiguration.Version,
            CreatedBy = userId,
            UpdatedBy = userId,
        };

        using var dbContext = DALManager.GetContext();
        
        dbContext.Entities.Add(entity);

        var properties = new List<EntitiesProperty>();
        
        foreach (var typeDef in _entitiesConfiguration.Definitions[entityDefinitionName])
        {
            var property = new EntitiesProperty()
            {
                Entity = entity.Id,
                Name = typeDef.Key,
                Type = typeDef.Value.Type,
                Value = ""
            };
            dbContext.EntitiesProperties.Add(property);
            entity.EntitiesProperties.Add(property);
        }

        //dbContext.SaveChanges();

        return entity;

    }
}