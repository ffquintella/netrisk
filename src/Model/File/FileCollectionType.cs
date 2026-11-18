namespace Model.File;

public enum FileCollectionType
{
    RiskFile,
    MitigationFile,
    IncidentResponsePlanFile,
    IncidentResponsePlanTaskFile,
    IncidentFile,

    /// <summary>
    /// Evidence on one answer of an assessment run (GitHub #80, S44). Stored as <c>view_type</c> 5, so
    /// members are only ever appended. Uploaded through the assessment evidence endpoints only.
    /// </summary>
    AssessmentRunAnswerFile,

    /// <summary>
    /// A risk index committee's minutes (S40 §6.4; T262): uploaded with no parent, then linked by the approver through
    /// <c>POST /RiskIndex/Profiles/{id}/Attachments</c>, the only route that may set the profile link.
    /// </summary>
    RiskIndexProfileEvidence,
}
