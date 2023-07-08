using System.Security.Cryptography;
using System.Text;
using ServerServices.Interfaces;
using Tools;
using SysEnv = System.Environment;

namespace ServerServices.Services;

public class EnvironmentService: IEnvironmentService
{
    private string ApplicationData => SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData);

    public string ApplicationDataFolder => ApplicationData + @"/SRServer";
    
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