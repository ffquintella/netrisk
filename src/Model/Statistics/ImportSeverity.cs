namespace Model.Statistics;

public class ImportSeverity
{
    public DateTime ImportDate { get; set; }
    
    public int ImportSequecialNumber { get; set; } 
    public double CriticalityLevel { get; set; } 
    public int ItemCount { get; set; } 
    public double TotalRiskValue { get; set; }
    
}