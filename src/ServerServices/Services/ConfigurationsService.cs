using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class ConfigurationsService: ServiceBase, IConfigurationsService
{
    
    public ConfigurationsService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }
    
    public void UpdateBackupPassword(string password)
    {
        using var context = DalService.GetContext();
        
        var configuration = context.Settings.FirstOrDefault(s => s.Name == "BackupPassword");
        
        if(configuration == null)
        {
            configuration = new Setting()
            {
                Name = "BackupPassword",
                Value = password
            };
            context.Settings.Add(configuration);
        }
        else
        {
            configuration.Value = password;
        }

        context.SaveChanges();

    }

    public string GetBackupPassword()
    {
        using var context = DalService.GetContext();
        var configuration = context.Settings.FirstOrDefault(s => s.Name == "BackupPassword");
        return configuration == null ? "" : configuration.Value!;
    }
}