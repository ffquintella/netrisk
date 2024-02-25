using DAL.Entities;

namespace DAL.EntitiesDto;

public class AssessmentQuestionDto: AssessmentQuestion
{
    
    //#pragma warning disable CS8764 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
    public new  Assessment? Assessment { get; set; }
    public new  string? Question { get; set; }

    public new List<AssessmentAnswerDto> AssessmentAnswers { get; set; } = new List<AssessmentAnswerDto>();
    //#pragma warning restore CS8764
}