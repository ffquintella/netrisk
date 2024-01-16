using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Assessment
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public DateTime Created { get; set; }

    public virtual ICollection<AssessmentAnswer> AssessmentAnswers { get; set; } = new List<AssessmentAnswer>();

    public virtual ICollection<AssessmentQuestion> AssessmentQuestions { get; set; } = new List<AssessmentQuestion>();

    public virtual ICollection<AssessmentRun> AssessmentRuns { get; set; } = new List<AssessmentRun>();
}
