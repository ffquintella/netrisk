namespace Model.Exceptions;

/// <summary>
/// A write to the entity map the caller may not make (T241, T292). Carries the machine-readable
/// <see cref="Error"/> (<c>Model.Entities.EntityWriteErrors</c>) and the HTTP status the API answers with — 403
/// for a missing permission or scope, 400 for a malformed tree change.
/// </summary>
public class EntityWriteRefusedException(string error, string message, int statusCode = 403) : Exception(message)
{
    public string Error { get; } = error;

    public int StatusCode { get; } = statusCode;
}
