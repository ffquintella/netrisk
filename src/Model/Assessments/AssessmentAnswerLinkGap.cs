namespace Model.Assessments;

/// <summary>
/// One run answer that carries text but names no option (M58, T280; S39 D-16) — the report the version 114 backfill
/// leaves for a person to resolve.
/// </summary>
public class AssessmentAnswerLinkGap
{
    /// <summary>Reasons, as <see cref="Reason"/> carries them.</summary>
    public const string NoMatch = "no_match", Ambiguous = "ambiguous", Matchable = "matchable";

    public int RunAnswerId { get; set; }
    public int RunId { get; set; }
    public int AssessmentId { get; set; }
    public int QuestionId { get; set; }

    /// <summary>The decoded answer text.</summary>
    public string AnswerText { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="NoMatch"/>: no option of the question has the text; <see cref="Ambiguous"/>: several do;
    /// <see cref="Matchable"/>: exactly one does now (the options changed after the answer was saved), not yet linked.
    /// </summary>
    public string Reason { get; set; } = NoMatch;
}
