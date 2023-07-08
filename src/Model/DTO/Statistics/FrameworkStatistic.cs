namespace Model.DTO.Statistics;

public class FrameworkStatistic
{
    public string Framework { get; set; } = "";
    public int Count { get; set; }
    public int TotalMaturity { get; set; }
    public int TotalDesiredMaturity { get; set; }
}