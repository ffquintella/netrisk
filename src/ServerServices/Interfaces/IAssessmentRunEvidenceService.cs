using DAL.Entities;
using Model.Assessments;

namespace ServerServices.Interfaces;

/// <summary>
/// A comment and evidence files on each answer of an assessment run (GitHub #80, T297, S44).
///
/// Every method finds the run through the caller's entity scope, so a run of another entity is
/// <see cref="Model.Exceptions.DataNotFoundException"/>, and every write refuses a submitted run with
/// <see cref="Model.Exceptions.RuleBrokenException"/> (<c>run_submitted</c>).
/// </summary>
public interface IAssessmentRunEvidenceService
{
    /// <summary>
    /// Sets — or, with null or blank text, clears — the comment on the answer to
    /// <paramref name="questionId"/>, creating the answer row (unanswered) when there is none yet.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">The comment is too long.</exception>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such run, or the question is not one of its assessment's.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException">The run is submitted.</exception>
    Task<AssessmentRunAnswer> SaveCommentAsync(int runId, int questionId, string? comment);

    /// <summary>Every evidence file of the run, oldest first, with the question each answers. Never the content.</summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such run.</exception>
    Task<List<AssessmentAnswerEvidence>> GetRunEvidenceAsync(int runId);

    /// <summary>
    /// Completes an upload staged through <c>POST /Files/local/chunk</c> as evidence on the answer to
    /// <paramref name="questionId"/>, creating the answer row when there is none yet.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">Bad name, type, upload id, chunk count or size.</exception>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such run or question, or the chunks are missing.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException">The run is submitted, or the answer already has the maximum number of files.</exception>
    Task<AssessmentAnswerEvidence> AttachEvidenceAsync(int runId, int questionId,
        AssessmentEvidenceUploadRequest request, User user);

    /// <summary>Deletes one evidence file of the answer to <paramref name="questionId"/>.</summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such run, or the file is not evidence on that answer.</exception>
    /// <exception cref="Model.Exceptions.PermissionInvalidException">The caller is neither the uploader nor an administrator.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException">The run is submitted.</exception>
    Task DeleteEvidenceAsync(int runId, int questionId, string uniqueName, User user);
}
