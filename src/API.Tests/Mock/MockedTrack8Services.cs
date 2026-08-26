using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using Model.Governance;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// Deterministic doubles for the Track 8 governance services.
///
/// The convention these follow is the one the Track 3 and Track 4 mocks established: fixtures for the
/// happy path, and the domain exception a controller maps onto each status code for every guard
/// branch. A controller test is a test of the HTTP contract, and here that contract is mostly which
/// exception becomes which code — <see cref="RuleBrokenException"/> is 422 (segregation of duties,
/// the appetite ceiling), <see cref="PermissionInvalidException"/> is 403 (insufficient band),
/// <see cref="DataAlreadyExistsException"/> is 409 (already accepted).
/// </summary>
public static class MockedRiskAcceptancesService
{
    /// <summary>A risk with no live acceptance, which accepts cleanly.</summary>
    public const int AcceptableRiskId = 1;

    /// <summary>A risk the caller submitted — segregation of duties refuses it.</summary>
    public const int OwnRiskId = 2;

    /// <summary>A risk whose residual is above the appetite ceiling.</summary>
    public const int AboveCeilingRiskId = 3;

    /// <summary>A risk that already carries a live acceptance.</summary>
    public const int AlreadyAcceptedRiskId = 4;

    /// <summary>A risk the caller lacks the band authority to accept.</summary>
    public const int OutOfBandRiskId = 5;

    public const int KnownAcceptanceId = 100;

    public static IRiskAcceptancesService Create()
    {
        var service = Substitute.For<IRiskAcceptancesService>();

        service.GetByRiskAsync(Arg.Any<int>()).Returns(call =>
        {
            var riskId = call.ArgAt<int>(0);
            if (riskId == 999)
                throw new DataNotFoundException("risks", riskId.ToString(),
                    new Exception($"Risk {riskId} was not found."));

            return Task.FromResult(new List<RiskAcceptance> { Acceptance(KnownAcceptanceId, riskId) });
        });

        service.GetActiveAsync(Arg.Any<int>()).Returns(call =>
        {
            var riskId = call.ArgAt<int>(0);
            return Task.FromResult(riskId == AlreadyAcceptedRiskId
                ? Acceptance(KnownAcceptanceId, riskId)
                : null);
        });

        service.GetExpiringAsync(Arg.Any<int>()).Returns(call =>
        {
            var days = call.ArgAt<int>(0);
            if (days < 0)
                throw new InvalidParameterException("days", "A negative window is not a window.");

            return Task.FromResult(new List<RiskAcceptance> { Acceptance(KnownAcceptanceId, AcceptableRiskId) });
        });

        service.CreateAsync(Arg.Any<int>(), Arg.Any<RiskAcceptanceRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                var riskId = call.ArgAt<int>(0);
                var request = call.ArgAt<RiskAcceptanceRequest>(1);

                if (string.IsNullOrWhiteSpace(request.BusinessJustification))
                    throw new InvalidParameterException(nameof(request.BusinessJustification),
                        "An acceptance needs a written business justification.");

                if (request.ExpiresAt is null)
                    throw new InvalidParameterException(nameof(request.ExpiresAt),
                        "An acceptance needs an expiry date.");

                return riskId switch
                {
                    OwnRiskId => throw new RuleBrokenException(
                        "You cannot accept this risk because you submitted it.", "segregation_of_duties"),
                    AboveCeilingRiskId => throw new RuleBrokenException(
                        "Residual 9.10 is above the acceptance ceiling of 6.00.", "risk_appetite_ceiling"),
                    AlreadyAcceptedRiskId => throw new DataAlreadyExistsException("local",
                        "risk_acceptances", riskId.ToString(), "This risk already has a live acceptance."),
                    OutOfBandRiskId => throw new PermissionInvalidException("review_veryhigh", 1,
                        "accept risk"),
                    999 => throw new DataNotFoundException("risks", riskId.ToString(),
                        new Exception("Risk not found.")),
                    _ => Task.FromResult(Acceptance(KnownAcceptanceId, riskId))
                };
            });

        service.RenewAsync(Arg.Any<int>(), Arg.Any<RiskAcceptanceRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                var id = call.ArgAt<int>(0);
                var request = call.ArgAt<RiskAcceptanceRequest>(1);

                if (string.IsNullOrWhiteSpace(request.BusinessJustification))
                    throw new InvalidParameterException(nameof(request.BusinessJustification),
                        "A renewal needs a fresh justification.");

                if (id == 998)
                    throw new InvalidStateTransitionException("Revoked", "Renewed",
                        "A revoked acceptance is not renewed, it is replaced.");

                if (id == 999)
                    throw new DataNotFoundException("risk_acceptances", id.ToString(),
                        new Exception("Acceptance not found."));

                return Task.FromResult(Acceptance(id + 1, AcceptableRiskId));
            });

        service.RevokeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            var reason = call.ArgAt<string>(1);

            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidParameterException("reason", "A revocation needs a reason.");

            if (id == 999)
                throw new DataNotFoundException("risk_acceptances", id.ToString(),
                    new Exception("Acceptance not found."));

            var revoked = Acceptance(id, AcceptableRiskId);
            revoked.Status = RiskAcceptanceStatus.Revoked;
            revoked.RevocationReason = reason;
            return Task.FromResult(revoked);
        });

        service.ProcessExpiryAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new RiskAcceptanceExpiryResult()));

        return service;
    }

    private static RiskAcceptance Acceptance(int id, int riskId) => new()
    {
        Id = id,
        RiskId = riskId,
        Name = $"Acceptance {id}",
        BusinessJustification = "The vendor patch breaks the payment integration.",
        AuthorizingManagerId = 1,
        StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        Status = RiskAcceptanceStatus.Active,
        ResidualScoreSnapshot = 4.5,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

public static class MockedRiskWorkflowService
{
    /// <summary>The risk the appetite fixture puts above the ceiling.</summary>
    public const int AboveCeilingRiskId = 3;

    public static IRiskWorkflowService Create()
    {
        var service = Substitute.For<IRiskWorkflowService>();

        service.EvaluateAppetiteAsync(Arg.Any<int>()).Returns(call =>
        {
            var riskId = call.ArgAt<int>(0);

            if (riskId == 999)
                throw new DataNotFoundException("risks", riskId.ToString(),
                    new Exception("Risk not found."));

            return Task.FromResult(new AppetiteEvaluation
            {
                AppetiteConfigured = true,
                AppetiteId = 1,
                MaxAcceptableResidual = 6,
                DualApprovalThreshold = 4,
                ResidualScore = riskId == AboveCeilingRiskId ? 9.1 : 3.2,
                ExceedsCeiling = riskId == AboveCeilingRiskId,
                RequiresDualApproval = false,
                Explanation = "Residual 3.2 is within appetite."
            });
        });

        service.CountRisksAboveAppetiteAsync().Returns(Task.FromResult(new List<AppetiteBreachCount>
        {
            new() { EntityId = 1, EntityName = "Head office", Count = 2 }
        }));

        service.FindLegacyViolationsAsync().Returns(Task.FromResult(new List<WorkflowViolation>
        {
            new()
            {
                RiskId = 7, Subject = "Legacy closed risk", Status = "Closed",
                Reason = "A risk cannot be closed without a management review or a live risk acceptance."
            }
        }));

        return service;
    }
}

public static class MockedRiskAppetitesService
{
    public const int KnownAppetiteId = 1;

    public static IRiskAppetitesService Create()
    {
        var service = Substitute.For<IRiskAppetitesService>();

        service.GetAllAsync().Returns(Task.FromResult(new List<RiskAppetite> { Appetite(KnownAppetiteId, null) }));
        service.GetGlobalAsync().Returns(Task.FromResult<RiskAppetite?>(Appetite(KnownAppetiteId, null)));

        service.SaveAsync(Arg.Any<RiskAppetite>(), Arg.Any<int>()).Returns(call =>
        {
            var appetite = call.ArgAt<RiskAppetite>(0);

            if (appetite.DualApprovalThreshold > appetite.MaxAcceptableResidual)
                throw new InvalidParameterException(nameof(appetite.DualApprovalThreshold),
                    "The dual-approval threshold has to be at or below the acceptance ceiling.");

            if (appetite.EntityId == 999)
                throw new DataNotFoundException("entities", "999", new Exception("Entity not found."));

            if (appetite.Id == 0 && appetite.EntityId is null && appetite.Notes == "duplicate-global")
                throw new DataAlreadyExistsException("local", "risk_appetites", "global",
                    "An organization-wide appetite already exists.");

            appetite.Id = appetite.Id == 0 ? KnownAppetiteId : appetite.Id;
            return Task.FromResult(appetite);
        });

        service.DeleteAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id == 999)
                throw new DataNotFoundException("risk_appetites", "999",
                    new Exception("Appetite not found."));
            return Task.CompletedTask;
        });

        return service;
    }

    private static RiskAppetite Appetite(int id, int? entityId) => new()
    {
        Id = id,
        EntityId = entityId,
        MaxAcceptableResidual = 6,
        DualApprovalThreshold = 4,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

public static class MockedMitigationTasksService
{
    public const int KnownTaskId = 50;

    public const int KnownMitigationId = 5;

    public static IMitigationTasksService Create()
    {
        var service = Substitute.For<IMitigationTasksService>();

        service.GetByMitigationAsync(Arg.Any<int>())
            .Returns(Task.FromResult(new List<MitigationTask> { Task_(KnownTaskId) }));
        service.GetByRiskAsync(Arg.Any<int>())
            .Returns(Task.FromResult(new List<MitigationTask> { Task_(KnownTaskId) }));
        service.GetDueOrOverdueAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MitigationTask> { Task_(KnownTaskId) }));

        service.GetAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id == 999)
                throw new DataNotFoundException("mitigation_tasks", "999",
                    new Exception("Task not found."));
            return Task.FromResult(Task_(id));
        });

        service.CreateAsync(Arg.Any<MitigationTaskRequest>(), Arg.Any<int>()).Returns(call =>
        {
            var request = call.ArgAt<MitigationTaskRequest>(0);

            if (string.IsNullOrWhiteSpace(request.Title))
                throw new InvalidParameterException(nameof(request.Title), "A task needs a title.");

            if (request.MitigationId == 999)
                throw new DataNotFoundException("mitigations", "999",
                    new Exception("Mitigation not found."));

            return Task.FromResult(Task_(KnownTaskId));
        });

        service.UpdateAsync(Arg.Any<MitigationTaskRequest>(), Arg.Any<int>()).Returns(call =>
        {
            var request = call.ArgAt<MitigationTaskRequest>(0);
            if (request.Id == 999)
                throw new DataNotFoundException("mitigation_tasks", "999",
                    new Exception("Task not found."));
            return Task.FromResult(Task_(request.Id));
        });

        service.DeleteAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id == 999)
                throw new DataNotFoundException("mitigation_tasks", "999",
                    new Exception("Task not found."));
            return Task.CompletedTask;
        });

        service.MarkNotifiedAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(Task.CompletedTask);

        return service;
    }

    private static MitigationTask Task_(int id) => new()
    {
        Id = id,
        MitigationId = KnownMitigationId,
        Title = "Rotate the shared service account",
        OwnerId = 2,
        DueDate = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
        Status = MitigationTaskStatus.Open,
        CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

public static class MockedAuditTrailService
{
    public static IAuditTrailService Create()
    {
        var service = Substitute.For<IAuditTrailService>();

        var rows = new List<AuditLog>
        {
            new()
            {
                Id = 1, EntityType = nameof(Risk), EntityId = 1, Field = "Status",
                OldValue = "New", NewValue = "Closed", Action = AuditLogAction.Update,
                UserId = 1, Actor = "admin",
                OccurredAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
                CorrelationId = "abc"
            }
        };

        service.GetForRecordAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(rows));
        service.GetForRiskAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(Task.FromResult(rows));
        service.GetForEntityPeriodAsync(Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<int>()).Returns(Task.FromResult(rows));
        service.ApplyRetentionAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(0));

        // The full pack (8.4.2/8.6.5): one of each section, so a renderer that drops a section fails
        // rather than producing a plausible-looking short file.
        service.GetEvidencePackAsync(Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<int>())
            .Returns(call => Task.FromResult(new GovernanceEvidencePack
            {
                EntityId = call.ArgAt<int?>(0),
                EntityName = call.ArgAt<int?>(0) == null ? "(all entities)" : "Retail Bank",
                FromUtc = call.ArgAt<DateTime>(1),
                ToUtc = call.ArgAt<DateTime>(2),
                GeneratedAtUtc = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
                RequestedBy = call.ArgAt<string>(3),
                Acceptances =
                [
                    new EvidenceAcceptance
                    {
                        Id = 1, RiskId = 1, RiskSubject = "Risk 1", Name = "Standing exception",
                        Status = "Active", AuthorizingManager = "Ana Approver (ana, #10)",
                        StartDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        ExpiresAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                        BusinessJustification = "=SUM(A1:A2), and a\nnewline",
                        ResidualScoreSnapshot = 4.25, FromCampaign = true
                    }
                ],
                Reviews =
                [
                    new EvidenceReview
                    {
                        Id = 1, RiskId = 1, RiskSubject = "Risk 1",
                        SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                        Reviewer = "Bob Reviewer (bob, #11)", Comments = "Treat",
                        RequiresCountersignature = true,
                        SecondReviewer = "Cleo (cleo, #12)",
                        SecondReviewAt = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                        SegregationOverrideReason = "Sole approver on site"
                    }
                ],
                CampaignDecisions =
                [
                    new EvidenceCampaignDecision
                    {
                        CampaignId = 1, CampaignName = "Q2 2026", CampaignStatus = "Completed",
                        PeriodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        PeriodEnd = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
                        DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                        RiskId = 1, RiskSubject = "Risk 1", Rank = 1, Decision = "Accepted",
                        DecidedBy = "Bob Reviewer (bob, #11)",
                        DecidedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                        RiskAcceptanceId = 1
                    }
                ],
                Changes =
                [
                    new EvidenceChange
                    {
                        OccurredAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                        EntityType = nameof(Risk), EntityId = 1, Field = "Status",
                        Action = "Update", Actor = "admin", UserId = 1,
                        OldValue = "New", NewValue = "Closed", CorrelationId = "abc"
                    }
                ]
            }));

        return service;
    }
}

public static class MockedEntityRiskReviewersService
{
    public const int AppointedEntityId = 1;

    public static IEntityRiskReviewersService Create()
    {
        var service = Substitute.For<IEntityRiskReviewersService>();

        service.GetByEntityAsync(Arg.Any<int>()).Returns(call => Task.FromResult(new List<EntityRiskReviewer>
        {
            new()
            {
                Id = 1, EntityId = call.ArgAt<int>(0), UserId = 1, IsPrimary = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        }));

        service.GetEntitiesForReviewerAsync(Arg.Any<int>())
            .Returns(Task.FromResult(new List<int> { AppointedEntityId }));

        service.AppointAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>())
            .Returns(call =>
            {
                var entityId = call.ArgAt<int>(0);
                var userId = call.ArgAt<int>(1);

                if (entityId == 999)
                    throw new DataNotFoundException("entities", "999", new Exception("Entity not found."));
                if (userId == 998)
                    throw new InvalidParameterException("userId",
                        "A disabled account cannot be appointed a risk reviewer.");

                return Task.FromResult(new EntityRiskReviewer
                {
                    Id = 1, EntityId = entityId, UserId = userId, IsPrimary = call.ArgAt<bool>(2),
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            });

        service.RemoveAsync(Arg.Any<int>()).Returns(call =>
        {
            if (call.ArgAt<int>(0) == 999)
                throw new DataNotFoundException("entity_risk_reviewers", "999",
                    new Exception("Appointment not found."));
            return Task.CompletedTask;
        });

        return service;
    }
}

public static class MockedRiskReviewCampaignsService
{
    public const int KnownCampaignId = 1;

    /// <summary>A campaign for an entity the fixture reviewer is *not* appointed to.</summary>
    public const int ForeignCampaignId = 2;

    public const int KnownItemId = 10;

    public static IRiskReviewCampaignsService Create()
    {
        var service = Substitute.For<IRiskReviewCampaignsService>();

        service.GenerateDueCampaignsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign> { Campaign(KnownCampaignId, 1) }));

        service.GetForReviewerAsync(Arg.Any<int>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign> { Campaign(KnownCampaignId, 1) }));

        service.GetAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id == 999)
                throw new DataNotFoundException("risk_review_campaigns", "999",
                    new Exception("Campaign not found."));

            // Entity 2 is one the fixture reviewer is not appointed to, so the controller's
            // appointment check has something to refuse.
            return Task.FromResult(Campaign(id, id == ForeignCampaignId ? 2 : 1));
        });

        service.SaveRankingAsync(Arg.Any<int>(), Arg.Any<List<int>>(), Arg.Any<int>()).Returns(call =>
        {
            var ordered = call.ArgAt<List<int>>(1);
            if (ordered.Contains(999))
                throw new InvalidParameterException("orderedItemIds",
                    "These item ids are not in the campaign: 999.");
            return Task.CompletedTask;
        });

        service.DecideAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CampaignDecisionRequest>(),
            Arg.Any<int>()).Returns(call =>
        {
            var request = call.ArgAt<CampaignDecisionRequest>(2);

            switch (request.Decision)
            {
                case RiskReviewDecision.Pending:
                    throw new InvalidParameterException(nameof(request.Decision),
                        "'Pending' is the absence of a decision.");
                case RiskReviewDecision.Accepted when request.Acceptance is null:
                    throw new InvalidParameterException(nameof(request.Acceptance),
                        "Accepting a risk needs a justification and an expiry date.");
                case RiskReviewDecision.MitigationRequested when request.Tasks is null || request.Tasks.Count == 0:
                    throw new InvalidParameterException(nameof(request.Tasks),
                        "Requesting mitigation needs at least one task.");
                case RiskReviewDecision.Escalated when request.EscalateToUserId is null:
                    throw new InvalidParameterException(nameof(request.EscalateToUserId),
                        "An escalation needs a named senior approver.");
            }

            if (request.Notes == "over-ceiling")
                throw new RuleBrokenException("Residual 9.10 is above the acceptance ceiling of 6.00.",
                    "risk_appetite_ceiling");

            if (request.Notes == "own-risk")
                throw new RuleBrokenException("You cannot accept this risk because you own it.",
                    "segregation_of_duties");

            return Task.FromResult(new RiskReviewCampaignItem
            {
                Id = call.ArgAt<int>(1),
                CampaignId = call.ArgAt<int>(0),
                RiskId = 1,
                Decision = request.Decision,
                DecisionNotes = request.Notes,
                DecidedById = call.ArgAt<int>(3),
                DecidedAt = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        service.MarkOverdueAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));

        service.GetStatisticsAsync(Arg.Any<int?>()).Returns(Task.FromResult(new List<CampaignStatistics>
        {
            new()
            {
                CampaignId = KnownCampaignId, EntityId = 1, EntityName = "Head office",
                TotalItems = 4, DecidedItems = 3, Accepted = 2, MitigationRequested = 1,
                Status = RiskReviewCampaignStatus.Open,
                DueDate = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)
            }
        }));

        return service;
    }

    private static RiskReviewCampaign Campaign(int id, int entityId) => new()
    {
        Id = id,
        EntityId = entityId,
        Name = $"Risk review 2026Q3 #{id}",
        PeriodStart = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
        DueDate = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        Status = RiskReviewCampaignStatus.Open,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        Items = new List<RiskReviewCampaignItem>
        {
            new() { Id = KnownItemId, CampaignId = id, RiskId = 1, Decision = RiskReviewDecision.Pending }
        }
    };
}

public static class MockedQuantitativeRiskService
{
    /// <summary>A risk that has never been scored quantitatively — the 204 case.</summary>
    public const int UnscoredRiskId = 2;

    public static IQuantitativeRiskService Create()
    {
        var service = Substitute.For<IQuantitativeRiskService>();

        service.GetAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            return Task.FromResult(id == UnscoredRiskId ? null : Result(id));
        });

        service.ComputeAndSaveAsync(Arg.Any<int>(), Arg.Any<QuantitativeRiskInput>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            var input = call.ArgAt<QuantitativeRiskInput>(1);

            if (input.LossEventFrequencyMax < input.LossEventFrequencyMin)
                throw new InvalidParameterException(nameof(input.LossEventFrequencyMostLikely),
                    "The loss-event frequency range has to run minimum ≤ most likely ≤ maximum.");

            if (id == 999)
                throw new DataNotFoundException("risks", "999", new Exception("Risk not found."));

            return Task.FromResult(Result(id))!;
        });

        service.RecomputeAllAsync().Returns(Task.FromResult(0));

        return service;
    }

    private static QuantitativeRiskResult Result(int riskId) => new()
    {
        RiskId = riskId,
        InherentP10 = 1000,
        InherentP50 = 45000,
        InherentP90 = 320000,
        InherentMean = 92000,
        MappedRiskLevel = "Medium",
        MappedScore = 5.2f,
        Seed = 20260826,
        Iterations = 10000,
        LossExceedanceCurve =
        [
            new LossExceedancePointDto { Loss = 1000, Probability = 0.9 },
            new LossExceedancePointDto { Loss = 320000, Probability = 0.1 }
        ]
    };
}

public static class MockedTokenRevocationService
{
    /// <summary>The jti the fixture reports as already revoked.</summary>
    public const string RevokedJti = "revoked-token-id";

    public static ITokenRevocationService Create()
    {
        var service = Substitute.For<ITokenRevocationService>();

        service.RevokeAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<string>())
            .Returns(call =>
            {
                var jti = call.ArgAt<string>(0);
                if (string.IsNullOrWhiteSpace(jti))
                    throw new InvalidParameterException("jti",
                        "A token with no jti claim cannot be revoked individually.");
                return Task.CompletedTask;
            });

        service.IsRevokedAsync(Arg.Any<string>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(0) == RevokedJti));

        service.PruneExpiredAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(0));

        return service;
    }
}

public static class MockedFileAccessAuthorizer
{
    /// <summary>Files whose id or user is this are refused, so the 401 branch has a fixture.</summary>
    public const int ForbiddenFileId = 4242;

    public static IFileAccessAuthorizer Create()
    {
        var service = Substitute.For<IFileAccessAuthorizer>();

        service.EnsureCanReadAsync(Arg.Any<NrFile>(), Arg.Any<User>()).Returns(call =>
        {
            var file = call.ArgAt<NrFile>(0);
            var user = call.ArgAt<User>(1);

            if (file.Id == ForbiddenFileId)
                throw new UserNotAuthorizedException(user.Name, user.Value, "files");

            return Task.CompletedTask;
        });

        return service;
    }
}
