using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Track 8 governance core: risk-level acceptance (8.1), residual and quantitative scoring
/// (8.2/8.7), risk appetite and counter-signature (8.3), the field-level audit trail (8.4), and
/// mitigation tasks plus pending-risk triage (8.5).
///
/// The business review portal (8.6) lives in <c>NRDbContext.ReviewPortal.cs</c> and the tables the
/// deferred Track 7 findings needed live in <c>NRDbContext.Security.cs</c>, each with its own
/// migration and its own numbered upgrade phase, so an installation can stop between them and still
/// have a schema that matches an EF migration exactly.
///
/// Configured in this partial rather than the generated <c>OnModelCreating</c> so that file stays
/// regenerable, and named per the Track 6 convention throughout: snake_case columns via
/// <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c> constraint prefixes, int-backed enums with
/// explicit conversions, <c>tinyint(1)</c> booleans, UTC <c>datetime</c> temporal columns, and
/// <c>varchar(n)</c>/<c>text</c> rather than a blob for anything textual.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RiskAppetite> RiskAppetites { get; set; } = null!;

    public virtual DbSet<MitigationTask> MitigationTasks { get; set; } = null!;

    public virtual DbSet<AuditLog> AuditLogs { get; set; } = null!;

    /// <summary>
    /// Attaches the field-level governance audit interceptor (Track 8 milestone 8.4.1) to every
    /// context, however it was constructed.
    ///
    /// Registering it here rather than at each <c>AddDbContext</c>/<c>DbContextOptionsBuilder</c>
    /// call site is deliberate: there are five of those (API, background jobs, console, the test
    /// harnesses) and an audit trail that depends on every one of them remembering is the trail that
    /// has a hole in it. The interceptor is a stateless singleton, so this adds no per-context cost.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(Auditing.GovernanceAuditInterceptor.Instance);
    }

    /// <summary>
    /// Entity-scoping predicates for the governance core (Track 2 milestone 2.3.1 applied to Track 8
    /// schema). An instance method rather than a static one because the derived filters read the
    /// already-filtered parent <c>DbSet</c>, so "my parent is visible to me" is expressed once.
    ///
    /// Without these a scoped caller could read another tenant's acceptance justifications and
    /// treatment tasks: the parent risk would be invisible while its children were not.
    /// </summary>
    private void ApplyGovernanceQueryFilters(ModelBuilder modelBuilder)
    {
        // The appetite is either the organization-wide default (entity_id null, and every risk is
        // measured against it) or an entity override.
        modelBuilder.Entity<RiskAppetite>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));

        modelBuilder.Entity<MitigationTask>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Mitigations.Any(m => m.Id == e.MitigationId));
    }

    private static void ConfigureGovernance(ModelBuilder modelBuilder)
    {
        ConfigureAcceptanceRiskLink(modelBuilder);
        ConfigureResidualAndQuantitativeScoring(modelBuilder);
        ConfigureScaleAnchors(modelBuilder);
        ConfigureRiskAppetite(modelBuilder);
        ConfigureCountersignature(modelBuilder);
        ConfigureRiskGovernanceColumns(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureMitigationTasks(modelBuilder);
        ConfigurePendingRiskTriage(modelBuilder);
    }

    /// <summary>
    /// 8.1.1 — the columns that let one <c>risk_acceptances</c> row serve a risk rather than a set of
    /// findings.
    /// </summary>
    private static void ConfigureAcceptanceRiskLink(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskAcceptance>(entity =>
        {
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.RequestedById).HasColumnName("requested_by_id").HasColumnType("int(11)");
            entity.Property(e => e.StartDate).HasColumnName("start_date").HasColumnType("datetime");
            entity.Property(e => e.RenewedFromId).HasColumnName("renewed_from_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.RiskId, "idx_ra_risk_id");
            entity.HasIndex(e => e.RenewedFromId, "idx_ra_renewed_from_id");

            // Cascade: an acceptance has no meaning without the risk it accepts. Deleting a risk is
            // already an administrator-only act, and leaving orphaned "accepted" rows behind would
            // make the expiry job reopen something that no longer exists.
            entity.HasOne(e => e.Risk)
                .WithMany(r => r.Acceptances)
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_ra_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RequestedBy)
                .WithMany()
                .HasForeignKey(e => e.RequestedById)
                .HasConstraintName("fk_ra_requested_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            // Restrict on the renewal chain: dropping a predecessor would silently break the
            // "renewed from" trail, which is the only record that the exception has been running
            // longer than its latest expiry date suggests.
            entity.HasOne(e => e.RenewedFrom)
                .WithMany()
                .HasForeignKey(e => e.RenewedFromId)
                .HasConstraintName("fk_ra_renewed_from_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>8.2.1 and 8.7.2 — residual score, its history, and the FAIR-lite cache.</summary>
    private static void ConfigureResidualAndQuantitativeScoring(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskScoring>(entity =>
        {
            entity.Property(e => e.ResidualRisk).HasColumnName("residual_risk");
            entity.Property(e => e.ResidualUpdatedAt).HasColumnName("residual_updated_at")
                .HasColumnType("datetime");

            entity.Property(e => e.QuantLefMin).HasColumnName("quant_lef_min");
            entity.Property(e => e.QuantLefMostLikely).HasColumnName("quant_lef_most_likely");
            entity.Property(e => e.QuantLefMax).HasColumnName("quant_lef_max");
            entity.Property(e => e.QuantLossMin).HasColumnName("quant_loss_min");
            entity.Property(e => e.QuantLossMostLikely).HasColumnName("quant_loss_most_likely");
            entity.Property(e => e.QuantLossMax).HasColumnName("quant_loss_max");
            entity.Property(e => e.QuantAleP10).HasColumnName("quant_ale_p10");
            entity.Property(e => e.QuantAleP50).HasColumnName("quant_ale_p50");
            entity.Property(e => e.QuantAleP90).HasColumnName("quant_ale_p90");
            entity.Property(e => e.QuantAleMean).HasColumnName("quant_ale_mean");
            entity.Property(e => e.QuantResidualAleP10).HasColumnName("quant_residual_ale_p10");
            entity.Property(e => e.QuantResidualAleP50).HasColumnName("quant_residual_ale_p50");
            entity.Property(e => e.QuantResidualAleP90).HasColumnName("quant_residual_ale_p90");
            entity.Property(e => e.QuantLossExceedanceCurve).HasColumnName("quant_loss_exceedance_curve")
                .HasColumnType("text");
            entity.Property(e => e.QuantSeed).HasColumnName("quant_seed").HasColumnType("int(11)");
            entity.Property(e => e.QuantComputedAt).HasColumnName("quant_computed_at")
                .HasColumnType("datetime");

            // The appetite queries ("risks above appetite", "order the campaign by residual") all
            // read residual, so it needs the same index treatment the inherent score already has.
            entity.HasIndex(e => e.ResidualRisk, "idx_risk_scoring_residual_risk");
        });

        modelBuilder.Entity<RiskScoringHistory>(entity =>
        {
            entity.Property(e => e.ResidualRisk).HasColumnName("residual_risk");
        });
    }

    /// <summary>8.7.1 — written definitions and numeric bounds on the ordinal scales.</summary>
    private static void ConfigureScaleAnchors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Likelihood>(entity =>
        {
            entity.Property(e => e.Definition).HasColumnName("definition").HasColumnType("text");
            entity.Property(e => e.ProbabilityMin).HasColumnName("probability_min");
            entity.Property(e => e.ProbabilityMax).HasColumnName("probability_max");
        });

        modelBuilder.Entity<Impact>(entity =>
        {
            entity.Property(e => e.Definition).HasColumnName("definition").HasColumnType("text");
            entity.Property(e => e.ImpactMin).HasColumnName("impact_min");
            entity.Property(e => e.ImpactMax).HasColumnName("impact_max");
        });
    }

    /// <summary>8.3.3 — the appetite thresholds that turn scores into behaviour.</summary>
    private static void ConfigureRiskAppetite(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskAppetite>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_appetites")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.MaxAcceptableResidual).HasColumnName("max_acceptable_residual");
            entity.Property(e => e.DualApprovalThreshold).HasColumnName("dual_approval_threshold");
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            // Unique per entity so an entity cannot end up with two contradictory appetites. The
            // global row (entity_id NULL) is *not* protected by this index — MySQL treats every NULL
            // as distinct — so the service enforces the single-global rule instead.
            entity.HasIndex(e => e.EntityId, "uq_risk_appetites_entity_id").IsUnique();

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_risk_appetites_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_risk_appetites_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>8.3.4 — second-approver columns on the existing review row.</summary>
    private static void ConfigureCountersignature(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MgmtReview>(entity =>
        {
            entity.Property(e => e.RequiresCountersignature)
                .HasColumnName("requires_countersignature")
                .HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.SecondReviewerId).HasColumnName("second_reviewer_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.SecondReviewAt).HasColumnName("second_review_at")
                .HasColumnType("datetime");
            entity.Property(e => e.SegregationOverrideReason)
                .HasColumnName("segregation_override_reason").HasColumnType("text");

            entity.HasIndex(e => e.SecondReviewerId, "idx_mgmt_reviews_second_reviewer_id");

            entity.HasOne(e => e.SecondReviewer)
                .WithMany()
                .HasForeignKey(e => e.SecondReviewerId)
                .HasConstraintName("fk_mgmt_reviews_second_reviewer_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>8.5.1 / 8.6.5 — the columns the risk row itself gains.</summary>
    private static void ConfigureRiskGovernanceColumns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Risk>(entity =>
        {
            entity.Property(e => e.BusinessRank).HasColumnName("business_rank").HasColumnType("int(11)");
            entity.Property(e => e.ReviewRequested).HasColumnName("review_requested")
                .HasColumnType("tinyint(1)").HasDefaultValue(false);
            entity.Property(e => e.ReviewRequestedAt).HasColumnName("review_requested_at")
                .HasColumnType("datetime");
            entity.Property(e => e.ReviewRequestedReason).HasColumnName("review_requested_reason")
                .HasColumnType("text");

            entity.HasIndex(e => e.BusinessRank, "idx_risks_business_rank");
            entity.HasIndex(e => e.ReviewRequested, "idx_risks_review_requested");
        });
    }

    /// <summary>8.4.1 — the field-level trail.</summary>
    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("audit_logs")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(128);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Field).HasColumnName("field").HasMaxLength(128);
            entity.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("text");
            entity.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("text");
            entity.Property(e => e.Action).HasColumnName("action").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.Actor).HasColumnName("actor").HasMaxLength(64);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);

            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "idx_audit_logs_entity_type_entity_id");
            entity.HasIndex(e => e.OccurredAt, "idx_audit_logs_occurred_at");
            entity.HasIndex(e => e.CorrelationId, "idx_audit_logs_correlation_id");

            // SetNull rather than Restrict: deleting a user must not be blocked by the trail, and it
            // must not delete the trail either — the row's value is that the change happened.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_audit_logs_user_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>8.5.3 — POA&amp;M-style treatment tasks.</summary>
    private static void ConfigureMitigationTasks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MitigationTask>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("mitigation_tasks")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.MitigationId).HasColumnName("mitigation_id").HasColumnType("int(11)");
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(255);
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id").HasColumnType("int(11)");
            entity.Property(e => e.DueDate).HasColumnName("due_date").HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)")
                .HasDefaultValue(MitigationTaskStatus.Open)
                .HasSentinel(default)
                .HasConversion<int>();
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.LastNotifiedDaysBefore).HasColumnName("last_notified_days_before")
                .HasColumnType("int(11)");

            entity.HasIndex(e => e.MitigationId, "idx_mitigation_tasks_mitigation_id");
            entity.HasIndex(e => new { e.Status, e.DueDate }, "idx_mitigation_tasks_status_due_date");
            entity.HasIndex(e => e.OwnerId, "idx_mitigation_tasks_owner_id");

            entity.HasOne(e => e.Mitigation)
                .WithMany(m => m.Tasks)
                .HasForeignKey(e => e.MitigationId)
                .HasConstraintName("fk_mitigation_tasks_mitigation_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .HasConstraintName("fk_mitigation_tasks_owner_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_mitigation_tasks_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>8.5.2 — triage columns on the dead intake table.</summary>
    private static void ConfigurePendingRiskTriage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PendingRisk>(entity =>
        {
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)")
                .HasDefaultValue(PendingRiskStatus.Pending)
                .HasSentinel(default)
                .HasConversion<int>();
            entity.Property(e => e.PromotedRiskId).HasColumnName("promoted_risk_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.TriagedById).HasColumnName("triaged_by_id").HasColumnType("int(11)");
            entity.Property(e => e.TriagedAt).HasColumnName("triaged_at").HasColumnType("datetime");
            entity.Property(e => e.DismissalReason).HasColumnName("dismissal_reason")
                .HasColumnType("text");

            entity.HasIndex(e => e.Status, "idx_pending_risks_status");
        });
    }

}
