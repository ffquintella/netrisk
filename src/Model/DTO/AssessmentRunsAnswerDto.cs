using DAL.Entities;

namespace Model.DTO;

public class AssessmentRunsAnswerDto: AssessmentRunsAnswer
{
#pragma warning disable CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    public override AssessmentRun? Run { get; set; }
    public override AssessmentQuestion? Question { get; set; }
    public override AssessmentAnswer? Answer { get; set; }
#pragma warning restore CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
}