using DAL.Entities;

namespace Model.DTO;

public class AssessmentRunsAnswerDto: AssessmentRunsAnswer
{
    public override AssessmentRun? Run { get; set; }
    public override AssessmentQuestion? Question { get; set; }
    public override AssessmentAnswer? Answer { get; set; }
}