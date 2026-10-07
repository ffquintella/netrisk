using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Assessments;

namespace ClientServices.Interfaces;

/// <summary>
/// A comment and evidence files on each answer of an assessment run (GitHub #80, T297, S44).
///
/// Every method throws on failure rather than returning null: <see cref="Model.Exceptions.DataNotFoundException"/>
/// for a 404, <see cref="Model.Exceptions.InvalidHttpRequestException"/> carrying the server's reason for a
/// 400/403/409, <see cref="Model.Exceptions.RestComunicationException"/> when the server was not reached.
/// Evidence is downloaded with <see cref="IFilesService.DownloadFileAsync"/>.
/// </summary>
public interface IAssessmentEvidenceService
{
    /// <summary>Sets — or, with null or blank text, clears — the comment on one answer.</summary>
    Task<AssessmentRunAnswer> SaveCommentAsync(int runId, int questionId, string? comment);

    /// <summary>Every evidence file of the run, with the question each answers.</summary>
    Task<List<AssessmentAnswerEvidence>> GetRunEvidenceAsync(int runId);

    /// <summary>
    /// Uploads a local file as evidence on one answer. The size is checked here first, against the same
    /// <see cref="AssessmentEvidencePolicy"/> the server enforces, so an oversized file is refused before
    /// a byte is sent.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">The file is empty or over the cap.</exception>
    Task<AssessmentAnswerEvidence> UploadEvidenceAsync(int runId, int questionId, Uri filePath);

    /// <summary>Deletes one evidence file of one answer.</summary>
    Task DeleteEvidenceAsync(int runId, int questionId, string uniqueName);
}
