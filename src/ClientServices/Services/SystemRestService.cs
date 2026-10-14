using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientServices.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Model.ClientData;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class SystemRestService: RestServiceBase, ISystemService
{
    private ILoggerFactory _factory;
    private IConfiguration _configuration;
    public SystemRestService(IRestService restService) : base(restService)
    {
        _factory = GetService<ILoggerFactory>();
        _configuration = GetService<IConfiguration>();
    }

    public string GetClientAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        if(assembly == null)
            throw new Exception("Error getting client version");
        
        var version = assembly.GetName()!.Version?.ToString()!;
        return version;
    }

    public bool NeedsUpgrade()
    {
#if DEBUG
        return false;
#else
        var client = RestService.GetClient();
        
        var request = new RestRequest("/System/ClientVersion");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                Logger.Error("Error getting client version");
                throw new Exception("Error getting client version");
            }
            
            return response != GetClientAssemblyVersion();
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error client version message: {Message}", ex.Message);
            throw new RestComunicationException("Error client version", ex);
        }
#endif
    }
    
    public async Task<bool> NeedsUpgradeAsync()
    {
#if DEBUG
        return false;
#else
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/System/ClientVersion");
        
        try
        {
            var response = await client.GetAsync<string>(request);

            if (response == null)
            {
                Logger.Error("Error getting client version");
                throw new Exception("Error getting client version");
            }
            
            return response != GetClientAssemblyVersion();
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error client version message: {Message}", ex.Message);
            throw new RestComunicationException("Error client version", ex);
        }
#endif
    }

    public string GetTempPath()
    {
        var tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NRGUIClient", "Temp");

        Directory.CreateDirectory(tempDir);
        
        return tempDir;
    }

    // Virtual so a test can stand in for a platform it is not running on (an Intel Mac, T341).
    public virtual string GetCurrentOsName()
    {
        OSPlatform? platform =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
            null;

        return OsNameFor(platform, RuntimeInformation.OSArchitecture);
    }

    /// <summary>
    /// The name the API knows a platform by. An Intel Mac still reports
    /// <see cref="ClientPlatforms.MacIntel"/> although NetRisk no longer ships for it (T341): that is
    /// the truth, and reporting it is what lets the server refuse it instead of answering it with the
    /// Apple Silicon build.
    /// </summary>
    internal static string OsNameFor(OSPlatform? platform, Architecture architecture)
    {
        if (platform == OSPlatform.Windows) return ClientPlatforms.Windows;
        if (platform == OSPlatform.Linux) return ClientPlatforms.Linux;
        if (platform == OSPlatform.OSX)
            return architecture == Architecture.Arm64 ? ClientPlatforms.MacArm64 : ClientPlatforms.MacIntel;

        return "unknown";
    }

    public bool IsCurrentPlatformSupported() => !ClientPlatforms.IsRetired(GetCurrentOsName());

    /// <summary>
    /// The platform name an upgrade request is made for. Refuses before anything is asked of the
    /// server on a platform NetRisk no longer ships for, so a client there can never fetch, write or
    /// run an update script or installer built for another architecture, whatever the server answers.
    /// </summary>
    private string UpgradeOsName()
    {
        var os = GetCurrentOsName();
        if(os == "unknown")
            throw new Exception("Unknown OS");

        if (ClientPlatforms.IsRetired(os))
            throw new PlatformNotSupportedException(ClientPlatforms.MacIntelUnsupportedMessage);

        return os;
    }
    
    public void  DownloadUpgradeScript()
    {
        var os = UpgradeOsName();

        var client = RestService.GetClient();
        
        var request = new RestRequest($"/System/UpdateScript/{os}");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                Logger.Error("Error getting update script");
                throw new Exception("Error getting update script");
            }

            var tempDir = GetTempPath();
            
            string scriptPath;
            if(os == "windows") scriptPath = Path.Combine(tempDir, "update.bat");
            else scriptPath = Path.Combine(tempDir, "update.sh");
            
            File.WriteAllText(scriptPath, response);
            

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting update script: {Message}", ex.Message);
            throw new RestComunicationException("Error getting update script", ex);
        }
    }

    public void DownloadApplication()
    {
        var os = UpgradeOsName();

        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/System/ClientDownloadLocation/{os}");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                Logger.Error("Error getting client download location");
                throw new Exception("Error getting client download location");
            }

            var tempDir = GetTempPath();
            
            string appPath;
            if (os == "windows")
                appPath = Path.Combine(tempDir, "NetRisk.exe");
            else if (os.StartsWith("mac", StringComparison.OrdinalIgnoreCase))
                appPath = Path.Combine(tempDir, "NetRisk.dmg");
            else
                appPath = Path.Combine(tempDir, "NetRisk.zip");
            
            DownloadFile(new Uri(response), appPath);

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error client download location: {Message}", ex.Message);
            throw new RestComunicationException("Error client download location", ex);
        }
    }

    public void ExecuteUpgrade()
    {
        var os = UpgradeOsName();

        var tempDir = GetTempPath();
        
        string scriptPath;
        if(os == "windows") scriptPath = Path.Combine(tempDir, "update.bat");
        else scriptPath = Path.Combine(tempDir, "update.sh");
        
        string appPath;
        if (os == "windows")
            appPath = Path.Combine(tempDir, "NetRisk.exe");
        else if (os.StartsWith("mac", StringComparison.OrdinalIgnoreCase))
            appPath = Path.Combine(tempDir, "NetRisk.dmg");
        else
            appPath = Path.Combine(tempDir, "NetRisk.zip");
        
        int nProcessID = Process.GetCurrentProcess().Id;
        
        Process p = new Process();
        p.StartInfo.FileName = scriptPath;
        p.StartInfo.WorkingDirectory = tempDir;
        p.StartInfo.Arguments = $"{nProcessID} {appPath}";
        p.Start();
        
        Environment.Exit(0);
    }
    
    public void DownloadFile(Uri url, string outputFilePath)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest(url.ToString());
        try
        {

            var fileData = client.DownloadData(request);

            if (fileData == null)
            {
                Logger.Error("Error getting application");
                throw new Exception("Error getting application");
            }
            
            
            File.WriteAllBytes(outputFilePath, fileData);

        }catch(Exception ex)
        {
            Logger.Error("Error downloading file: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
     
        
    }
    
}
