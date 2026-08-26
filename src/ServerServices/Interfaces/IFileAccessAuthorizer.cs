using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Decides whether a caller may read one attachment (security finding NR-2026-017).
///
/// A service of its own rather than a method on <see cref="IFilesService"/>: the files service is
/// consumed by background jobs and by the report renderer, which legitimately read attachments with
/// no user in hand, and folding the check into every read would have meant giving those callers a
/// bypass parameter — which is how a control ends up being passed <c>false</c> everywhere.
/// </summary>
public interface IFileAccessAuthorizer
{
    /// <summary>
    /// Returns normally when the caller may read the file; throws
    /// <see cref="Model.Exceptions.UserNotAuthorizedException"/> otherwise.
    /// </summary>
    Task EnsureCanReadAsync(NrFile file, User user);
}
