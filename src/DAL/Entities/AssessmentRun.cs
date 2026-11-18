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

    public int ProgressPercentage { get; set; }

    public int CurrentPageIndex { get; set; } = 1;

    public string? Comments { get; set; }

    public int? HostId { get; set; }

    /// <summary>
    /// UTC: when the run moved into Submitted — <c>assessment_runs.submitted_at</c> (M58, T280; S39 D-16). Stamped by the
    /// server on that transition only; later saves of a submitted run (a comment, the analyst) leave it alone, and
    /// reopening the run clears it. Null for a run submitted before the column existed. <see cref="RunDate"/> is not
    /// this: it is the date the run was created with.
    /// </summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// The run's score, computed by the server when the run is submitted — <c>assessment_runs.score</c> (M58, T280; S39
    /// §3.1): Σ risk score of the chosen option ÷ Σ highest risk score of the question, over the visible questions
    /// answered with a linked option, in [0, 1]. Null when nothing scorable was answered, while the run is not
    /// submitted, and for runs submitted before the column existed.
    /// </summary>
    public double? Score { get; set; }

    public virtual User? Analyst { get; set; }

    public virtual Assessment Assessment { get; set; } = null!;

    public virtual ICollection<AssessmentRunsAnswer> AssessmentRunsAnswers { get; set; } = new List<AssessmentRunsAnswer>();

    public virtual Entity Entity { get; set; } = null!;

    public virtual Host? Host { get; set; }
}
