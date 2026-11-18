namespace Model.Exceptions;

/// <summary>
/// A request to the entity map the API refuses with a stable, machine-readable <see cref="Error"/> and the HTTP status it
/// answers with, rendered as <c>{ error, message }</c> — plus <c>details.entityId</c> when <see cref="EntityId"/> names
/// the row refused (S40 §8.5). <see cref="EntityWriteRefusedException"/> is the write-side refusal (T241, T292); this base
/// also carries the refusals of the reads that are not writes, such as <c>GET /Entities/{id}/History</c>.
/// </summary>
public class EntityRequestRefusedException(string error, string message, int statusCode = 403, int? entityId = null)
    : Exception(message)
{
    /// <summary>The stable code (<c>Model.Entities.EntityWriteErrors</c>, <c>Model.Entities.EntityRiskContextErrors</c>).</summary>
    public string Error { get; } = error;

    /// <summary>400 for a malformed request, 403 for a missing permission or scope, 404 for an entity not found.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>The entity whose row was refused, when the refusal names one (a batch line, a missing entity).</summary>
    public int? EntityId { get; } = entityId;
}
