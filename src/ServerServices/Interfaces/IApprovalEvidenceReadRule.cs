using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Who reads a file attached as approval evidence to a configuration record whose read rule lives with that record — today
/// the IT Risk Committee's minutes on an index profile (<c>nr_files.risk_index_profile_id</c>, S40 §7.3, §11.3). The files
/// authorizer asks; the record's own module answers, so the authorizer never reads that module's tables itself.
/// </summary>
public interface IApprovalEvidenceReadRule
{
    /// <summary>Null when <paramref name="user"/> may read evidence of <paramref name="parentId"/>; else the refusal's code.</summary>
    /// <param name="heldPermissions">The user's permissions read from the database, never the token's.</param>
    Task<string?> RefusalAsync(int parentId, User user, IReadOnlyCollection<string> heldPermissions, CancellationToken ct = default);
}
