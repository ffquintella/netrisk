namespace Model.Statistics;

public class RiskEntity
{
    public int EntityId { get; set; }
    public string EntityName { get; set; } = "";
    public string EntityType { get; set; } = "";
    public float TotalCalculatedRisk { get; set; }
}