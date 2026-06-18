using Serilog;
using ServerServices.Interfaces;
using SyncContracts;

namespace BackgroundJobs.Jobs.Sync;

/// <summary>
/// Frequent "fast lane" sync for the security-sensitive, time-critical path: pushes new
/// password-reset links and pulls back password changes / link deletes promptly so the
/// one-time-link guarantee and reset latency stay acceptable.
/// </summary>
public class SyncFastJob : IJob
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settings;
    private readonly ISyncPushBuilder _pushBuilder;
    private readonly ISyncClient _client;
    private readonly ISyncIngestService _ingest;

    public SyncFastJob(ILogger logger, ISettingsService settings, ISyncPushBuilder pushBuilder,
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
            _logger.Debug("Website sync URL not configured; skipping fast sync");
            return;
        }
        var insecure = await WebsiteSyncSettings.GetInsecureAsync(_settings);

        try
        {
            var payload = await _pushBuilder.BuildFastAsync();
            var response = await _client.FastPushAsync(url, payload, insecure);
            if (response == null) return;

            var applied = await _ingest.ApplyAsync(response.Actions);
            if (applied.Count > 0)
            {
                await _client.AckAsync(url, new AckRequest { AckedActionIds = applied }, insecure);
                _logger.Information("Fast sync applied {Count} website actions", applied.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fast website sync failed");
        }
    }
}
