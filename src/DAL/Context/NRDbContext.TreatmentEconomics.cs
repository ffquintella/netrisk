using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.6 — treatment economics (S47 §4): <c>mitigation_economics</c> (the treatment option and the monetary
/// cost of a mitigation), <c>mitigation_dependencies</c> (Gate D's dependency constraint) and
/// <c>risk_targets</c> (the target risk level of the register).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enum, <c>varchar(n)</c> and
/// never <c>char(n)</c>. Every CHECK is declared on the model too, so the snapshot carries the same DDL as
/// <c>Structure/95.sql</c>. No inverse navigation on <see cref="Mitigation"/> or <see cref="Risk"/>, so the
/// payloads of <c>GET /Mitigations</c> and <c>GET /Risks</c> do not change. The query filters are in
/// <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<MitigationEconomics> MitigationEconomics { get; set; } = null!;

    public virtual DbSet<MitigationDependency> MitigationDependencies { get; set; } = null!;

    public virtual DbSet<RiskTarget> RiskTargets { get; set; } = null!;

    private static void ConfigureTreatmentEconomics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MitigationEconomics>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("mitigation_economics", t =>
                {
                    t.HasCheckConstraint("ck_mitigation_economics_treatment_option",
                        "`treatment_option` >= 1 AND `treatment_option` <= 4");
                    t.HasCheckConstraint("ck_mitigation_economics_cost_complete",
                        "(`cost_one_time` IS NULL) = (`cost_annual` IS NULL) AND " +
                        "(`cost_annual` IS NULL) = (`cost_side_effects_annual` IS NULL)");
                    t.HasCheckConstraint("ck_mitigation_economics_cost_non_negative",
                        "(`cost_one_time` IS NULL OR `cost_one_time` >= 0) AND " +
                        "(`cost_annual` IS NULL OR `cost_annual` >= 0) AND " +
                        "(`cost_side_effects_annual` IS NULL OR `cost_side_effects_annual` >= 0)");
                    t.HasCheckConstraint("ck_mitigation_economics_horizon",
                        "`cost_horizon_years` IS NULL OR (`cost_horizon_years` >= 1 AND `cost_horizon_years` <= 30)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.MitigationId).HasColumnName("mitigation_id").HasColumnType("int(11)");
            entity.Property(e => e.TreatmentOption).HasColumnName("treatment_option").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.TransferCounterparty).HasColumnName("transfer_counterparty")
                .HasColumnType("varchar(255)").HasMaxLength(255);
            entity.Property(e => e.CostOneTime).HasColumnName("cost_one_time").HasColumnType("decimal(18,2)")
                .HasPrecision(18, 2);
            entity.Property(e => e.CostAnnual).HasColumnName("cost_annual").HasColumnType("decimal(18,2)")
                .HasPrecision(18, 2);
            entity.Property(e => e.CostSideEffectsAnnual).HasColumnName("cost_side_effects_annual")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.CostHorizonYears).HasColumnName("cost_horizon_years").HasColumnType("int(11)");
            entity.Property(e => e.CostBasis).HasColumnName("cost_basis").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.EffortPersonDays).HasColumnName("effort_person_days").HasColumnType("decimal(10,2)")
                .HasPrecision(10, 2);
            entity.Property(e => e.DurationDays).HasColumnName("duration_days").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.Ignore(e => e.CostDeclared);

            entity.HasIndex(e => e.MitigationId, "uq_mitigation_economics_mitigation_id").IsUnique();
            entity.HasIndex(e => e.UpdatedById, "idx_mitigation_economics_updated_by_id");

            entity.HasOne(e => e.Mitigation)
                .WithMany()
                .HasForeignKey(e => e.MitigationId)
                .HasConstraintName("fk_mitigation_economics_mitigation_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_mitigation_economics_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MitigationDependency>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("mitigation_dependencies", t => t.HasCheckConstraint("ck_mitigation_dependencies_not_self",
                    "`mitigation_id` <> `prerequisite_id`"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.MitigationId).HasColumnName("mitigation_id").HasColumnType("int(11)");
            entity.Property(e => e.PrerequisiteId).HasColumnName("prerequisite_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.MitigationId, e.PrerequisiteId },
                "uq_mitigation_dependencies_mitigation_id_prerequisite_id").IsUnique();
            entity.HasIndex(e => e.PrerequisiteId, "idx_mitigation_dependencies_prerequisite_id");
            entity.HasIndex(e => e.CreatedById, "idx_mitigation_dependencies_created_by_id");

            entity.HasOne(e => e.Mitigation)
                .WithMany()
                .HasForeignKey(e => e.MitigationId)
                .HasConstraintName("fk_mitigation_dependencies_mitigation_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Prerequisite)
                .WithMany()
                .HasForeignKey(e => e.PrerequisiteId)
                .HasConstraintName("fk_mitigation_dependencies_prerequisite_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_mitigation_dependencies_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskTarget>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_targets", t =>
                {
                    t.HasCheckConstraint("ck_risk_targets_level",
                        "`target_score` IS NOT NULL OR `target_expected_loss` IS NOT NULL");
                    t.HasCheckConstraint("ck_risk_targets_score",
                        "`target_score` IS NULL OR (`target_score` >= 0 AND `target_score` <= 10)");
                    t.HasCheckConstraint("ck_risk_targets_expected_loss",
                        "`target_expected_loss` IS NULL OR `target_expected_loss` >= 0");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.TargetScore).HasColumnName("target_score").HasColumnType("decimal(4,2)")
                .HasPrecision(4, 2);
            entity.Property(e => e.TargetExpectedLoss).HasColumnName("target_expected_loss")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.TargetDate).HasColumnName("target_date").HasColumnType("date");
            entity.Property(e => e.Rationale).HasColumnName("rationale").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.SetById).HasColumnName("set_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.RiskId, "uq_risk_targets_risk_id").IsUnique();
            entity.HasIndex(e => e.SetById, "idx_risk_targets_set_by_id");

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_targets_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SetBy)
                .WithMany()
                .HasForeignKey(e => e.SetById)
                .HasConstraintName("fk_risk_targets_set_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
