using Mapster;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.DTO;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class MgmtReviewsService: ServiceBase, IMgmtReviewsService
{
    /// <summary>
    /// Whether the review cadence keys off the inherent score or the post-treatment one
    /// (Track 8 milestone 8.2.2). The setting existed in version 1, was deleted in version 29, and
    /// never did anything while it existed; version 80 re-creates it and this is where it now bites.
    /// </summary>
    public const string CadenceBasisSetting = "next_review_date_uses";

    /// <summary>The value that selects the residual score. Anything else means inherent.</summary>
    public const string CadenceBasisResidual = "ResidualRisk";

    /// <summary>The permission a counter-signature requires. A second approver who is not more
    /// senior than the first is not an escalation, it is a second opinion.</summary>
    private const string TopBandPermission = "review_veryhigh";

    private readonly IRiskWorkflowService? _workflow;
    private readonly IPermissionsService? _permissions;

    public MgmtReviewsService(
        ILogger logger,
        IDalService dalService
    ): base(logger, dalService)
    {
    }

    /// <summary>
    /// The constructor the DI container uses. The workflow engine and the permission service are
    /// optional so the legacy two-argument shape keeps working for the callers (and tests) that only
    /// read reviews; the Track 8 methods require them and say so.
    /// </summary>
    public MgmtReviewsService(
        ILogger logger,
        IDalService dalService,
        IRiskWorkflowService workflow,
        IPermissionsService permissions
    ): base(logger, dalService)
    {
        _workflow = workflow;
        _permissions = permissions;
    }

    private void RiskExists(int riskId)
    {
        using var dbContext = DalService.GetContext();
        // Check if risk exists 
        var risk = dbContext.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));
    }

    public List<MgmtReview> GetRiskReviews(int riskId)
    {
        using var dbContext = DalService.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews.Where(mr => mr.RiskId == riskId).ToList(); 

        return reviews;
    }

    public List<Review> GetReviewTypes()
    {
        using var dbContext = DalService.GetContext();

        var reviews = dbContext.Reviews.ToList();

        return reviews; 
    }

    public List<NextStep> GetNextSteps()
    {
        using var dbContext = DalService.GetContext();

        var nextSteps = dbContext.NextSteps.ToList();

        return nextSteps; 
    }

    public ReviewLevel GetRiskReviewLevel(int riskId)
    {
        using var dbContext = DalService.GetContext();

        var risk = dbContext.Risks.FirstOrDefault(r => r.Id == riskId);
        if(risk == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));
        
        var scoring = dbContext.RiskScorings.FirstOrDefault(rs => rs.Id == riskId);
        
        if (scoring == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk scoring with id {riskId} not found"));

        var riskLevels = dbContext.RiskLevels.ToList();

        RiskLevel? foundRiskLevel = null;
        foreach (var riskLevel in riskLevels.OrderBy(rl => rl.Value))
        {
            if(scoring.CalculatedRisk  > Convert.ToSingle(riskLevel.Value )) foundRiskLevel = riskLevel;
            else break;
        }

        if (foundRiskLevel == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk level with id {riskId} not found"));
        
        var reviewLevel = dbContext.ReviewLevels.FirstOrDefault(rl => rl.Name == foundRiskLevel.DisplayName);
        
        if(reviewLevel == null)
            throw new DataNotFoundException("local", "risks", new Exception($"Review level with id {riskId} not found"));

        return reviewLevel; 
    }

    public MgmtReview? GetRiskLastReview(int riskId)
    {
        using var dbContext = DalService.GetContext();
        
        RiskExists(riskId);

        var reviews = dbContext.MgmtReviews
            .Where(mr => mr.RiskId == riskId)
            .Include(mr => mr.ReviewNavigation)
            .Include(mr => mr.NextStepNavigation)
            .OrderBy(mr => mr.SubmissionDate).Reverse()
            .FirstOrDefault(); 

        return reviews;
    }

    public MgmtReview Create(MgmtReview review)
    {
        using var dbContext = DalService.GetContext();

        var dbReview = dbContext.MgmtReviews.Add(review);
        dbContext.SaveChanges();

        var dbObj = dbContext.MgmtReviews
            .Include(rev => rev.ReviewNavigation)
            .Include(rev => rev.NextStepNavigation)
            .FirstOrDefault(mr => mr.Id == dbReview.Entity.Id);
        
        return dbObj!;
    }
    
    public MgmtReview Update(MgmtReviewDto review)
    {
        using var dbContext = DalService.GetContext();

        var dbObj = dbContext.MgmtReviews.FirstOrDefault(mr => mr.Id == review.Id);
        
        if(dbObj == null)
            throw new DataNotFoundException("local", "mgmtReviews", new Exception($"MgmtReview with id {review.Id} not found"));

        
        dbObj = review.Adapt<MgmtReview>();
        
        //var dbReview = dbContext.MgmtReviews.Update(dbObj);
        dbContext.SaveChanges();
        
        return dbObj;
    }

    public MgmtReview GetOne(int mgmtReviewId)
    {
        using var dbContext = DalService.GetContext();

        var dbObj = dbContext.MgmtReviews.FirstOrDefault(mr => mr.Id == mgmtReviewId);
        
        if(dbObj == null)
            throw new DataNotFoundException("local", "mgmtReviews", new Exception($"MgmtReview with id {mgmtReviewId} not found"));
        
        return dbObj;
    }

    // --- Track 8 milestone 8.3: enforcement ----------------------------------------------------

    public async Task<MgmtReview> CreateReviewAsync(MgmtReview review, int actingUserId,
        string? segregationOverrideReason = null)
    {
        ArgumentNullException.ThrowIfNull(review);

        var workflow = RequireWorkflow();

        // Maker-checker first. Any user holding the matching severity band could previously approve
        // single-handedly, including the risk's own submitter, owner or manager — and administrators
        // bypassed every check. Nobody bypasses this one.
        await workflow.EnsureSegregationOfDutiesAsync(review.RiskId, actingUserId, "review",
            segregationOverrideReason);

        var appetite = await workflow.EvaluateAppetiteAsync(review.RiskId);

        await using var dbContext = DalService.GetContext();

        var risk = await dbContext.Risks.FirstOrDefaultAsync(r => r.Id == review.RiskId);
        if (risk == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Risk with id {review.RiskId} not found"));

        review.Id = 0;
        review.Reviewer = actingUserId;
        if (review.SubmissionDate == default) review.SubmissionDate = DateTime.UtcNow;
        review.Comments ??= string.Empty;

        // Above the threshold the review lands unsigned by the second approver, which is what holds
        // the risk in review until somebody counter-signs. Refusing the review instead would leave
        // the reviewer with nowhere to record what they concluded.
        review.RequiresCountersignature = appetite.RequiresDualApproval;
        review.SecondReviewerId = null;
        review.SecondReviewAt = null;
        review.SegregationOverrideReason =
            string.IsNullOrWhiteSpace(segregationOverrideReason) ? null : segregationOverrideReason;

        dbContext.MgmtReviews.Add(review);

        // A completed review answers whatever event asked for one, so the flag clears here rather
        // than being left for somebody to notice.
        risk.ReviewRequested = false;
        risk.ReviewRequestedReason = null;
        risk.LastUpdate = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        Logger.Information(
            "Management review {Id} recorded on risk {RiskId} by user {User}{Countersign}", review.Id,
            review.RiskId, actingUserId,
            review.RequiresCountersignature ? " (awaiting counter-signature)" : string.Empty);

        return (await dbContext.MgmtReviews
            .Include(rev => rev.ReviewNavigation)
            .Include(rev => rev.NextStepNavigation)
            .FirstOrDefaultAsync(mr => mr.Id == review.Id))!;
    }

    public async Task<MgmtReview> CountersignAsync(int reviewId, int actingUserId,
        string? segregationOverrideReason = null)
    {
        var workflow = RequireWorkflow();

        await using var dbContext = DalService.GetContext();

        var review = await dbContext.MgmtReviews.FirstOrDefaultAsync(mr => mr.Id == reviewId);
        if (review == null)
            throw new DataNotFoundException("local", "mgmtReviews",
                new Exception($"MgmtReview with id {reviewId} not found"));

        if (!review.RequiresCountersignature)
            throw new InvalidStateTransitionException("SingleApproval", "Countersigned",
                "This review did not cross the dual-approval threshold, so there is nothing to " +
                "counter-sign. Signing it anyway would put a second approver's name against a decision " +
                "that did not need one.");

        if (review.SecondReviewerId != null)
            throw new InvalidStateTransitionException("Countersigned", "Countersigned",
                "This review has already been counter-signed.");

        if (review.Reviewer == actingUserId)
            throw new RuleBrokenException(
                "The second approver has to be someone other than the first. One person signing twice " +
                "is a single approval with two dates on it.",
                "dual_approval_distinct_approvers");

        await workflow.EnsureSegregationOfDutiesAsync(review.RiskId, actingUserId, "counter-sign",
            segregationOverrideReason);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Value == actingUserId);
        if (user == null)
            throw new DataNotFoundException("local", "user",
                new Exception($"User with id {actingUserId} not found"));

        if (!user.Admin)
        {
            var permissions = _permissions == null
                ? new List<string>()
                : await _permissions.GetUserPermissionsAsync(user);

            if (!permissions.Any(p => string.Equals(p, TopBandPermission, StringComparison.OrdinalIgnoreCase)))
                throw new PermissionInvalidException(TopBandPermission, actingUserId, "counter-sign review");
        }

        review.SecondReviewerId = actingUserId;
        review.SecondReviewAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        Logger.Information("Management review {Id} counter-signed by user {User}", reviewId, actingUserId);

        return review;
    }

    public async Task<ReviewLevel> GetRiskReviewLevelAsync(int riskId)
    {
        await using var dbContext = DalService.GetContext();

        var risk = await dbContext.Risks.FirstOrDefaultAsync(r => r.Id == riskId);
        if (risk == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Risk with id {riskId} not found"));

        var scoring = await dbContext.RiskScorings.FirstOrDefaultAsync(rs => rs.Id == riskId);
        if (scoring == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Risk scoring with id {riskId} not found"));

        var setting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Name == CadenceBasisSetting);
        var useResidual = string.Equals(setting?.Value?.Trim(), CadenceBasisResidual,
            StringComparison.OrdinalIgnoreCase);

        // Residual only when it has actually been computed. Falling back to inherent otherwise is
        // the safe direction: a null residual would resolve to the lowest band and hand an untreated
        // risk the longest review interval in the table.
        var score = useResidual && scoring.ResidualRisk != null
            ? scoring.ResidualRisk.Value
            : scoring.CalculatedRisk;

        var riskLevels = await dbContext.RiskLevels.OrderBy(rl => rl.Value).ToListAsync();

        RiskLevel? foundRiskLevel = null;
        foreach (var riskLevel in riskLevels)
        {
            if (score > Convert.ToSingle(riskLevel.Value)) foundRiskLevel = riskLevel;
            else break;
        }

        if (foundRiskLevel == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Risk level for risk {riskId} not found"));

        var reviewLevel = await dbContext.ReviewLevels
            .FirstOrDefaultAsync(rl => rl.Name == foundRiskLevel.DisplayName);

        if (reviewLevel == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Review level for risk {riskId} not found"));

        return reviewLevel;
    }

    /// <summary>
    /// The workflow engine, or a clear error. The two-argument constructor exists for read-only
    /// callers; reaching an enforcement method through it is a wiring mistake, and a NullReference
    /// deep in the call stack is a poor way to report one.
    /// </summary>
    private IRiskWorkflowService RequireWorkflow() =>
        _workflow ?? throw new InvalidOperationException(
            "This MgmtReviewsService was constructed without the risk-workflow engine, so the Track 8 " +
            "approval rules cannot be applied. Resolve it from the container instead of constructing it.");
}
