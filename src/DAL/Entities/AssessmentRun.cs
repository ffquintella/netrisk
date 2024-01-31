using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class AssessmentRun
{
    public int Id { get; set; }

    public int AssessmentId { get; set; }

    public int EntityId { get; set; }

    public DateTime? RunDate { get; set; }

    public int? AnalystId { get; set; }

    public int Status { get; set; }

    public string? Comments { get; set; }

    public int? HostId { get; set; }

    public virtual User? Analyst { get; set; }

    public virtual Assessment Assessment { get; set; } = null!;

    public virtual ICollection<AssessmentRunsAnswer> AssessmentRunsAnswers { get; set; } = new List<AssessmentRunsAnswer>();

    public virtual Entity Entity { get; set; } = null!;

    public virtual Host? Host { get; set; }
}
