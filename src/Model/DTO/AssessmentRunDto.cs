using DAL.Entities;

namespace Model.DTO;

public class AssessmentRunDto: AssessmentRun
{
    public override Entity? Entity { get; set; }
    public override User? Analyst { get; set; }
    public override Assessment? Assessment { get; set; }
    
    public new int? Status { get; set; }
}