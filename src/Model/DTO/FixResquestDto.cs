namespace Model.DTO;

public class FixResquestDto
{
    int VulnerabilityId { get; set; }
    string Comments { get; set; }
    string Destination { get; set; }
    int? FixTeamId { get; set; }
}