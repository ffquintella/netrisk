namespace Model.DTO;

public class FixRequestDto
{
    public int VulnerabilityId { get; set; }
    public  string Comments { get; set; } = "";
    public  string Destination { get; set; } = "";
    public int? FixTeamId { get; set; }
    public string Identifier { get; set; } = "";
}