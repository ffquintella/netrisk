using DAL.Entities;

namespace DAL.EntitiesDto;

public class AssessmentAnswerDto: AssessmentAnswer
{
    public new  Assessment? Assessment { get; set; }
    public new  AssessmentQuestion? Question { get; set; }
}