namespace Model.Exceptions;

/// <summary>
/// A write to the entity map the caller may not make (T241, T292). Carries the machine-readable
/// <see cref="EntityRequestRefusedException.Error"/> (<c>Model.Entities.EntityWriteErrors</c>,
/// <c>Model.Entities.EntityRiskContextErrors</c>) and the HTTP status the API answers with — 403 for a missing permission
/// or scope, 400 for a malformed tree change or batch line, 404 for a batch line naming an entity that does not exist.
/// </summary>
public class EntityWriteRefusedException(string error, string message, int statusCode = 403, int? entityId = null)
    : EntityRequestRefusedException(error, message, statusCode, entityId);
