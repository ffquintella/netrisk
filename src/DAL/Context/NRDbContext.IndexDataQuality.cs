using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M58 (S39 §14, D-15, D-16) — the incident and assessment data the risk index read with a documented workaround,
/// corrected at the source. Track 6 naming: snake_case columns, enums as <c>int</c> with an explicit conversion, UTC
/// <c>datetime</c>, and the FK <c>assessment_answer_id</c> with <c>fk_assessment_run_answers_assessment_answer_id</c>.
/// No new scope filter: <c>incidents</c> and <c>assessment_runs</c> are filtered, and the answers inherit their run's.
/// </summary>
public partial class NRDbContext
{
    private static void ConfigureIndexDataQuality(ModelBuilder modelBuilder)
    {
        // T279 (D-15): severity replaces the category anchor when set; resolved_at is the closure date the decay starts at.
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.Property(e => e.Severity)
                .HasColumnName("severity")
                .HasColumnType("int(11)")
                .HasConversion<int?>();

            entity.Property(e => e.ResolvedAt)
                .HasColumnName("resolved_at")
                .HasColumnType("datetime");
        });

        // T280 (D-16): the submission time and the server's score of a run.
        modelBuilder.Entity<AssessmentRun>(entity =>
        {
            entity.Property(e => e.SubmittedAt)
                .HasColumnName("submitted_at")
                .HasColumnType("datetime");

            entity.Property(e => e.Score)
                .HasColumnName("score")
                .HasColumnType("double");
        });

        // T280 (D-16): an answer names its option by id. Deleting the option keeps the answer (its text and comment stay)
        // and drops the link; a submitted run's score was recorded at submission and does not move.
        modelBuilder.Entity<AssessmentRunAnswer>(entity =>
        {
            entity.Property(e => e.AssessmentAnswerId)
                .HasColumnName("assessment_answer_id")
                .HasColumnType("int(11)");

            entity.HasIndex(e => e.AssessmentAnswerId, "idx_assessment_run_answers_assessment_answer_id");

            entity.HasOne(e => e.AssessmentAnswer)
                .WithMany()
                .HasForeignKey(e => e.AssessmentAnswerId)
                .HasConstraintName("fk_assessment_run_answers_assessment_answer_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
