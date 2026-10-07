using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// A comment and evidence files on each answer of an assessment run (GitHub #80, T297, S44).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, the FK column
/// <c>assessment_run_answer_id</c> with constraint <c>fk_nr_files_assessment_run_answer_id</c> and a
/// configured navigation, and the index <c>idx_nr_files_assessment_run_answer_id</c>.
///
/// No new scope filter: <c>nr_files</c> already filters on its own <c>entity_id</c>, which the files
/// service stamps from the assessment, and <c>assessment_run_answers</c> already inherits the run's.
/// </summary>
public partial class NRDbContext
{
    private static void ConfigureAssessmentEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssessmentRunAnswer>(entity =>
        {
            // Free text, so TEXT rather than a varchar; the 4 000-character bound is the API's.
            entity.Property(e => e.Comment)
                .HasColumnName("comment")
                .HasColumnType("text");
        });

        modelBuilder.Entity<NrFile>(entity =>
        {
            entity.Property(e => e.AssessmentRunAnswerId)
                .HasColumnName("assessment_run_answer_id")
                .HasColumnType("int(11)");

            entity.HasIndex(e => e.AssessmentRunAnswerId, "idx_nr_files_assessment_run_answer_id");

            // Deleting the answer — or the run, which cascades to its answers — takes the evidence with
            // it: a file has no meaning detached from the answer it substantiates.
            entity.HasOne(e => e.AssessmentRunAnswer)
                .WithMany()
                .HasForeignKey(e => e.AssessmentRunAnswerId)
                .HasConstraintName("fk_nr_files_assessment_run_answer_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
