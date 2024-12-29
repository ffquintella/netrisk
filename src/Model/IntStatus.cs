namespace Model;

public enum IntStatus
{
    New = 1,
    Open = 2,
    UnderReview = 3,
    Closed = 4,
    SentForPatch = 5,
    NotRelevant = 6,
    MitigationPlanned = 7,
    Mitigated = 8,
    ManagementReview = 9,
    Reopened = 10,
    Rejected = 11,
    Duplicated = 12,
    Prioritized = 13,
    Critical = 14,
    AwaitingUserResponse = 15,
    AwaitingVendorResponse = 16,
    AwaitingInternalResponse = 17,
    AwaitingFix = 18,
    FixInProgress = 19,
    FixAvailable = 20,
    FixNotAvailable = 21,
    FixNotRequired = 22,
    FixNotApplicable = 23,
    FixNotPossible = 24,
    Fixed = 25,
    Solved = 26,
    Retired = 27,
    AwaitingApproval = 28,
    Outdated = 29,
    AwaitingFixVerification = 30,
    FixVerified = 31,
    FixWithProblems = 32,
    FixNotVerified = 33,
    FixNotTested = 34,
    Regular = 35,
    Ok = 36,
    Deleted = 37,
    NeedsMoreInfo = 38,
    NeedsFurtherInvestigation = 39,
    NeedsFix = 40,
    Verified = 41,
    Active = 42,
    Processing = 43,
    Error = 44,
    Running = 45,
    Stopped = 46,
    Completed = 47,
    Failed = 48,
    Cancelled = 49,
    Read = 50,
    Approved = 51,
    UnderInvestigation = 52,
}