using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M56, T265–T267 (S40 §7.2, §7.4) — the Tenable connection and the per-source vendor readings (ACR and AES) it collects.
/// Connections are global configuration (no filter); a reading carries the entity of its host and is filtered on it.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<TenableConnection> TenableConnections { get; set; } = null!;

    public virtual DbSet<VendorScore> VendorScores { get; set; } = null!;

    private static void ConfigureTenable(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenableConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("tenable_connections")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(120)").HasMaxLength(120);
            entity.Property(e => e.Product).HasColumnName("product").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.BaseUrl).HasColumnName("base_url").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.EncryptedAccessKey).HasColumnName("encrypted_access_key").HasColumnType("text");
            entity.Property(e => e.EncryptedSecretKey).HasColumnName("encrypted_secret_key").HasColumnType("text");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncIntervalHours).HasColumnName("sync_interval_hours").HasColumnType("int(11)");
            entity.Property(e => e.SyncFindings).HasColumnName("sync_findings").HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncAssets).HasColumnName("sync_assets").HasColumnType("tinyint(1)");
            entity.Property(e => e.CollectAcr).HasColumnName("collect_acr").HasColumnType("tinyint(1)");
            entity.Property(e => e.CollectAes).HasColumnName("collect_aes").HasColumnType("tinyint(1)");
            entity.Property(e => e.MinSeverity).HasColumnName("min_severity").HasColumnType("int(11)");
            entity.Property(e => e.InitialWindowDays).HasColumnName("initial_window_days").HasColumnType("int(11)");
            entity.Property(e => e.IncludeUnlicensed).HasColumnName("include_unlicensed").HasColumnType("tinyint(1)");
            entity.Property(e => e.AllowInvalidCertificate).HasColumnName("allow_invalid_certificate")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.FindingsCursor).HasColumnName("findings_cursor").HasColumnType("datetime");
            entity.Property(e => e.AssetsCursor).HasColumnName("assets_cursor").HasColumnType("datetime");
            entity.Property(e => e.NextDueAt).HasColumnName("next_due_at").HasColumnType("datetime");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at").HasColumnType("datetime");
            entity.Property(e => e.LastSyncStatus).HasColumnName("last_sync_status").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.LastSyncError).HasColumnName("last_sync_error").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.Name, "uq_tenable_connections_name").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_tenable_connections_entity_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_tenable_connections_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<VendorScore>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("vendor_scores")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.Source).HasColumnName("source").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectKind).HasColumnName("object_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.HostId).HasColumnName("host_id").HasColumnType("int(11)");
            entity.Property(e => e.ExternalRef).HasColumnName("external_ref").HasColumnType("varchar(128)").HasMaxLength(128);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("double");
            entity.Property(e => e.ScaleMax).HasColumnName("scale_max").HasColumnType("double");
            entity.Property(e => e.Level).HasColumnName("level").HasColumnType("varchar(16)").HasMaxLength(16);
            entity.Property(e => e.Details).HasColumnName("details").HasColumnType("longtext");
            entity.Property(e => e.FirstCapturedAt).HasColumnName("first_captured_at").HasColumnType("datetime");
            entity.Property(e => e.LastCapturedAt).HasColumnName("last_captured_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.Source, e.HostId }, "idx_vendor_scores_source_host");
            entity.HasIndex(e => new { e.Source, e.ExternalRef }, "idx_vendor_scores_source_ref");
            entity.HasIndex(e => e.EntityId, "idx_vendor_scores_entity_id");
            entity.HasIndex(e => e.HostId, "idx_vendor_scores_host_id");

            // A reading of a deleted host has no object left to describe (S40 §7.2).
            entity.HasOne(e => e.Host)
                .WithMany()
                .HasForeignKey(e => e.HostId)
                .HasConstraintName("fk_vendor_scores_host_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
