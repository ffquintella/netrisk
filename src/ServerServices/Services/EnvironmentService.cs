using System.Security.Cryptography;
using System.Text;
using Serilog;
using ServerServices.Interfaces;
using Tools;
using SysEnv = System.Environment;

namespace ServerServices.Services;

public class EnvironmentService: IEnvironmentService
{
    public EnvironmentService()
    {
        if (OperatingSystem.IsLinux())
        {
            ApplicationData = "/var/netrisk";
        }
        if(OperatingSystem.IsWindows())
        {
            ApplicationData = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData);
        }
        if(OperatingSystem.IsMacOS())
        {
            ApplicationData = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData);
        }
    }
    
    private string ApplicationData { get; set; } = "";

    public string ApplicationDataFolder => ApplicationData + @"/NRServer";
    
    public string ServerSecretToken
    {
        get
        {
            Directory.CreateDirectory(ApplicationDataFolder);
            if(!File.Exists(ApplicationDataFolder + @"/secret_token.txt"))
            {
                var token = RandomGenerator.RandomString(37);
                
                var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(token));
                var key = Convert.ToBase64String(hmac.Key);
                
                File.WriteAllText(ApplicationDataFolder + @"/secret_token.txt", key  );
            }
            
            return File.ReadAllText(ApplicationDataFolder + @"/secret_token.txt");
        }
    }
    
}