using System.IO;
using ClientServices.Interfaces;
using DeviceId;
using Tools;
using SysEnv = System.Environment;

namespace ClientServices.Services;

public class EnvironmentService: IEnvironmentService
{
    public string NewLine => SysEnv.NewLine;

    public bool Is64BitProcess => SysEnv.Is64BitProcess;
    
    public string ApplicationData => SysEnv.GetFolderPath(SysEnv.SpecialFolder.ApplicationData);

    public string ApplicationDataFolder => Path.Combine(ApplicationData , @"NRGUIClient");


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