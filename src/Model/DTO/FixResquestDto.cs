namespace Model.DTO;

public class FixResquestDto
{
    int VulnerabilityId { get; set; }
    private string Comments { get; set; } = "";
    private string Destination { get; set; } = "";
    int? FixTeamId { get; set; }
}