using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class AssessmentQuestion
{
    public int Id { get; set; }

    public int AssessmentId { get; set; }

    public string Question { get; set; } = "";

    public int Order { get; set; }

    public int? ParentQuestionId { get; set; }

    public int PageNumber { get; set; } = 1;

    public string? ConditionJson { get; set; }

    public string? ExplanationMarkdown { get; set; }

    public virtual Assessment Assessment { get; set; } = null!;

    public virtual AssessmentQuestion? ParentQuestion { get; set; }

    public virtual ICollection<AssessmentQuestion> SubQuestions { get; set; } = new List<AssessmentQuestion>();

    public virtual ICollection<AssessmentAnswer> AssessmentAnswers { get; set; } = new List<AssessmentAnswer>();

    public virtual ICollection<AssessmentRunsAnswer> AssessmentRunsAnswers { get; set; } = new List<AssessmentRunsAnswer>();
}
