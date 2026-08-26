using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Track 8 milestone 8.6 — the business risk acceptance portal's schema: who is appointed to review
/// an entity's risks, the periodic campaigns generated for them, and the ranked decision each
/// campaign item carries.
///
/// A separate partial and a separate migration from the governance core. The portal sits last in the
/// dependency chain — it consumes 8.1 acceptance, 8.3 appetite and 8.5 tasks — and keeping its three
/// tables in their own upgrade phase means an installation that does not deploy the portal can see
/// exactly which phase it is declining.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<EntityRiskReviewer> EntityRiskReviewers { get; set; } = null!;

    public virtual DbSet<RiskReviewCampaign> RiskReviewCampaigns { get; set; } = null!;

    public virtual DbSet<RiskReviewCampaignItem> RiskReviewCampaignItems { get; set; } = null!;

    /// <summary>
    /// Entity-scoping predicates for the portal. A reviewer appointment and a campaign both carry
    /// their entity directly; an item inherits from its campaign. Business rankings and decision
    /// notes are exactly the material a multi-tenant install must not leak.
    /// </summary>
    private void ApplyReviewPortalQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityRiskReviewer>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ScopeEntityIds.Contains(e.EntityId));

        modelBuilder.Entity<RiskReviewCampaign>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ScopeEntityIds.Contains(e.EntityId));

        modelBuilder.Entity<RiskReviewCampaignItem>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskReviewCampaigns.Any(c => c.Id == e.CampaignId));
    }

    /// <summary>8.6.2–8.6.4 — reviewer designation, campaigns and their items.</summary>
    private static void ConfigureReviewPortal(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityRiskReviewer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("entity_risk_reviewers")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.IsPrimary).HasColumnName("is_primary").HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.AppointedById).HasColumnName("appointed_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.EntityId, e.UserId }, "uq_entity_risk_reviewers_entity_user")
                .IsUnique();
            entity.HasIndex(e => e.UserId, "idx_entity_risk_reviewers_user_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_entity_risk_reviewers_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_entity_risk_reviewers_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AppointedBy)
                .WithMany()
                .HasForeignKey(e => e.AppointedById)
                .HasConstraintName("fk_entity_risk_reviewers_appointed_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskReviewCampaign>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_review_campaigns")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start").HasColumnType("datetime");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end").HasColumnType("datetime");
            entity.Property(e => e.DueDate).HasColumnName("due_date").HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)")
                .HasDefaultValue(RiskReviewCampaignStatus.Open)
                .HasSentinel(default)
                .HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime");
            entity.Property(e => e.LastNotifiedDaysBefore).HasColumnName("last_notified_days_before")
                .HasColumnType("int(11)");

            // One open campaign per entity per period: the generator runs daily and must converge on
            // the same campaign rather than creating a new one every morning.
            entity.HasIndex(e => new { e.EntityId, e.PeriodStart, e.PeriodEnd },
                "uq_risk_review_campaigns_entity_period").IsUnique();
            entity.HasIndex(e => new { e.Status, e.DueDate }, "idx_risk_review_campaigns_status_due_date");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_risk_review_campaigns_entity_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RiskReviewCampaignItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_review_campaign_items")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Rank).HasColumnName("rank").HasColumnType("int(11)");
            entity.Property(e => e.Decision).HasColumnName("decision").HasColumnType("int(11)")
                .HasDefaultValue(RiskReviewDecision.Pending)
                .HasSentinel(default)
                .HasConversion<int>();
            entity.Property(e => e.DecisionNotes).HasColumnName("decision_notes").HasColumnType("text");
            entity.Property(e => e.DecidedById).HasColumnName("decided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.DecidedAt).HasColumnName("decided_at").HasColumnType("datetime");
            entity.Property(e => e.RiskAcceptanceId).HasColumnName("risk_acceptance_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.EscalatedToId).HasColumnName("escalated_to_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.CampaignId, e.RiskId }, "uq_risk_review_campaign_items_campaign_risk")
                .IsUnique();
            entity.HasIndex(e => e.RiskId, "idx_risk_review_campaign_items_risk_id");

            entity.HasOne(e => e.Campaign)
                .WithMany(c => c.Items)
                .HasForeignKey(e => e.CampaignId)
                .HasConstraintName("fk_risk_review_campaign_items_campaign_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_review_campaign_items_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DecidedBy)
                .WithMany()
                .HasForeignKey(e => e.DecidedById)
                .HasConstraintName("fk_risk_review_campaign_items_decided_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.EscalatedTo)
                .WithMany()
                .HasForeignKey(e => e.EscalatedToId)
                .HasConstraintName("fk_risk_review_campaign_items_escalated_to_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RiskAcceptance)
                .WithMany()
                .HasForeignKey(e => e.RiskAcceptanceId)
                .HasConstraintName("fk_risk_review_campaign_items_risk_acceptance_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
