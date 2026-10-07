using Model.DTO;

namespace Model.Assessments;

/// <summary>
/// One evidence file on one answer of an assessment run (GitHub #80, S44), as the API lists it.
///
/// A <see cref="FileListing"/>, so the client's existing download path takes it unchanged:
/// <see cref="FileListing.Type"/> is the file type's display name (its MIME type) and
/// <see cref="FileListing.OwnerId"/> the uploader. Never carries the content.
/// </summary>
public class AssessmentAnswerEvidence : FileListing
{
    /// <summary>The question the evidence answers.</summary>
    public int QuestionId { get; set; }

    /// <summary>The answer row (<c>assessment_run_answers.id</c>) the file hangs off.</summary>
    public int AssessmentRunAnswerId { get; set; }

    /// <summary>Size in bytes.</summary>
    public int Size { get; set; }
}

/// <summary>Body of <c>PUT /Assessments/runs/{runId}/questions/{questionId}/comment</c>.</summary>
public class AssessmentAnswerCommentRequest
{
    /// <summary>The comment; null or blank clears it.</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Body of <c>POST /Assessments/runs/{runId}/questions/{questionId}/evidence</c>: completes an upload
/// whose content was staged in chunks through <c>POST /Files/local/chunk</c>.
/// </summary>
public class AssessmentEvidenceUploadRequest
{
    /// <summary>The staging id the chunks were sent under (<c>GET /Files/local/id</c>).</summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>How many chunks were staged.</summary>
    public int TotalChunks { get; set; }

    /// <summary>The file's name as the assessor picked it; the server keeps only its last segment.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The <c>file_types.value</c> the client resolved for the file, as a string.</summary>
    public string Type { get; set; } = string.Empty;
}
