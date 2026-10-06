using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// The Stage 9.1 linkage chain (S41 §4.3): <c>risk_chain_links</c>, one row per link from a risk to
/// one node of the chain — an entity of a chain type, or a host.
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/
/// <c>idx_</c>/<c>uq_</c>/<c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c>
/// for the two enums, and no text column at all.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RiskChainLink> RiskChainLinks { get; set; } = null!;

    private static void ConfigureRiskChain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskChainLink>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            // The CHECK is declared on the model too, so the snapshot and the generated migration
            // carry the same DDL as Structure/88.sql: exactly one of the two targets.
            entity.ToTable("risk_chain_links", t => t.HasCheckConstraint("ck_risk_chain_links_one_target",
                    "((`entity_id` IS NOT NULL) + (`host_id` IS NOT NULL)) = 1"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.ChainLevel).HasColumnName("chain_level").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.HostId).HasColumnName("host_id").HasColumnType("int(11)");
            entity.Property(e => e.Origin).HasColumnName("origin").HasColumnType("int(11)")
                .HasConversion<int>()
                .HasDefaultValue(DAL.Enums.RiskChainLinkOrigin.Declared)
                .HasSentinel((DAL.Enums.RiskChainLinkOrigin)0);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            // NULLs never collide in a MariaDB unique index, so the host rows (entity_id NULL) do not
            // conflict in the first index and the entity rows do not conflict in the second.
            entity.HasIndex(e => new { e.RiskId, e.EntityId }, "uq_risk_chain_links_risk_id_entity_id")
                .IsUnique();
            entity.HasIndex(e => new { e.RiskId, e.HostId }, "uq_risk_chain_links_risk_id_host_id")
                .IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_risk_chain_links_entity_id");
            entity.HasIndex(e => e.HostId, "idx_risk_chain_links_host_id");
            entity.HasIndex(e => e.CreatedById, "idx_risk_chain_links_created_by_id");

            entity.HasOne(e => e.Risk)
                .WithMany(r => r.ChainLinks)
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_chain_links_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            // CASCADE, as risk_to_entity does: deleting a node removes the links to it (S41 §11, D11).
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_risk_chain_links_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Host)
                .WithMany()
                .HasForeignKey(e => e.HostId)
                .HasConstraintName("fk_risk_chain_links_host_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_risk_chain_links_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
