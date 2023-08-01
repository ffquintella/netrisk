using System.Reflection;
using DAL;
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
    
    public async Task<EntitiesConfiguration> GetEntitiesConfigurationAsync()
    {
        var currentDir = Assembly.GetExecutingAssembly().AssemblyDirectory();
        
        var configPath = $"{currentDir}/EntitiesConfiguration.yaml";
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention( CamelCaseNamingConvention.Instance)  
            .Build();

        var yml = await System.IO.File.ReadAllTextAsync(configPath);
        
        var config = deserializer.Deserialize<EntitiesConfiguration>(yml);

        return config;
    }
}