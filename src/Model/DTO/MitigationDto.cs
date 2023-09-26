namespace Model.DTO;

public class MitigationDto
{
    public int Id { get; set; }
    public int RiskId { get; set; }
    public DateTime SubmissionDate { get; set; }
    public DateTime LastUpdate { get; set; }
    public int PlanningStrategy { get; set; }
    public int MitigationEffort { get; set; }
    public int MitigationCost { get; set; }
    public int MitigationOwner { get; set; }
    public string? CurrentSolution { get; set; }
    public string? SecurityRequirements { get; set; }
    public string? SecurityRecommendations { get; set; }
    public int SubmittedBy { get; set; }
    public DateOnly PlanningDate { get; set; }
    public int MitigationPercent { get; set; }

}

/*
{
    "id": 0,
    "riskId": 310,
    "submissionDate": "2022-06-24T13:41:57",
    "lastUpdate": "0001-01-01T00:00:00",
    "planningStrategy": 3,
    "mitigationEffort": 4,
    "mitigationCost": 3,
    "mitigationOwner": 3,
    "currentSolution": "Desenvolver integrações entre sistemas que torne desnecessário a troca de dados manual",
    "securityRequirements": "Vazamentos de das planilhas de dados",
    "securityRecommendations": "Enquanto não for possível desenvolver o controle criptografar os dados ",
    "submittedBy": 3,
    "planningDate": "2022-07-31",
    "mitigationPercent": 10
}
*/