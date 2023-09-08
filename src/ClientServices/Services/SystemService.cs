using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientServices.Interfaces;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class SystemService: ServiceBase, ISystemService
{
    public SystemService(IRestService restService) : base(restService)
    {
    }

    public string GetClientAssemblyVersion()
    {
        return Assembly.GetExecutingAssembly()!.GetName()!.Version?.ToString()!;
    }

    public bool NeedsUpgrade()
    {
        return true;
        
        var client = _restService.GetClient();
        
        var request = new RestRequest("/System/ClientVersion");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                _logger.Error("Error getting client version");
                throw new Exception("Error getting client version");
            }
            
            return response != GetClientAssemblyVersion();
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error client version message: {Message}", ex.Message);
            throw new RestComunicationException("Error client version", ex);
        }
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
    
    public void DownloadUpgradeScript()
    {
        var client = _restService.GetClient();
        
        var os = GetCurrentOsName();
        if(os == "unknown")
            throw new Exception("Unknown OS");
        
        var request = new RestRequest($"/System/UpdateScript/{os}");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                _logger.Error("Error getting update script");
                throw new Exception("Error getting update script");
            }

            var tempDir = GetTempPath();
            
            string scriptPath;
            if(os == "windows") scriptPath = Path.Combine(tempDir, "update.ps1");
            else scriptPath = Path.Combine(tempDir, "update.sh");
            
            File.WriteAllText(scriptPath, response);

        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting update script: {Message}", ex.Message);
            throw new RestComunicationException("Error getting update script", ex);
        }
    }

    public void DownloadApplication()
    {
        var client = _restService.GetClient();
        
        var os = GetCurrentOsName();
        if(os == "unknown")
            throw new Exception("Unknown OS");
        
        var request = new RestRequest($"/System/ClientDownloadLocation/{os}");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                _logger.Error("Error getting client download location");
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
            _logger.Error("Error client download location: {Message}", ex.Message);
            throw new RestComunicationException("Error client download location", ex);
        }
    }
    
    public void DownloadFile(Uri url, string outputFilePath)
    {
        const int BUFFER_SIZE = 16 * 1024;
        using (var outputFileStream = File.Create(outputFilePath, BUFFER_SIZE))
        {
            var req = WebRequest.Create(url);
            using (var response = req.GetResponse())
            {
                using (var responseStream = response.GetResponseStream())
                {
                    var buffer = new byte[BUFFER_SIZE];
                    int bytesRead;
                    do
                    {
                        bytesRead = responseStream.Read(buffer, 0, BUFFER_SIZE);
                        outputFileStream.Write(buffer, 0, bytesRead);
                    } while (bytesRead > 0);
                }
            }
        }
    }
    
}