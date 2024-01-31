using DAL.Entities;

namespace Model.DTO;

public class AssessmentRunDto: AssessmentRun
{
#pragma warning disable CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    public override Entity? Entity
    {
        get => null;
        set
        {
            if(value != null) EntityId = value.Id;
        }
    }

    public override User? Analyst
    {
        get => null;
        set
        {
            if(value != null) AnalystId = value.Value;
        }
    }
    
    public override Host? Host
    {
        get => null;
        set
        {
            if(value != null) HostId = value.Id;
        }
    }

    public override Assessment? Assessment
    {
        get => null;
        set
        {
            if(value != null) AssessmentId = value.Id; 
        }
    }
#pragma warning restore CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    
    public new int? Status { get; set; }
}