using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Assessment : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public DateTime Created { get; set; }

    public int? EntityId { get; set; }
    public virtual Entity? Entity { get; set; }

    public virtual ICollection<AssessmentAnswer> AssessmentAnswers { get; set; } = new List<AssessmentAnswer>();

    public virtual ICollection<AssessmentQuestion> AssessmentQuestions { get; set; } = new List<AssessmentQuestion>();

    public virtual ICollection<AssessmentRun> AssessmentRuns { get; set; } = new List<AssessmentRun>();
}
