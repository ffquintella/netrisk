using ServerServices.Interfaces;

namespace BackgroundJobs.Jobs.Sync;

/// <summary>Setting keys + helpers shared by the website sync jobs.</summary>
public static class WebsiteSyncSettings
{
    public const string UrlKey = "WebsiteSyncUrl";
    public const string InsecureKey = "WebsiteSyncInsecure";
    public const string IntervalKey = "WebsiteSyncIntervalMinutes";
    public const string FastIntervalKey = "WebsiteFastSyncIntervalMinutes";

    public const int DefaultIntervalMinutes = 60;
    public const int DefaultFastIntervalMinutes = 2;

    public static async Task<string?> GetUrlAsync(ISettingsService settings)
        => await GetValueAsync(settings, UrlKey);

    public static async Task<bool> GetInsecureAsync(ISettingsService settings)
        => string.Equals(await GetValueAsync(settings, InsecureKey), "true", StringComparison.OrdinalIgnoreCase);

    public static async Task<string?> GetValueAsync(ISettingsService settings, string key)
    {
        try
        {
            if (!await settings.ConfigurationKeyExistsAsync(key)) return null;
            return await settings.GetConfigurationKeyValueAsync(key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Converts a minute interval to a Hangfire cron expression.</summary>
    public static string MinutesToCron(int minutes)
    {
        if (minutes < 1) minutes = 1;
        if (minutes < 60) return $"*/{minutes} * * * *";
        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return hours >= 24 ? "0 0 * * *" : $"0 */{hours} * * *";
        }
        return $"*/{minutes} * * * *";
    }
}
