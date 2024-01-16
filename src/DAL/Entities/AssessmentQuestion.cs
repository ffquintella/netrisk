using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class AssessmentQuestion
{
    public int Id { get; set; }

    public int AssessmentId { get; set; }

    public string Question { get; set; } = null!;

    public int Order { get; set; }

    public virtual Assessment Assessment { get; set; } = null!;

    public virtual ICollection<AssessmentAnswer> AssessmentAnswers { get; set; } = new List<AssessmentAnswer>();

    public virtual ICollection<AssessmentRunsAnswer> AssessmentRunsAnswers { get; set; } = new List<AssessmentRunsAnswer>();
}
