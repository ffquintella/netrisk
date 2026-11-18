using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M57, T270–T276 (S40 §7.2, §7.3, §7.4) — the Vision One and SecurityScorecard posture expansion: vendor assets and their
/// readings, the per-issue-type score impact of a SecurityScorecard factor, and the new columns on the connections, the
/// factors, hosts and findings. Vendor assets carry the connection's entity and are filtered on it.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<VendorAsset> VendorAssets { get; set; } = null!;

    public virtual DbSet<SecurityScorecardIssueSummary> SecurityScorecardIssueSummaries { get; set; } = null!;

    private static void ConfigureVendorPosture(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VendorAsset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("vendor_assets")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.Provider).HasColumnName("provider").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.AssetKind).HasColumnName("asset_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.Criticality).HasColumnName("criticality").HasColumnType("int(11)");
            entity.Property(e => e.CriticalityRaw).HasColumnName("criticality_raw").HasColumnType("varchar(32)")
                .HasMaxLength(32);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Tags).HasColumnName("tags").HasColumnType("text");
            entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").HasColumnType("datetime");
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.Provider, e.ConnectionId, e.AssetKind, e.ExternalId },
                "uq_vendor_assets_provider_connection_kind_external_id").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_vendor_assets_entity_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_vendor_assets_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<VendorScore>(entity =>
        {
            entity.Property(e => e.VendorAssetId).HasColumnName("vendor_asset_id").HasColumnType("bigint(20)");

            entity.HasIndex(e => new { e.Source, e.VendorAssetId }, "idx_vendor_scores_source_asset");
            entity.HasIndex(e => e.VendorAssetId, "idx_vendor_scores_vendor_asset_id");

            // A reading of an asset that is gone from the table has nothing left to describe (S40 §7.2).
            entity.HasOne(e => e.VendorAsset)
                .WithMany()
                .HasForeignKey(e => e.VendorAssetId)
                .HasConstraintName("fk_vendor_scores_vendor_asset_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityScorecardIssueSummary>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("security_scorecard_issue_summaries")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.FactorId).HasColumnName("factor_id").HasColumnType("int(11)");
            entity.Property(e => e.IssueType).HasColumnName("issue_type").HasColumnType("varchar(128)").HasMaxLength(128);
            entity.Property(e => e.Severity).HasColumnName("severity").HasColumnType("varchar(16)").HasMaxLength(16);
            entity.Property(e => e.IssueCount).HasColumnName("issue_count").HasColumnType("int(11)");
            entity.Property(e => e.TotalScoreImpact).HasColumnName("total_score_impact").HasColumnType("double");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => e.FactorId, "idx_security_scorecard_issue_summaries_factor_id");

            entity.HasOne(e => e.Factor)
                .WithMany(f => f.IssueSummaries)
                .HasForeignKey(e => e.FactorId)
                .HasConstraintName("fk_security_scorecard_issue_summaries_factor_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityScorecardFactor>(entity =>
        {
            entity.Property(e => e.IsBackfill).HasColumnName("is_backfill").HasColumnType("tinyint(1)");
        });

        modelBuilder.Entity<SecurityScorecardConnection>(entity =>
        {
            entity.Property(e => e.HistoryBackfillDays).HasColumnName("history_backfill_days").HasColumnType("int(11)");
            entity.Property(e => e.HistoryBackfilledFrom).HasColumnName("history_backfilled_from").HasColumnType("date");
            entity.Property(e => e.StoreIssueImpact).HasColumnName("store_issue_impact").HasColumnType("tinyint(1)");
            entity.Property(e => e.NextDueAt).HasColumnName("next_due_at").HasColumnType("datetime");
        });

        modelBuilder.Entity<TrendMicroConnection>(entity =>
        {
            entity.Property(e => e.SyncPosture).HasColumnName("sync_posture").HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncAttackSurfaceObjects).HasColumnName("sync_attack_surface_objects")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncWorkbenchAlerts).HasColumnName("sync_workbench_alerts").HasColumnType("tinyint(1)");
            entity.Property(e => e.NextDueAt).HasColumnName("next_due_at").HasColumnType("datetime");
        });

        modelBuilder.Entity<Host>(entity =>
        {
            entity.Property(e => e.CriticalitySource).HasColumnName("criticality_source").HasColumnType("varchar(32)")
                .HasMaxLength(32);
        });

        modelBuilder.Entity<Vulnerability>(entity =>
        {
            entity.Property(e => e.ExploitAttemptCount).HasColumnName("exploit_attempt_count").HasColumnType("int(11)");
            entity.Property(e => e.GlobalExploitActivity).HasColumnName("global_exploit_activity")
                .HasColumnType("varchar(16)").HasMaxLength(16);
        });
    }
}
