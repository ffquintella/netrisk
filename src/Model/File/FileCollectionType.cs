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
}
