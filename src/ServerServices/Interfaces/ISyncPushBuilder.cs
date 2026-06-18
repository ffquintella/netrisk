using SyncContracts;

namespace ServerServices.Interfaces;

/// <summary>Builds the display snapshots the server pushes to the website's sync endpoint.</summary>
public interface ISyncPushBuilder
{
    Task<PushPayload> BuildBulkAsync();
    Task<FastPushPayload> BuildFastAsync();
}
