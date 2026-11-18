using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M55, T264 (S40 §6.7, §8.5): the justification of each risk-context batch (<see cref="EntityRiskContextNote"/>). Track 6
/// naming. The entity is CASCADE — a note is about the entity and goes with it, as its property rows do; the author is
/// SET NULL, so removing a user does not remove the reason a weight moved.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<EntityRiskContextNote> EntityRiskContextNotes { get; set; } = null!;

    private static void ConfigureEntityRiskContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityRiskContextNote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("entity_risk_context_notes")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.BatchId).HasColumnName("batch_id").HasColumnType("varchar(36)").HasMaxLength(36);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Justification).HasColumnName("justification").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.EntityId, "idx_entity_risk_context_notes_entity_id");
            entity.HasIndex(e => e.BatchId, "idx_entity_risk_context_notes_batch_id");
            entity.HasIndex(e => e.CreatedById, "idx_entity_risk_context_notes_created_by_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_entity_risk_context_notes_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_entity_risk_context_notes_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
