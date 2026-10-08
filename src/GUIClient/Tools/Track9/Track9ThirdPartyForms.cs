using System.Collections.Generic;
using System.Linq;
using Model.ThirdParties;

namespace GUIClient.Tools.Track9;

/// <summary>Typed request builders shared by the Stage 9.10 register screens.</summary>
public static class Track9ThirdPartyForms
{
    public static ThirdPartyRequest BuildRequest(ThirdPartyDto source) => new()
    {
        Name = source.Name,
        LegalName = source.LegalName,
        TaxId = source.TaxId,
        Country = source.Country,
        Description = source.Description,
        Website = source.Website,
        EntityId = source.EntityId,
        OwnerId = source.OwnerId,
        Status = source.Status,
        IsCloudProvider = source.IsCloudProvider,
        IsIdentityProvider = source.IsIdentityProvider,
        ProcessesPersonalData = source.ProcessesPersonalData,
        ContractReference = source.ContractReference,
        ContractStart = source.ContractStart,
        ContractEnd = source.ContractEnd,
        SlaAvailabilityPercent = source.SlaAvailabilityPercent,
        ContractedRtoMinutes = source.ContractedRtoMinutes,
        ContractedRpoMinutes = source.ContractedRpoMinutes,
        VulnerabilityFixDays = source.VulnerabilityFixDays,
        RightToAudit = source.RightToAudit,
        AuditClauseReference = source.AuditClauseReference,
        ExitPlan = source.ExitPlan,
        ExitPlanReviewedAt = source.ExitPlanReviewedAt,
        ExitPlanTestedAt = source.ExitPlanTestedAt,
        DataPortability = source.DataPortability
    };

    public static ThirdPartyAssessmentAnswersRequest BuildAnswers(IEnumerable<HecvatAnswerDto> answers) => new()
    {
        Answers = answers.Select(answer => new HecvatAnswerRequest
        {
            QuestionId = answer.QuestionId,
            Answer = answer.Answer,
            PreferredAnswer = answer.PreferredAnswer,
            Weight = answer.Weight,
            Critical = answer.Critical,
            Notes = answer.Notes
        }).ToList()
    };
}
