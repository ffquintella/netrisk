using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Persisted task-dependency edges and the blocked-task override record for incident response
/// plans (Track 2 milestone 2.4.3).
///
/// Configured here rather than in the generated <c>OnModelCreating</c> so the generated file stays
/// regenerable, and named per the Track 6 convention — snake_case columns set through
/// <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c> prefixes — because new schema is
/// expected to be born compliant rather than added to the drift.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<IncidentResponsePlanTaskDependency> IncidentResponsePlanTaskDependencies { get; set; } = null!;

    private static void ConfigureIrpDependencies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IncidentResponsePlanTaskDependency>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("incident_response_plan_task_dependencies")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.TaskId).HasColumnName("task_id").HasColumnType("int(11)");
            entity.Property(e => e.DependsOnTaskId).HasColumnName("depends_on_task_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // The same edge twice would double-count in the schedule and makes no sense besides.
            entity.HasIndex(e => new { e.TaskId, e.DependsOnTaskId }, "uq_irptd_task_depends_on").IsUnique();
            entity.HasIndex(e => e.DependsOnTaskId, "idx_irptd_depends_on_task_id");

            // Deleting a task takes its edges with it in both directions; a dangling edge would
            // otherwise leave the graph unschedulable.
            //
            // Only the reference navigations are modelled — no inverse collections on the task.
            // Nothing needs them (the scheduler queries this set by task id), and declaring them
            // makes the model snapshot one that EF Core 10 + Pomelo cannot build a relational
            // model from, which breaks the schema-consistency guard.
            entity.HasOne(e => e.Task)
                .WithMany()
                .HasForeignKey(e => e.TaskId)
                .HasConstraintName("fk_irptd_task_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DependsOnTask)
                .WithMany()
                .HasForeignKey(e => e.DependsOnTaskId)
                .HasConstraintName("fk_irptd_depends_on_task_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentResponsePlanTask>(entity =>
        {
            entity.Property(e => e.OverrideReason)
                .HasColumnName("override_reason")
                .HasColumnType("text");

            entity.Property(e => e.OverriddenById)
                .HasColumnName("overridden_by_id")
                .HasColumnType("int(11)");

            entity.Property(e => e.OverriddenAt)
                .HasColumnName("overridden_at")
                .HasColumnType("datetime");

            // Named explicitly; EF's default would be IX_… which breaks the Track 6 index convention.
            entity.HasIndex(e => e.OverriddenById, "idx_irpt_overridden_by_id");

            entity.HasOne(e => e.OverriddenBy)
                .WithMany()
                .HasForeignKey(e => e.OverriddenById)
                .HasConstraintName("fk_irpt_overridden_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
