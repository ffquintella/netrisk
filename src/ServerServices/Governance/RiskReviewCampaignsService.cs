using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.6 — periodic business review campaigns.
///
/// The campaign is the artifact that answers "show me that the business reviewed its risks last
/// quarter, and what it decided" — the periodic-review evidence ISO 27001 and DORA auditors ask for.
/// It is generated on a cadence rather than created by hand, because a review that depends on
/// somebody remembering to start it is the review that does not happen.
///
/// Items are pre-populated from the entity's open risks ordered by residual score, plus anything
/// overdue per the cadence machinery and anything whose acceptance is about to lapse — so the
/// reviewer's list leads with what actually needs a decision.
/// </summary>
public class RiskReviewCampaignsService(
    ILogger logger,
    IDalService dalService,
    IRiskAcceptancesService acceptances,
    IMitigationTasksService tasks,
    INotificationEventPublisher notifications)
    : ServiceBase(logger, dalService), IRiskReviewCampaignsService
{
    public const string EnabledSetting = "risk_review_campaigns_enabled";
    public const string CadenceMonthsSetting = "risk_review_campaign_cadence_months";
    public const string DueDaysSetting = "risk_review_campaign_due_days";

    /// <summary>Quarterly, which is the spec's default and the common practice.</summary>
    public const int DefaultCadenceMonths = 3;

    public const int DefaultDueDays = 30;

    /// <summary>The seeded <c>review</c> value for "Accept the risk".</summary>
    private const int ReviewAcceptTheRisk = 2;

    /// <summary>The seeded <c>next_step</c> values: 1 request another review, 3 accept until next review.</summary>
    private const int NextStepRequestRiskReview = 1;

    private const int NextStepAcceptUntilNextReview = 3;

    public async Task<List<RiskReviewCampaign>> GenerateDueCampaignsAsync(DateTime asOfUtc)
    {
        await using var db = DalService.GetContext();

        if (!await IsEnabledAsync(db)) return [];

        var cadence = await ReadIntAsync(db, CadenceMonthsSetting, DefaultCadenceMonths);
        var dueDays = await ReadIntAsync(db, DueDaysSetting, DefaultDueDays);

        var (periodStart, periodEnd) = PeriodFor(asOfUtc, cadence);

        // Only entities that have somebody to review them. Generating a campaign nobody can act on
        // produces an overdue record that reflects a configuration gap rather than a review failure,
        // and the two must not look the same on a dashboard.
        var entityIds = await db.EntityRiskReviewers.Select(r => r.EntityId).Distinct().ToListAsync();

        var created = new List<RiskReviewCampaign>();

        foreach (var entityId in entityIds)
        {
            var existing = await db.RiskReviewCampaigns.FirstOrDefaultAsync(c =>
                c.EntityId == entityId && c.PeriodStart == periodStart && c.PeriodEnd == periodEnd);

            if (existing != null) continue;

            var campaign = new RiskReviewCampaign
            {
                EntityId = entityId,
                Name = $"Risk review {periodStart:yyyy'Q'}{Quarter(periodStart, cadence)}",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                DueDate = asOfUtc.Date.AddDays(dueDays),
                Status = RiskReviewCampaignStatus.Open,
                CreatedAt = asOfUtc
            };

            db.RiskReviewCampaigns.Add(campaign);
            await db.SaveChangesAsync();

            await PopulateAsync(db, campaign, asOfUtc);

            created.Add(campaign);
        }

        if (created.Count > 0)
            Logger.Information("Generated {Count} risk-review campaigns for the period {Start:yyyy-MM-dd} " +
                               "to {End:yyyy-MM-dd}", created.Count, periodStart, periodEnd);

        return created;
    }

    public async Task<List<RiskReviewCampaign>> GetForReviewerAsync(int userId, bool openOnly = true)
    {
        await using var db = DalService.GetContext();

        var entityIds = await db.EntityRiskReviewers.Where(r => r.UserId == userId)
            .Select(r => r.EntityId).Distinct().ToListAsync();

        if (entityIds.Count == 0) return [];

        var query = db.RiskReviewCampaigns.Where(c => entityIds.Contains(c.EntityId));

        if (openOnly)
            query = query.Where(c => c.Status == RiskReviewCampaignStatus.Open ||
                                     c.Status == RiskReviewCampaignStatus.Overdue);

        return await query
            .Include(c => c.Items)
            .OrderBy(c => c.DueDate)
            .ToListAsync();
    }

    public async Task<RiskReviewCampaign> GetAsync(int campaignId)
    {
        await using var db = DalService.GetContext();

        return await db.RiskReviewCampaigns
                   .Include(c => c.Items).ThenInclude(i => i.Risk)
                   .Include(c => c.Items).ThenInclude(i => i.DecidedBy)
                   .FirstOrDefaultAsync(c => c.Id == campaignId)
               ?? throw new DataNotFoundException("local", "risk_review_campaigns",
                   new Exception($"Risk review campaign with id {campaignId} not found"));
    }

    public async Task SaveRankingAsync(int campaignId, List<int> orderedItemIds, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(orderedItemIds);

        await using var db = DalService.GetContext();

        var items = await db.RiskReviewCampaignItems.Where(i => i.CampaignId == campaignId).ToListAsync();
        if (items.Count == 0)
            throw new DataNotFoundException("local", "risk_review_campaign_items",
                new Exception($"Campaign {campaignId} has no items"));

        var known = items.Select(i => i.Id).ToHashSet();
        var unknown = orderedItemIds.Where(id => !known.Contains(id)).ToList();

        if (unknown.Count > 0)
            throw new InvalidParameterException(nameof(orderedItemIds),
                $"These item ids are not in campaign {campaignId}: {string.Join(", ", unknown)}. A " +
                "ranking that silently drops the ids it does not recognise would reorder the list into " +
                "something the reviewer did not ask for.");

        var rank = 1;
        foreach (var id in orderedItemIds)
        {
            var item = items.First(i => i.Id == id);
            item.Rank = rank++;
        }

        // Mirrored onto the risk so the desktop list and the reports can sort on business priority
        // without joining through the campaign that produced it (8.6.5).
        var riskIds = items.Where(i => i.Rank != null).Select(i => i.RiskId).ToList();
        var risks = await db.Risks.Where(r => riskIds.Contains(r.Id)).ToListAsync();

        foreach (var risk in risks)
        {
            var item = items.First(i => i.RiskId == risk.Id);
            risk.BusinessRank = item.Rank;
        }

        await db.SaveChangesAsync();

        Logger.Information("Campaign {Campaign} ranked by user {User}: {Count} items", campaignId,
            actingUserId, orderedItemIds.Count);
    }

    public async Task<RiskReviewCampaignItem> DecideAsync(int campaignId, int itemId,
        CampaignDecisionRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Decision == RiskReviewDecision.Pending)
            throw new InvalidParameterException(nameof(request.Decision),
                "'Pending' is the absence of a decision, so it cannot be recorded as one.");

        int riskId;
        int? acceptanceId = null;

        await using (var db = DalService.GetContext())
        {
            var item = await db.RiskReviewCampaignItems
                           .FirstOrDefaultAsync(i => i.Id == itemId && i.CampaignId == campaignId)
                       ?? throw new DataNotFoundException("local", "risk_review_campaign_items",
                           new Exception($"Item {itemId} not found in campaign {campaignId}"));

            var campaign = await db.RiskReviewCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId)
                           ?? throw new DataNotFoundException("local", "risk_review_campaigns",
                               new Exception($"Risk review campaign with id {campaignId} not found"));

            if (campaign.Status is RiskReviewCampaignStatus.Completed or RiskReviewCampaignStatus.Cancelled)
                throw new InvalidStateTransitionException(campaign.Status.ToString(), "Decided",
                    "This campaign is closed. Reopening it would let a decision be added after the " +
                    "period it documents was signed off.");

            riskId = item.RiskId;
        }

        // The decision's side effects run through their own services, each with its own rules —
        // acceptance is appetite-gated and segregation-checked, tasks validate their owner. Doing it
        // here rather than inline is what makes the portal and the desktop app apply one rulebook.
        switch (request.Decision)
        {
            case RiskReviewDecision.Accepted:
            {
                if (request.Acceptance is null)
                    throw new InvalidParameterException(nameof(request.Acceptance),
                        "Accepting a risk needs a justification and an expiry date. Those are the fields " +
                        "the acceptance record exists to carry.");

                var acceptance = await acceptances.CreateAsync(riskId, request.Acceptance, actingUserId);
                acceptanceId = acceptance.Id;
                break;
            }

            case RiskReviewDecision.MitigationRequested:
            {
                if (request.Tasks is null || request.Tasks.Count == 0)
                    throw new InvalidParameterException(nameof(request.Tasks),
                        "Requesting mitigation needs at least one task with an owner and a due date. " +
                        "'Please mitigate this' is not a plan of action.");

                var mitigationId = await EnsureMitigationAsync(riskId, actingUserId);

                foreach (var task in request.Tasks)
                {
                    task.MitigationId = mitigationId;
                    await tasks.CreateAsync(task, actingUserId);
                }

                break;
            }

            case RiskReviewDecision.Escalated:
            {
                if (request.EscalateToUserId is null)
                    throw new InvalidParameterException(nameof(request.EscalateToUserId),
                        "An escalation needs a named senior approver. Escalating to nobody leaves the " +
                        "risk exactly where it was, with a note saying it moved.");
                break;
            }
        }

        await using var db2 = DalService.GetContext();

        var target = await db2.RiskReviewCampaignItems.FirstAsync(i => i.Id == itemId);

        if (request.Decision == RiskReviewDecision.Escalated &&
            !await db2.Users.AnyAsync(u => u.Value == request.EscalateToUserId!.Value))
            throw new DataNotFoundException("local", "user",
                new Exception($"User with id {request.EscalateToUserId} not found"));

        target.Decision = request.Decision;
        target.DecisionNotes = request.Notes;
        target.DecidedById = actingUserId;
        target.DecidedAt = DateTime.UtcNow;
        target.RiskAcceptanceId = acceptanceId;
        target.EscalatedToId = request.Decision == RiskReviewDecision.Escalated
            ? request.EscalateToUserId
            : null;

        // The acceptance branch already wrote its own review row, so writing a second one here would
        // duplicate the timeline entry the milestone is trying to unify.
        if (request.Decision != RiskReviewDecision.Accepted)
            db2.MgmtReviews.Add(new MgmtReview
            {
                RiskId = riskId,
                SubmissionDate = DateTime.UtcNow,
                Review = ReviewAcceptTheRisk,
                Reviewer = actingUserId,
                NextStep = NextStepRequestRiskReview,
                Comments = DescribeDecision(request),
                NextReview = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(DefaultCadenceMonths))
            });

        await db2.SaveChangesAsync();

        // An escalation that nobody is told about is a risk that stopped moving. Raised after the
        // save so the notification describes state that is committed.
        if (request.Decision == RiskReviewDecision.Escalated)
        {
            var risk = await db2.Risks.FirstOrDefaultAsync(r => r.Id == riskId);
            var score = await db2.RiskScorings.Where(sc => sc.Id == riskId)
                .Select(sc => (double?)(sc.ResidualRisk ?? sc.CalculatedRisk)).FirstOrDefaultAsync();

            if (risk != null)
                await notifications.RiskEscalatedAsync(risk, score, request.EscalateToUserId!.Value,
                    request.Notes);
        }

        await CloseCampaignIfCompleteAsync(campaignId);

        return target;
    }

    public async Task<List<RiskReviewCampaign>> MarkOverdueAsync(DateTime asOfUtc)
    {
        await using var db = DalService.GetContext();

        var overdue = await db.RiskReviewCampaigns
            .Where(c => c.Status == RiskReviewCampaignStatus.Open && c.DueDate < asOfUtc)
            .ToListAsync();

        foreach (var campaign in overdue) campaign.Status = RiskReviewCampaignStatus.Overdue;

        if (overdue.Count > 0) await db.SaveChangesAsync();

        return overdue;
    }

    public async Task<List<CampaignStatistics>> GetStatisticsAsync(int? entityId = null)
    {
        await using var db = DalService.GetContext();

        var campaigns = await db.RiskReviewCampaigns
            .Where(c => entityId == null || c.EntityId == entityId)
            .Include(c => c.Items)
            .OrderByDescending(c => c.PeriodStart)
            .ToListAsync();

        var names = await db.EntitiesProperties
            .Where(p => p.Type == "name")
            .Select(p => new { p.Entity, p.Value })
            .ToDictionaryAsync(p => p.Entity, p => p.Value);

        return campaigns.Select(c =>
        {
            var decided = c.Items.Where(i => i.Decision != RiskReviewDecision.Pending).ToList();

            return new CampaignStatistics
            {
                CampaignId = c.Id,
                EntityId = c.EntityId,
                EntityName = names.TryGetValue(c.EntityId, out var name) ? name : $"Entity {c.EntityId}",
                TotalItems = c.Items.Count,
                DecidedItems = decided.Count,
                Accepted = decided.Count(i => i.Decision == RiskReviewDecision.Accepted),
                MitigationRequested = decided.Count(i => i.Decision == RiskReviewDecision.MitigationRequested),
                Escalated = decided.Count(i => i.Decision == RiskReviewDecision.Escalated),
                Status = c.Status,
                DueDate = c.DueDate,
                AverageDaysToDecide = decided.Count == 0
                    ? null
                    : decided.Where(i => i.DecidedAt != null)
                        .Select(i => (i.DecidedAt!.Value - c.CreatedAt).TotalDays)
                        .DefaultIfEmpty()
                        .Average()
            };
        }).ToList();
    }

    // --- internals ------------------------------------------------------------------------------

    /// <summary>
    /// Fills a new campaign with the entity's open risks, ordered by residual score descending, with
    /// the ones that most need a decision first: an overdue review or an expiring acceptance beats a
    /// merely high score, because the first two have a deadline attached.
    /// </summary>
    private async Task PopulateAsync(DAL.Context.AuditableContext db, RiskReviewCampaign campaign,
        DateTime asOfUtc)
    {
        var risks = await db.Risks
            .Where(r => r.EntityId == campaign.EntityId && r.Status != "Closed")
            .Select(r => new { r.Id })
            .ToListAsync();

        if (risks.Count == 0) return;

        var riskIds = risks.Select(r => r.Id).ToList();

        var scores = await db.RiskScorings
            .Where(s => riskIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ResidualRisk, s.CalculatedRisk })
            .ToDictionaryAsync(s => s.Id, s => (double)(s.ResidualRisk ?? s.CalculatedRisk));

        var flagged = await db.Risks
            .Where(r => riskIds.Contains(r.Id) && r.ReviewRequested)
            .Select(r => r.Id)
            .ToListAsync();

        var expiringSoon = await db.RiskAcceptances
            .Where(a => a.RiskId != null && riskIds.Contains(a.RiskId.Value) &&
                        a.Status == RiskAcceptanceStatus.Active &&
                        a.ExpiresAt <= asOfUtc.AddDays(60))
            .Select(a => a.RiskId!.Value)
            .ToListAsync();

        var priority = flagged.Concat(expiringSoon).ToHashSet();

        var ordered = riskIds
            .OrderByDescending(id => priority.Contains(id))
            .ThenByDescending(id => scores.TryGetValue(id, out var score) ? score : 0)
            .ThenBy(id => id)
            .ToList();

        foreach (var riskId in ordered)
            db.RiskReviewCampaignItems.Add(new RiskReviewCampaignItem
            {
                CampaignId = campaign.Id,
                RiskId = riskId,
                Decision = RiskReviewDecision.Pending,
                CreatedAt = asOfUtc
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The mitigation the requested tasks hang off, created if the risk has none. A treatment plan
    /// with tasks but no plan row would leave the tasks unreachable from the risk editor.
    /// </summary>
    private async Task<int> EnsureMitigationAsync(int riskId, int actingUserId)
    {
        await using var db = DalService.GetContext();

        var existing = await db.Mitigations
            .Where(m => m.RiskId == riskId)
            .OrderByDescending(m => m.LastUpdate)
            .FirstOrDefaultAsync();

        if (existing != null) return existing.Id;

        // The lookup columns are non-nullable FKs, so a mitigation created from the portal takes the
        // first seeded value of each rather than a zero that would violate the constraint.
        var effort = await db.MitigationEfforts.Select(e => e.Value).FirstOrDefaultAsync();
        var cost = await db.MitigationCosts.Select(c => c.Value).FirstOrDefaultAsync();
        var strategy = await db.PlanningStrategies.Select(p => p.Value).FirstOrDefaultAsync();

        var mitigation = new Mitigation
        {
            RiskId = riskId,
            SubmissionDate = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow,
            PlanningStrategy = strategy,
            MitigationEffort = effort,
            MitigationCost = cost,
            MitigationOwner = actingUserId,
            SubmittedBy = actingUserId,
            CurrentSolution = string.Empty,
            SecurityRequirements = "Created from a business risk review: the reviewer asked for mitigation.",
            SecurityRecommendations = string.Empty,
            PlanningDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            MitigationPercent = 0
        };

        db.Mitigations.Add(mitigation);
        await db.SaveChangesAsync();

        return mitigation.Id;
    }

    /// <summary>
    /// Closes a campaign once every item has a decision. The completion is what makes the campaign
    /// evidence rather than a to-do list.
    /// </summary>
    private async Task CloseCampaignIfCompleteAsync(int campaignId)
    {
        await using var db = DalService.GetContext();

        var campaign = await db.RiskReviewCampaigns
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == campaignId);

        if (campaign is null) return;
        if (campaign.Items.Count == 0) return;
        if (campaign.Items.Any(i => i.Decision == RiskReviewDecision.Pending)) return;

        campaign.Status = RiskReviewCampaignStatus.Completed;
        campaign.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        Logger.Information("Risk-review campaign {Id} for entity {Entity} completed with {Count} decisions",
            campaign.Id, campaign.EntityId, campaign.Items.Count);
    }

    private static string DescribeDecision(CampaignDecisionRequest request) => request.Decision switch
    {
        RiskReviewDecision.MitigationRequested =>
            "Business risk review: mitigation requested. " + (request.Notes ?? string.Empty),
        RiskReviewDecision.Escalated =>
            $"Business risk review: escalated to user {request.EscalateToUserId}. " +
            (request.Notes ?? string.Empty),
        _ => "Business risk review. " + (request.Notes ?? string.Empty)
    };

    private static (DateTime Start, DateTime End) PeriodFor(DateTime asOf, int cadenceMonths)
    {
        if (cadenceMonths < 1) cadenceMonths = 1;

        // Periods are aligned to the calendar year rather than to the install date, so an
        // organization's Q1 is January to March whatever day NetRisk was deployed — which is what
        // makes the campaign name mean the same thing to a reviewer and to an auditor.
        var index = (asOf.Month - 1) / cadenceMonths;
        var startMonth = index * cadenceMonths + 1;

        var start = new DateTime(asOf.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(cadenceMonths).AddDays(-1);

        return (start, end);
    }

    private static int Quarter(DateTime periodStart, int cadenceMonths) =>
        (periodStart.Month - 1) / System.Math.Max(1, cadenceMonths) + 1;

    private static async Task<bool> IsEnabledAsync(DAL.Context.AuditableContext db)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == EnabledSetting);
        if (setting?.Value is null) return true;

        return setting.Value.Trim().ToLowerInvariant() is "true" or "1" or "yes";
    }

    private static async Task<int> ReadIntAsync(DAL.Context.AuditableContext db, string key, int fallback)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == key);
        return setting?.Value is not null && int.TryParse(setting.Value, out var value) && value > 0
            ? value
            : fallback;
    }
}
