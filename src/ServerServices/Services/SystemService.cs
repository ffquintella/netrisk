using System.Reflection;
using AutoMapper;
using DAL;
using Model.ClientData;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerServices.Services;

public class SystemService: ServiceBase, ISystemService
{

    public SystemService(ILogger logger, DALManager dalManager
    ): base(logger, dalManager)
    {
    }
    
    private ClientInformation? _clientInformation;

    public async Task<ClientInformation> GetClientInformation()
    {
        if (_clientInformation != null)
            return _clientInformation;
        
        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        
        var configPath = $"{currentDir}/ClientInformation.yaml";
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention( CamelCaseNamingConvention.Instance)  
            .Build();

        var yml = await System.IO.File.ReadAllTextAsync(configPath);
        
        var config = deserializer.Deserialize<ClientInformation>(yml);
        
        _clientInformation = config;

        return config;
    }

    private async Task<string> ReadDiskFileAsync(string filePath)
    {
        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        
        var fullPath = $"{currentDir}/{filePath}";
        
        var data = await System.IO.File.ReadAllTextAsync(fullPath);
        
        return data;
    }
    
    public async Task<string> GetUpdateScriptAsync(string osFamily)
    {
        switch (osFamily.ToLower())
        {
            case "windows":
                return await ReadDiskFileAsync("Scripts/Update-Windows.ps1");
            case "linux":
                return await ReadDiskFileAsync("Scripts/Update-Linux.sh");
            case "mac":
                return await ReadDiskFileAsync("Scripts/Update-Mac.sh");
            default:
                throw new InvalidParameterException("osFamily", "Unknown OS Family");
            
        }
    }
}