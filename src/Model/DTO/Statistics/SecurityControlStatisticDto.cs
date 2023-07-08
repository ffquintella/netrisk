namespace Model.DTO.Statistics;

public class SecurityControlStatistic
{
    public double TotalRisk { get; set; }
    public string Framework { get; set; } = "";
    public int FrameworkId { get; set; }
    public int ControlId { get; set; }
    public string ReferemceName { get; set; } = "";
    public string ControlName { get; set; } = "";
    public int? ClassId { get; set; }
    public int MaturityId { get; set; }
    public int DesireedMaturityId { get; set; }
    public int? PiorityId { get; set; }
    public int Status { get; set; }
    public sbyte Deleted { get; set; }
    public string ControlNumber { get; set; } = "";
}