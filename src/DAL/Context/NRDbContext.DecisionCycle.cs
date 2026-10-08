using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.9 — archival with reopening conditions, incident backtesting and the risk committee (S50 §4):
/// <c>risk_archives</c> (the Phase 4 "archive" decision), <c>risk_archive_conditions</c> (the Phase 7 triggers that
/// reopen it), <c>risk_archive_reviews</c> (its quarterly review), <c>incident_backtests</c> and
/// <c>incident_backtest_risks</c> (an incident confronted with the register), and <c>risk_committees</c>,
/// <c>risk_committee_members</c>, <c>risk_committee_decisions</c> and <c>risk_committee_votes</c> (the collegiate
/// approver). The third line adds no table: it is a seeded role and permission (<c>Data/98.sql</c>).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enums, <c>varchar(n)</c> and
/// never <c>char(n)</c>, <c>text</c> for the free-text justifications an acceptance already stores as <c>text</c>. Every
/// CHECK is declared on the model too, so the snapshot carries the same DDL as <c>Structure/98.sql</c>. No CHECK names
/// a column a <c>SET NULL</c> foreign key may clear. No inverse navigation on <see cref="Risk"/>, <see cref="Incident"/>
/// or <see cref="User"/>, so the payloads of <c>GET /Risks</c> and <c>GET /Incidents</c> do not change. The query
/// filters are in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RiskArchive> RiskArchives { get; set; } = null!;

    public virtual DbSet<RiskArchiveCondition> RiskArchiveConditions { get; set; } = null!;

    public virtual DbSet<RiskArchiveReview> RiskArchiveReviews { get; set; } = null!;

    public virtual DbSet<IncidentBacktest> IncidentBacktests { get; set; } = null!;

    public virtual DbSet<IncidentBacktestRisk> IncidentBacktestRisks { get; set; } = null!;

    public virtual DbSet<RiskCommittee> RiskCommittees { get; set; } = null!;

    public virtual DbSet<RiskCommitteeMember> RiskCommitteeMembers { get; set; } = null!;

    public virtual DbSet<RiskCommitteeDecision> RiskCommitteeDecisions { get; set; } = null!;

    public virtual DbSet<RiskCommitteeVote> RiskCommitteeVotes { get; set; } = null!;

    private static void ConfigureDecisionCycle(ModelBuilder modelBuilder)
    {
        ConfigureArchive(modelBuilder);
        ConfigureBacktesting(modelBuilder);
        ConfigureCommittees(modelBuilder);
    }

    private static void ConfigureArchive(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskArchive>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_archives", t =>
                {
                    t.HasCheckConstraint("ck_risk_archives_status", "`status` >= 1 AND `status` <= 2");
                    t.HasCheckConstraint("ck_risk_archives_reopen_origin",
                        "`reopen_origin` IS NULL OR (`reopen_origin` >= 1 AND `reopen_origin` <= 3)");
                    t.HasCheckConstraint("ck_risk_archives_reopened",
                        "(`status` = 1 AND `reopened_at` IS NULL AND `reopen_origin` IS NULL) OR (`status` = 2 AND `reopened_at` IS NOT NULL AND `reopen_origin` IS NOT NULL AND `reopen_reason` IS NOT NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.ClosureId).HasColumnName("closure_id").HasColumnType("int(11)");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Justification).HasColumnName("justification").HasColumnType("varchar(4000)")
                .HasMaxLength(4000);
            entity.Property(e => e.PreviousStatus).HasColumnName("previous_status").HasColumnType("varchar(50)")
                .HasMaxLength(50);
            entity.Property(e => e.ArchivedAt).HasColumnName("archived_at").HasColumnType("datetime");
            entity.Property(e => e.ArchivedById).HasColumnName("archived_by_id").HasColumnType("int(11)");
            entity.Property(e => e.NextReviewDueAt).HasColumnName("next_review_due_at").HasColumnType("datetime");
            entity.Property(e => e.LastReviewedAt).HasColumnName("last_reviewed_at").HasColumnType("datetime");
            entity.Property(e => e.ReviewNotifiedAt).HasColumnName("review_notified_at").HasColumnType("datetime");
            entity.Property(e => e.ReopenedAt).HasColumnName("reopened_at").HasColumnType("datetime");
            entity.Property(e => e.ReopenOrigin).HasColumnName("reopen_origin").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.ReopenedById).HasColumnName("reopened_by_id").HasColumnType("int(11)");
            entity.Property(e => e.ReopenReason).HasColumnName("reopen_reason").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.ReopenEventId).HasColumnName("reopen_event_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.RiskId, "idx_risk_archives_risk_id");
            entity.HasIndex(e => new { e.Status, e.NextReviewDueAt }, "idx_risk_archives_status_next_review_due_at");
            entity.HasIndex(e => e.ClosureId, "idx_risk_archives_closure_id");
            entity.HasIndex(e => e.ArchivedById, "idx_risk_archives_archived_by_id");
            entity.HasIndex(e => e.ReopenedById, "idx_risk_archives_reopened_by_id");
            entity.HasIndex(e => e.ReopenEventId, "idx_risk_archives_reopen_event_id");

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_archives_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            // SET NULL: reopening through the legacy route deletes the closure, and the archive must survive it — no
            // longer live, but still the record of a decision.
            entity.HasOne(e => e.Closure)
                .WithMany()
                .HasForeignKey(e => e.ClosureId)
                .HasConstraintName("fk_risk_archives_closure_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ArchivedBy)
                .WithMany()
                .HasForeignKey(e => e.ArchivedById)
                .HasConstraintName("fk_risk_archives_archived_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ReopenedBy)
                .WithMany()
                .HasForeignKey(e => e.ReopenedById)
                .HasConstraintName("fk_risk_archives_reopened_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ReopenEvent)
                .WithMany()
                .HasForeignKey(e => e.ReopenEventId)
                .HasConstraintName("fk_risk_archives_reopen_event_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskArchiveCondition>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_archive_conditions", t => t.HasCheckConstraint(
                    "ck_risk_archive_conditions_trigger_type", "`trigger_type` >= 1 AND `trigger_type` <= 6"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ArchiveId).HasColumnName("archive_id").HasColumnType("int(11)");
            entity.Property(e => e.TriggerType).HasColumnName("trigger_type").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.ArchiveId, e.TriggerType }, "uq_risk_archive_conditions_archive_id_trigger_type")
                .IsUnique();

            entity.HasOne(e => e.Archive)
                .WithMany(a => a.Conditions)
                .HasForeignKey(e => e.ArchiveId)
                .HasConstraintName("fk_risk_archive_conditions_archive_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RiskArchiveReview>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_archive_reviews", t =>
                {
                    t.HasCheckConstraint("ck_risk_archive_reviews_outcome", "`outcome` >= 1 AND `outcome` <= 2");
                    t.HasCheckConstraint("ck_risk_archive_reviews_next_due",
                        "(`outcome` = 1 AND `next_review_due_at` IS NOT NULL) OR (`outcome` = 2 AND `next_review_due_at` IS NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ArchiveId).HasColumnName("archive_id").HasColumnType("int(11)");
            entity.Property(e => e.Outcome).HasColumnName("outcome").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Note).HasColumnName("note").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("datetime");
            entity.Property(e => e.ReviewedById).HasColumnName("reviewed_by_id").HasColumnType("int(11)");
            entity.Property(e => e.NextReviewDueAt).HasColumnName("next_review_due_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => e.ArchiveId, "idx_risk_archive_reviews_archive_id");
            entity.HasIndex(e => e.ReviewedById, "idx_risk_archive_reviews_reviewed_by_id");

            entity.HasOne(e => e.Archive)
                .WithMany(a => a.Reviews)
                .HasForeignKey(e => e.ArchiveId)
                .HasConstraintName("fk_risk_archive_reviews_archive_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReviewedBy)
                .WithMany()
                .HasForeignKey(e => e.ReviewedById)
                .HasConstraintName("fk_risk_archive_reviews_reviewed_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureBacktesting(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IncidentBacktest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("incident_backtests")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id").HasColumnType("int(11)");
            entity.Property(e => e.Note).HasColumnName("note").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.AssessedAt).HasColumnName("assessed_at").HasColumnType("datetime");
            entity.Property(e => e.AssessedById).HasColumnName("assessed_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            // One assessment per incident: re-assessing replaces the links, and the trail keeps the old ones.
            entity.HasIndex(e => e.IncidentId, "uq_incident_backtests_incident_id").IsUnique();
            entity.HasIndex(e => e.AssessedById, "idx_incident_backtests_assessed_by_id");

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .HasConstraintName("fk_incident_backtests_incident_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssessedBy)
                .WithMany()
                .HasForeignKey(e => e.AssessedById)
                .HasConstraintName("fk_incident_backtests_assessed_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IncidentBacktestRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("incident_backtest_risks")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.BacktestId).HasColumnName("backtest_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.BacktestId, e.RiskId }, "uq_incident_backtest_risks_backtest_id_risk_id")
                .IsUnique();
            entity.HasIndex(e => e.RiskId, "idx_incident_backtest_risks_risk_id");

            entity.HasOne(e => e.Backtest)
                .WithMany(b => b.Risks)
                .HasForeignKey(e => e.BacktestId)
                .HasConstraintName("fk_incident_backtest_risks_backtest_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_incident_backtest_risks_risk_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCommittees(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskCommittee>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_committees", t => t.HasCheckConstraint("ck_risk_committees_required_approvals",
                    "`required_approvals` >= 2 AND `required_approvals` <= 50"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Mandate).HasColumnName("mandate").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.RequiredApprovals).HasColumnName("required_approvals").HasColumnType("int(11)");
            entity.Property(e => e.RetiredAt).HasColumnName("retired_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.EntityId, "idx_risk_committees_entity_id");
            entity.HasIndex(e => e.UpdatedById, "idx_risk_committees_updated_by_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_risk_committees_entity_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_risk_committees_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskCommitteeMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_committee_members")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.CommitteeId).HasColumnName("committee_id").HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.CommitteeId, e.UserId }, "uq_risk_committee_members_committee_id_user_id")
                .IsUnique();
            entity.HasIndex(e => e.UserId, "idx_risk_committee_members_user_id");
            entity.HasIndex(e => e.CreatedById, "idx_risk_committee_members_created_by_id");

            entity.HasOne(e => e.Committee)
                .WithMany(c => c.Members)
                .HasForeignKey(e => e.CommitteeId)
                .HasConstraintName("fk_risk_committee_members_committee_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_risk_committee_members_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_risk_committee_members_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskCommitteeDecision>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_committee_decisions", t =>
                {
                    t.HasCheckConstraint("ck_risk_committee_decisions_kind", "`kind` >= 1 AND `kind` <= 2");
                    t.HasCheckConstraint("ck_risk_committee_decisions_status", "`status` >= 1 AND `status` <= 4");
                    t.HasCheckConstraint("ck_risk_committee_decisions_required_approvals",
                        "`required_approvals` >= 2 AND `required_approvals` <= 50");
                    t.HasCheckConstraint("ck_risk_committee_decisions_closed",
                        "(`status` = 1 AND `closed_at` IS NULL) OR (`status` <> 1 AND `closed_at` IS NOT NULL)");
                    t.HasCheckConstraint("ck_risk_committee_decisions_withdrawn",
                        "`status` <> 4 OR `withdrawal_reason` IS NOT NULL");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.CommitteeId).HasColumnName("committee_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.RenewsAcceptanceId).HasColumnName("renews_acceptance_id").HasColumnType("int(11)");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.RequiredApprovals).HasColumnName("required_approvals").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.BusinessJustification).HasColumnName("business_justification").HasColumnType("text");
            entity.Property(e => e.CompensatingControls).HasColumnName("compensating_controls").HasColumnType("text");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime");
            entity.Property(e => e.MinutesReference).HasColumnName("minutes_reference").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.OpenedById).HasColumnName("opened_by_id").HasColumnType("int(11)");
            entity.Property(e => e.OpenedAt).HasColumnName("opened_at").HasColumnType("datetime");
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at").HasColumnType("datetime");
            entity.Property(e => e.WithdrawalReason).HasColumnName("withdrawal_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.AcceptanceId).HasColumnName("acceptance_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.Version).HasColumnName("version").HasColumnType("int(11)").IsConcurrencyToken();

            entity.HasIndex(e => e.CommitteeId, "idx_risk_committee_decisions_committee_id");
            entity.HasIndex(e => new { e.RiskId, e.Status }, "idx_risk_committee_decisions_risk_id_status");
            entity.HasIndex(e => e.RenewsAcceptanceId, "idx_risk_committee_decisions_renews_acceptance_id");
            entity.HasIndex(e => e.AcceptanceId, "idx_risk_committee_decisions_acceptance_id");
            entity.HasIndex(e => e.OpenedById, "idx_risk_committee_decisions_opened_by_id");

            entity.HasOne(e => e.Committee)
                .WithMany()
                .HasForeignKey(e => e.CommitteeId)
                .HasConstraintName("fk_risk_committee_decisions_committee_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_committee_decisions_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RenewsAcceptance)
                .WithMany()
                .HasForeignKey(e => e.RenewsAcceptanceId)
                .HasConstraintName("fk_risk_committee_decisions_renews_acceptance_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Acceptance)
                .WithMany()
                .HasForeignKey(e => e.AcceptanceId)
                .HasConstraintName("fk_risk_committee_decisions_acceptance_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.OpenedBy)
                .WithMany()
                .HasForeignKey(e => e.OpenedById)
                .HasConstraintName("fk_risk_committee_decisions_opened_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskCommitteeVote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_committee_votes", t => t.HasCheckConstraint("ck_risk_committee_votes_choice",
                    "`choice` >= 1 AND `choice` <= 3"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.DecisionId).HasColumnName("decision_id").HasColumnType("int(11)");
            entity.Property(e => e.VoterId).HasColumnName("voter_id").HasColumnType("int(11)");
            entity.Property(e => e.Choice).HasColumnName("choice").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Comment).HasColumnName("comment").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.CastAt).HasColumnName("cast_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // One vote per member and decision, in the database and not only in code: two votes racing for the last
            // approval must not both count.
            entity.HasIndex(e => new { e.DecisionId, e.VoterId }, "uq_risk_committee_votes_decision_id_voter_id")
                .IsUnique();
            entity.HasIndex(e => e.VoterId, "idx_risk_committee_votes_voter_id");

            entity.HasOne(e => e.Decision)
                .WithMany(d => d.Votes)
                .HasForeignKey(e => e.DecisionId)
                .HasConstraintName("fk_risk_committee_votes_decision_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Voter)
                .WithMany()
                .HasForeignKey(e => e.VoterId)
                .HasConstraintName("fk_risk_committee_votes_voter_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
