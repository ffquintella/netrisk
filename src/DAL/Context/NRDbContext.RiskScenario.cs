using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.2 (S42 §4): the structured scenario and evidence confidence on <c>risks</c>, the standalone
/// hypothesis on <c>pending_risks</c>, and the near-miss kind on <c>incidents</c>.
///
/// Columns only — no new table. Named per the Track 6 convention: snake_case via
/// <c>HasColumnName</c>, <c>int</c> + <c>HasConversion</c> for the three enums, <c>text</c> for the
/// free-text scenario fields (never BLOB, never <c>char(n)</c>), <c>fk_</c>/<c>idx_</c> prefixes.
/// </summary>
public partial class NRDbContext
{
    private static void ConfigureRiskScenario(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Risk>(entity =>
        {
            entity.Property(e => e.ScenarioCause).HasColumnName("scenario_cause").HasColumnType("text");
            entity.Property(e => e.ScenarioVulnerability).HasColumnName("scenario_vulnerability")
                .HasColumnType("text");
            entity.Property(e => e.ScenarioCentralEvent).HasColumnName("scenario_central_event")
                .HasColumnType("text");
            entity.Property(e => e.ScenarioConsequences).HasColumnName("scenario_consequences")
                .HasColumnType("text");

            // Nullable and with no default: NULL is "not declared", which a legacy risk honestly is.
            entity.Property(e => e.EvidenceConfidence).HasColumnName("evidence_confidence")
                .HasColumnType("int(11)")
                .HasConversion<int>();
        });

        modelBuilder.Entity<PendingRisk>(entity =>
        {
            // The two assessment columns became nullable (CLR int?) — a standalone hypothesis has no
            // assessment. Their store types are configured in NRDbContext.cs and do not change.
            entity.Property(e => e.Origin).HasColumnName("origin").HasColumnType("int(11)")
                .HasDefaultValue(PendingRiskOrigin.Assessment)
                .HasSentinel(default)
                .HasConversion<int>();

            entity.Property(e => e.SubmittedById).HasColumnName("submitted_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.SubmittedById, "idx_pending_risks_submitted_by_id");

            // The scope column (S42 §4.2). SetNull, as risks.entity_id: deleting an entity must not
            // delete the hypotheses filed under it — they become organization-wide.
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.HasIndex(e => e.EntityId, "idx_pending_risks_entity_id");
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_pending_risks_entity_id")
                .OnDelete(DeleteBehavior.SetNull);

            // SetNull: deleting a user must not delete the hypotheses they raised, nor be blocked by them.
            entity.HasOne(e => e.SubmittedBy)
                .WithMany()
                .HasForeignKey(e => e.SubmittedById)
                .HasConstraintName("fk_pending_risks_submitted_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)")
                .HasDefaultValue(IncidentKind.Incident)
                .HasSentinel(default)
                .HasConversion<int>();
        });
    }
}
