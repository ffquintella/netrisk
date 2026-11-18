using System;

namespace DAL.Entities;

public partial class AssessmentRunAnswer
{
    public int Id { get; set; }

    public int AssessmentRunId { get; set; }

    public int AssessmentQuestionId { get; set; }

    public string? AnswerContentJson { get; set; }

    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// The option chosen — <c>assessment_run_answers.assessment_answer_id</c>, FK
    /// <c>fk_assessment_run_answers_assessment_answer_id</c> (M58, T280; S39 D-16). Before it, the answer was joined to its
    /// option by the text in <see cref="AnswerContentJson"/>. Null when the row holds no answer (only a comment or
    /// evidence), when the text matched no single option, or when the option was deleted.
    /// </summary>
    public int? AssessmentAnswerId { get; set; }

    public virtual AssessmentAnswer? AssessmentAnswer { get; set; }

    public virtual AssessmentRun AssessmentRun { get; set; } = null!;

    public virtual AssessmentQuestion AssessmentQuestion { get; set; } = null!;
}
