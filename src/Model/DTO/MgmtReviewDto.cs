namespace Model.DTO;

public class MgmtReviewDto
{
    public int Id { get; set; }
    public int RiskId { get; set; }
    public DateTime SubmissionDate { get; set; }
    public int Review { get; set; }
    public int Reviewer { get; set; }
    public int NextStep { get; set; }
    public string Comments { get; set; } = null!;
    
    public DateOnly NextReview { get; set; }
    
}

/*
    {
        "id": 1,
        "riskId": 1,
        "submissionDate": "2022-06-24T13:42:50",
        "review": 1,
        "reviewer": 2,
        "nextStep": 1,
        "comments": "Aguardando correções da FGV Conhecimentos",
        "nextReview": "2022-08-01",
        "nextStepNavigation": null,
        "reviewNavigation": null,
        "reviewerNavigation": null,
        "risk": null
    },
*/