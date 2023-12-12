using DAL.Entities;

namespace Model.DTO;

public class AssessmentRunDto: AssessmentRun
{
#pragma warning disable CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    public override Entity? Entity { get; set; }

    public override User? Analyst { get; set; }
    public override Assessment? Assessment { get; set; }
#pragma warning restore CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    
    public new int? Status { get; set; }
}