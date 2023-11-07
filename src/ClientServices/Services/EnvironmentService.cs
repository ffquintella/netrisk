using System.IO;
using ClientServices.Interfaces;
using DeviceId;
using Tools;
using SysEnv = System.Environment;

namespace ClientServices.Services;

public class EnvironmentService: IEnvironmentService
{
    
    public EnvironmentService(string environment)
    {
        Environment = environment;
        ApplicationData = SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData);


        if (environment == "production")
        {
            ApplicationDataFolder = Path.Combine(ApplicationData , @"NRGUIClient");
        }
        else
        {
            var path = Path.Combine(ApplicationData, environment);
            ApplicationDataFolder = Path.Combine(path , @"NRGUIClient");
        }
        
    }
    
    public string Environment { get; }
    
    public string NewLine => SysEnv.NewLine;

    public bool Is64BitProcess => SysEnv.Is64BitProcess;
    
    public string ApplicationData { get; }

    public string ApplicationDataFolder { get; }


    public string DeviceToken
    {
        get
        {
            Directory.CreateDirectory(ApplicationDataFolder);
            var deviceTokenFile = Path.Combine(ApplicationDataFolder, "device_token.txt");
            if(!File.Exists(deviceTokenFile))
            {
                var token = RandomGenerator.RandomString(20);
                File.WriteAllText(deviceTokenFile, token  );
            }
            
            return File.ReadAllText(deviceTokenFile);
        }
    }
    
    public string DeviceID
    {
        get
        {
            Directory.CreateDirectory(ApplicationDataFolder);
            var deviceFile = Path.Combine(ApplicationDataFolder, "device_id.txt");
            if(!File.Exists(deviceFile))
            {
                string deviceId = new DeviceIdBuilder()
                    .AddMachineName()
                    .AddOsVersion()
                    .AddMacAddress()
                    .ToString();
                
                File.WriteAllText(deviceFile, deviceId  );
            }

            return File.ReadAllText(deviceFile);
            
            
        }
    }

    public string? GetEnvironmentVariable(string variableName) =>
        SysEnv.GetEnvironmentVariable(variableName);
}