using System.Reflection;
using DAL;
using Model.ClientData;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerServices.Services;

public class SystemService(ILogger logger, IDalService dalService) : ServiceBase(logger, dalService), ISystemService
{
    private ClientInformation? _clientInformation;

    public async Task<ClientInformation> GetClientInformation(string osFamily)
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
        switch (ClientPlatforms.Normalize(osFamily))
        {
            case ClientPlatforms.Windows:
                return await ReadDiskFileAsync("Scripts/Update-Windows.bat");
            case ClientPlatforms.Linux:
                return await ReadDiskFileAsync("Scripts/Update-Linux.sh");
            case ClientPlatforms.Mac:
            case ClientPlatforms.MacArm64:
                return await ReadDiskFileAsync("Scripts/Update-Mac.sh");
            // An Intel Mac is refused by name rather than by the default, so the client is told why:
            // NetRisk used to ship for it (T341) and its deployed clients still ask. It must never
            // fall into the Mac case above, which is the Apple Silicon script.
            case ClientPlatforms.MacIntel:
                Logger.Warning("Update script refused for a retired client platform ({Platform})", ClientPlatforms.MacIntel);
                throw new InvalidParameterException("osFamily", ClientPlatforms.MacIntelUnsupportedMessage);
            default:
                throw new InvalidParameterException("osFamily", "Unknown OS Family");
            
        }
    }
}
