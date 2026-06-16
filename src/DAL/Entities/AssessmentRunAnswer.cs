using System;

namespace DAL.Entities;

public partial class AssessmentRunAnswer
{
    public int Id { get; set; }

    public int AssessmentRunId { get; set; }

    public int AssessmentQuestionId { get; set; }

    public string? AnswerContentJson { get; set; }

    public DateTime LastUpdatedAt { get; set; }

    public virtual AssessmentRun AssessmentRun { get; set; } = null!;

    public virtual AssessmentQuestion AssessmentQuestion { get; set; } = null!;
}
