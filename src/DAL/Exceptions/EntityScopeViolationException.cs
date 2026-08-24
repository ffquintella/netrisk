using System;

namespace DAL.Exceptions;

/// <summary>
/// Thrown when a save would put a record into a business entity the caller has no claim to
/// (Track 2 milestone 2.3.1).
///
/// The model's query filters stop a caller from reading, updating or deleting another entity's
/// rows, but they cannot stop a caller from writing a <i>new</i> row stamped with someone else's
/// <c>entity_id</c>, or from re-stamping one of their own rows on the way out of scope. That is
/// what this guards.
/// </summary>
public class EntityScopeViolationException : Exception
{
    public EntityScopeViolationException(string entityType, int? entityId, string scope)
        : base($"A {entityType} cannot be saved into business entity " +
               $"{(entityId?.ToString() ?? "<unassigned>")}; the caller's scope is {scope}")
    {
        EntityType = entityType;
        EntityId = entityId;
        Scope = scope;
    }

    public string EntityType { get; }

    public int? EntityId { get; }

    public string Scope { get; }
}
