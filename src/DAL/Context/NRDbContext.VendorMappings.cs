using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// T263 (S40 §6.6, §7.2) — the vendor → entity rules and the vendor identities of hosts they are matched against.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<VendorEntityMapping> VendorEntityMappings { get; set; } = null!;

    public virtual DbSet<HostExternalId> HostExternalIds { get; set; } = null!;

    private static void ConfigureVendorMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VendorEntityMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("vendor_entity_mappings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Provider).HasColumnName("provider").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.MatchKind).HasColumnName("match_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.MatchValue).HasColumnName("match_value").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.MatchLabel).HasColumnName("match_label").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Priority).HasColumnName("priority").HasColumnType("int(11)");
            entity.Property(e => e.RestampPolicy).HasColumnName("restamp_policy").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            // Case-insensitive under the table's collation, as the sync matches the tags: two rules for "BU: Law" and
            // "bu: law" on one connection would be the same rule twice.
            entity.HasIndex(e => new { e.Provider, e.ConnectionId, e.MatchKind, e.MatchValue },
                "uq_vendor_entity_mappings_provider_connection_kind_value").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_vendor_entity_mappings_entity_id");
            entity.HasIndex(e => e.CreatedById, "idx_vendor_entity_mappings_created_by_id");

            // A rule for a deleted entity has nothing to attribute to; the hosts it stamped keep their entity until the
            // entity's own deletion handles them.
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_vendor_entity_mappings_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_vendor_entity_mappings_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HostExternalId>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("host_external_ids")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnName("host_id").HasColumnType("int(11)");
            entity.Property(e => e.Provider).HasColumnName("provider").HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.Tags).HasColumnName("tags").HasColumnType("text");
            entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").HasColumnType("datetime");
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.Provider, e.ExternalId }, "uq_host_external_ids_provider_external_id").IsUnique();
            entity.HasIndex(e => e.HostId, "idx_host_external_ids_host_id");
            entity.HasIndex(e => new { e.Provider, e.ConnectionId }, "idx_host_external_ids_provider_connection_id");

            entity.HasOne(e => e.Host)
                .WithMany()
                .HasForeignKey(e => e.HostId)
                .HasConstraintName("fk_host_external_ids_host_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
