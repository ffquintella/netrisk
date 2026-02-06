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

    public string GetCurrentOsName()
    {
       
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return "windows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return "linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return "mac";
        }

        return "unknown";
    }
    
    public void  DownloadUpgradeScript()
    {
        var client = RestService.GetClient();
        
        var os = GetCurrentOsName();
        if(os == "unknown")
            throw new Exception("Unknown OS");
        
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
        using var client = RestService.GetClient();
        
        var os = GetCurrentOsName();
        if(os == "unknown")
            throw new Exception("Unknown OS");
        
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
            if(os == "windows") appPath = Path.Combine(tempDir, "NetRisk.exe");
            else appPath = Path.Combine(tempDir, "NetRisk");
            
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
        var tempDir = GetTempPath();
        
        var os = GetCurrentOsName();
        if(os == "unknown")
            throw new Exception("Unknown OS");
        
        string scriptPath;
        if(os == "windows") scriptPath = Path.Combine(tempDir, "update.bat");
        else scriptPath = Path.Combine(tempDir, "update.sh");
        
        string appPath;
        if(os == "windows") appPath = Path.Combine(tempDir, "NetRisk.exe");
        else appPath = Path.Combine(tempDir, "NetRisk");
        
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
