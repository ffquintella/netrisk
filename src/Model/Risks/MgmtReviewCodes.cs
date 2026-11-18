namespace Model.Risks;

/// <summary>
/// The seeded <c>review</c> lookup (<c>src/ConsoleClient/DB/Data/1.sql</c>), the decision column of a management review
/// (<c>MgmtReview.Review</c>). One source of truth for every service that writes or reads a review's outcome (S39 D-06,
/// T277): the Track 8 services each declared their own constant, and all three contradicted the seed.
/// <c>ConsoleClient.Tests/DB/MgmtReviewCodesSeedTest</c> fails if a value here stops naming the seeded row.
/// </summary>
public static class ReviewCodes
{
    /// <summary>"Approve Risk": the reviewer accepts the risk as assessed.</summary>
    public const int ApproveRisk = 1;

    /// <summary>"Reject Risk and Close".</summary>
    public const int RejectRiskAndClose = 2;

    /// <summary>"Request Risk review": the review did not settle the risk; somebody has to look again.</summary>
    public const int RequestRiskReview = 3;
}

/// <summary>
/// The seeded <c>next_step</c> lookup (<c>src/ConsoleClient/DB/Data/1.sql</c>), the follow-up column of a management review
/// (<c>MgmtReview.NextStep</c>). Like <see cref="ReviewCodes"/>, pinned to the seed by
/// <c>ConsoleClient.Tests/DB/MgmtReviewCodesSeedTest</c>. The seed has no "request another review" next step: that outcome
/// is <see cref="ReviewCodes.RequestRiskReview"/>, in the other column.
/// </summary>
public static class NextStepCodes
{
    /// <summary>"Accept until Next Review".</summary>
    public const int AcceptUntilNextReview = 1;

    /// <summary>"Consider for Project": treatment work is needed (the desktop offers to plan a mitigation, IX-6).</summary>
    public const int ConsiderForProject = 2;

    /// <summary>"Submit as a Production Issue".</summary>
    public const int SubmitAsProductionIssue = 3;

    /// <summary>"Reject" (the desktop offers to close the risk, IX-6).</summary>
    public const int Reject = 4;
}
