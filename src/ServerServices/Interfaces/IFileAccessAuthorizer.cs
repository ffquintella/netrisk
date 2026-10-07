using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Decides whether a caller may read one attachment (security finding NR-2026-017), and whether they
/// may attach a new one to a parent record (NR-2026-035).
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

    /// <summary>
    /// Whether the caller may attach a new file to the parent record <paramref name="file"/> names
    /// (security finding NR-2026-035). Called before a file is created, with the request body.
    ///
    /// Returns normally for a file with no parent — it is readable only by its uploader, so it reaches
    /// nobody else's record — and when the parent is visible in the caller's entity scope and the
    /// caller may write to it. Throws <see cref="Model.Exceptions.UserNotAuthorizedException"/> when the
    /// parent is missing, outside the caller's scope or not writable by them (one outcome for all three,
    /// so the call is not an existence oracle), and
    /// <see cref="Model.Exceptions.InvalidParameterException"/> when the file names more than one parent.
    /// </summary>
    Task EnsureCanAttachAsync(NrFile file, User user);
}
