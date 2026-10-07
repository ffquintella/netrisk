using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.3 — business impact analysis and continuity (S43 §4): <c>business_impact_analyses</c>,
/// <c>bia_dependencies</c> and <c>restoration_tests</c>.
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/
/// <c>idx_</c>/<c>uq_</c>/<c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c>
/// for the outcome, <c>varchar(n)</c> or <c>text</c> for text and never <c>char(n)</c>. Every CHECK is
/// declared on the model too, so the snapshot and the migration carry the same DDL as
/// <c>Structure/90.sql</c>.
///
/// No inverse navigation is added to <see cref="Entity"/>, so the payload of <c>GET /Entities</c> does
/// not change. None of the three tables is entity-scoped: processes and services carry no scope column
/// and <c>entities</c> has no filter (S43 §11, D12).
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<BusinessImpactAnalysis> BusinessImpactAnalyses { get; set; } = null!;

    public virtual DbSet<BiaDependency> BiaDependencies { get; set; } = null!;

    public virtual DbSet<RestorationTest> RestorationTests { get; set; } = null!;

    private static void ConfigureContinuity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessImpactAnalysis>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("business_impact_analyses", t =>
                {
                    t.HasCheckConstraint("ck_business_impact_analyses_declared",
                        "`mtpd_minutes` IS NOT NULL OR `rto_minutes` IS NOT NULL OR `rpo_minutes` IS NOT NULL");
                    t.HasCheckConstraint("ck_business_impact_analyses_non_negative",
                        "(`mtpd_minutes` IS NULL OR `mtpd_minutes` >= 0) AND (`rto_minutes` IS NULL OR `rto_minutes` >= 0) AND (`rpo_minutes` IS NULL OR `rpo_minutes` >= 0)");
                    t.HasCheckConstraint("ck_business_impact_analyses_rto_within_mtpd",
                        "`rto_minutes` IS NULL OR `mtpd_minutes` IS NULL OR `rto_minutes` <= `mtpd_minutes`");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.MtpdMinutes).HasColumnName("mtpd_minutes").HasColumnType("int(11)");
            entity.Property(e => e.RtoMinutes).HasColumnName("rto_minutes").HasColumnType("int(11)");
            entity.Property(e => e.RpoMinutes).HasColumnName("rpo_minutes").HasColumnType("int(11)");
            entity.Property(e => e.AssessedAt).HasColumnName("assessed_at").HasColumnType("datetime");
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.EntityId, "uq_business_impact_analyses_entity_id").IsUnique();
            entity.HasIndex(e => e.CreatedById, "idx_business_impact_analyses_created_by_id");
            entity.HasIndex(e => e.UpdatedById, "idx_business_impact_analyses_updated_by_id");

            // CASCADE, as the chain links are (S41 D11, S43 D14): deleting the node deletes its BIA.
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_business_impact_analyses_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_business_impact_analyses_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_business_impact_analyses_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BiaDependency>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("bia_dependencies", t => t.HasCheckConstraint("ck_bia_dependencies_not_self",
                    "`dependent_entity_id` <> `provider_entity_id`"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.DependentEntityId).HasColumnName("dependent_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.ProviderEntityId).HasColumnName("provider_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            // 57 characters: "continuity_dependencies" would have made this 65, past MariaDB's 64.
            entity.HasIndex(e => new { e.DependentEntityId, e.ProviderEntityId },
                    "uq_bia_dependencies_dependent_entity_id_provider_entity_id")
                .IsUnique();
            entity.HasIndex(e => e.ProviderEntityId, "idx_bia_dependencies_provider_entity_id");
            entity.HasIndex(e => e.CreatedById, "idx_bia_dependencies_created_by_id");

            entity.HasOne(e => e.Dependent)
                .WithMany()
                .HasForeignKey(e => e.DependentEntityId)
                .HasConstraintName("fk_bia_dependencies_dependent_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Provider)
                .WithMany()
                .HasForeignKey(e => e.ProviderEntityId)
                .HasConstraintName("fk_bia_dependencies_provider_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_bia_dependencies_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RestorationTest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("restoration_tests", t =>
                {
                    t.HasCheckConstraint("ck_restoration_tests_non_negative",
                        "(`achieved_rto_minutes` IS NULL OR `achieved_rto_minutes` >= 0) AND (`achieved_rpo_minutes` IS NULL OR `achieved_rpo_minutes` >= 0)");
                    t.HasCheckConstraint("ck_restoration_tests_measured",
                        "`outcome` <> 1 OR `achieved_rto_minutes` IS NOT NULL OR `achieved_rpo_minutes` IS NOT NULL");
                    t.HasCheckConstraint("ck_restoration_tests_void_complete",
                        "(`voided_at` IS NULL) = (`void_reason` IS NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.TestedAt).HasColumnName("tested_at").HasColumnType("datetime");
            entity.Property(e => e.Outcome).HasColumnName("outcome").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.AchievedRtoMinutes).HasColumnName("achieved_rto_minutes").HasColumnType("int(11)");
            entity.Property(e => e.AchievedRpoMinutes).HasColumnName("achieved_rpo_minutes").HasColumnType("int(11)");
            entity.Property(e => e.DeclaredRtoMinutes).HasColumnName("declared_rto_minutes").HasColumnType("int(11)");
            entity.Property(e => e.DeclaredRpoMinutes).HasColumnName("declared_rpo_minutes").HasColumnType("int(11)");
            entity.Property(e => e.EvidenceReference).HasColumnName("evidence_reference")
                .HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.RecordedById).HasColumnName("recorded_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidedAt).HasColumnName("voided_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedById).HasColumnName("voided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason").HasColumnType("varchar(500)")
                .HasMaxLength(500);

            entity.HasIndex(e => new { e.EntityId, e.TestedAt }, "idx_restoration_tests_entity_id_tested_at");
            entity.HasIndex(e => e.RecordedById, "idx_restoration_tests_recorded_by_id");
            entity.HasIndex(e => e.VoidedById, "idx_restoration_tests_voided_by_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_restoration_tests_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RecordedBy)
                .WithMany()
                .HasForeignKey(e => e.RecordedById)
                .HasConstraintName("fk_restoration_tests_recorded_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.VoidedBy)
                .WithMany()
                .HasForeignKey(e => e.VoidedById)
                .HasConstraintName("fk_restoration_tests_voided_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
