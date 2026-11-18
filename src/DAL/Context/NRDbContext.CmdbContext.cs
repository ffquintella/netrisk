using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M58, T291 (S39 §3.2 CMDB-INET / CMDB-REL, §5.3) — what the CMDB says about a host that the risk index reads: whether it
/// faces the internet (three columns on <c>hosts</c>, already filtered) and which applications it runs
/// (<c>host_application_links</c>, filtered through its host in <c>NRDbContext.EntityScope</c>). Track 6 naming.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<HostApplicationLink> HostApplicationLinks { get; set; } = null!;

    private static void ConfigureCmdbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Host>(entity =>
        {
            entity.Property(e => e.InternetFacing).HasColumnName("internet_facing").HasColumnType("tinyint(1)");
            entity.Property(e => e.InternetFacingSource).HasColumnName("internet_facing_source")
                .HasColumnType("varchar(32)").HasMaxLength(32);
            entity.Property(e => e.InternetFacingSeenAt).HasColumnName("internet_facing_seen_at").HasColumnType("datetime");
        });

        modelBuilder.Entity<HostApplicationLink>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("host_application_links")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnName("host_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Source).HasColumnName("source").HasColumnType("varchar(32)").HasMaxLength(32);
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("datetime");

            // One row per pair, whoever reported it: the connection that first reported it owns it, and refreshes it.
            entity.HasIndex(e => new { e.HostId, e.EntityId }, "uq_host_application_links_host_id_entity_id").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_host_application_links_entity_id");
            entity.HasIndex(e => e.ConnectionId, "idx_host_application_links_connection_id");

            // A relation outlives neither end, nor the connection that reported it (manual rows carry no connection).
            entity.HasOne(e => e.Host)
                .WithMany()
                .HasForeignKey(e => e.HostId)
                .HasConstraintName("fk_host_application_links_host_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_host_application_links_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Connection)
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_host_application_links_connection_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
