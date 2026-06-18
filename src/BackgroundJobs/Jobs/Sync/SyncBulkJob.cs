using Serilog;
using ServerServices.Interfaces;
using SyncContracts;

namespace BackgroundJobs.Jobs.Sync;

/// <summary>
/// Periodic bulk sync: pushes the website's display snapshot and pulls back queued visitor
/// actions, applying them to the main database. Drives the non-time-critical data.
/// </summary>
public class SyncBulkJob : IJob
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settings;
    private readonly ISyncPushBuilder _pushBuilder;
    private readonly ISyncClient _client;
    private readonly ISyncIngestService _ingest;

    public SyncBulkJob(ILogger logger, ISettingsService settings, ISyncPushBuilder pushBuilder,
        ISyncClient client, ISyncIngestService ingest)
    {
        _logger = logger;
        _settings = settings;
        _pushBuilder = pushBuilder;
        _client = client;
        _ingest = ingest;
    }

    public void Run() => RunAsync().GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var url = await WebsiteSyncSettings.GetUrlAsync(_settings);
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.Debug("Website sync URL not configured; skipping bulk sync");
            return;
        }
        var insecure = await WebsiteSyncSettings.GetInsecureAsync(_settings);

        try
        {
            var payload = await _pushBuilder.BuildBulkAsync();
            var response = await _client.PushAsync(url, payload, insecure);
            if (response == null)
            {
                _logger.Warning("Bulk sync push to website returned no response");
                return;
            }

            var applied = await _ingest.ApplyAsync(response.Actions);
            if (applied.Count > 0)
            {
                await _client.AckAsync(url, new AckRequest { AckedActionIds = applied }, insecure);
                _logger.Information("Bulk sync applied {Count} website actions", applied.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Bulk website sync failed");
        }
    }
}
