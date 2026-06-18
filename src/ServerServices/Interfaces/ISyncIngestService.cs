using SyncContracts;

namespace ServerServices.Interfaces;

/// <summary>
/// Applies website-originated outbox actions to the main database via the existing domain
/// services, idempotently (deduplicated by client action id). Returns the ids that are now
/// durable on the server so the caller can acknowledge them to the website.
/// </summary>
public interface ISyncIngestService
{
    Task<List<Guid>> ApplyAsync(IEnumerable<OutboxActionDto> actions);
}
