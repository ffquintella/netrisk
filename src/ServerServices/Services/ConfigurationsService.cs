using DAL.Entities;
using Model.DTO;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class ConfigurationsService: ServiceBase, IConfigurationsService
{
    
    public ConfigurationsService(ILogger logger, IDalService dalService) : base(logger, dalService)
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

    // Website sync setting keys (kept in sync with BackgroundJobs.Jobs.Sync.WebsiteSyncSettings).
    private const string SyncUrlKey = "WebsiteSyncUrl";
    private const string SyncInsecureKey = "WebsiteSyncInsecure";
    private const string SyncIntervalKey = "WebsiteSyncIntervalMinutes";
    private const string SyncFastIntervalKey = "WebsiteFastSyncIntervalMinutes";

    public WebsiteSyncConfigDto GetWebsiteSyncConfig()
    {
        using var context = DalService.GetContext();
        var settings = context.Settings
            .Where(s => s.Name == SyncUrlKey || s.Name == SyncInsecureKey
                        || s.Name == SyncIntervalKey || s.Name == SyncFastIntervalKey)
            .ToDictionary(s => s.Name, s => s.Value);

        var config = new WebsiteSyncConfigDto();
        if (settings.TryGetValue(SyncUrlKey, out var url)) config.Url = url ?? "";
        if (settings.TryGetValue(SyncInsecureKey, out var insecure))
            config.Insecure = string.Equals(insecure, "true", StringComparison.OrdinalIgnoreCase);
        if (settings.TryGetValue(SyncIntervalKey, out var interval) && int.TryParse(interval, out var i) && i >= 1)
            config.IntervalMinutes = i;
        if (settings.TryGetValue(SyncFastIntervalKey, out var fast) && int.TryParse(fast, out var f) && f >= 1)
            config.FastIntervalMinutes = f;
        return config;
    }

    public void UpdateWebsiteSyncConfig(WebsiteSyncConfigDto config)
    {
        using var context = DalService.GetContext();
        SetSetting(context, SyncUrlKey, config.Url);
        SetSetting(context, SyncInsecureKey, config.Insecure ? "true" : "false");
        SetSetting(context, SyncIntervalKey, config.IntervalMinutes.ToString());
        SetSetting(context, SyncFastIntervalKey, config.FastIntervalMinutes.ToString());
        context.SaveChanges();
    }

    private static void SetSetting(DAL.Context.AuditableContext context, string name, string value)
    {
        var setting = context.Settings.FirstOrDefault(s => s.Name == name);
        if (setting == null)
            context.Settings.Add(new Setting { Name = name, Value = value });
        else
            setting.Value = value;
    }
}