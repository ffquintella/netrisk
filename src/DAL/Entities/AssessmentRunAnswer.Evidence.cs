namespace DAL.Entities;

/// <summary>
/// The assessor's comment on one answer of a run (GitHub #80, T297, S44), stored in
/// <c>assessment_run_answers.comment</c>. The evidence files of the answer are <see cref="NrFile"/> rows
/// pointing back here through <see cref="NrFile.AssessmentRunAnswerId"/>.
///
/// Kept in a partial so the generated entity stays regenerable from the database.
/// </summary>
public partial class AssessmentRunAnswer
{
    /// <summary>
    /// Free text explaining the answer, at most <c>AssessmentEvidencePolicy.MaxCommentLength</c>
    /// characters (enforced by the API). Null when the assessor wrote nothing.
    /// </summary>
    public string? Comment { get; set; }
}
