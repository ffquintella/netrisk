using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.7 — tail statistics and portfolio (S48 §4): <c>risk_loss_components</c> (the Phase 3 loss magnitude by
/// form of loss), <c>risk_tail_statistics</c> and <c>risk_tail_components</c> (E[L], P95 and CVaR95 with confidence
/// intervals per run, and each component's contribution), <c>risk_correlations</c> (the declared correlation the
/// portfolio aggregation uses) and <c>risk_appetite_tail_limits</c> (Gate B's monetary tolerances).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enums, <c>varchar(n)</c> and
/// never <c>char(n)</c>. Every CHECK is declared on the model too, so the snapshot carries the same DDL as
/// <c>Structure/96.sql</c>. No inverse navigation on <see cref="Risk"/>, <see cref="RiskScoring"/> or
/// <see cref="RiskAppetite"/>, so the payloads of <c>GET /Risks</c> and <c>GET /RiskAppetites</c> do not change. The
/// query filters are in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RiskLossComponent> RiskLossComponents { get; set; } = null!;

    public virtual DbSet<RiskTailStatistics> RiskTailStatistics { get; set; } = null!;

    public virtual DbSet<RiskTailComponent> RiskTailComponents { get; set; } = null!;

    public virtual DbSet<RiskCorrelation> RiskCorrelations { get; set; } = null!;

    public virtual DbSet<RiskAppetiteTailLimit> RiskAppetiteTailLimits { get; set; } = null!;

    private static void ConfigureTailRisk(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskLossComponent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_loss_components", t =>
                {
                    t.HasCheckConstraint("ck_risk_loss_components_component",
                        "`component` >= 1 AND `component` <= 7");
                    t.HasCheckConstraint("ck_risk_loss_components_range",
                        "`loss_min` >= 0 AND `loss_min` <= `loss_most_likely` AND `loss_most_likely` <= `loss_max`");
                    t.HasCheckConstraint("ck_risk_loss_components_fine_basis",
                        "`component` <> 6 OR `basis` IS NOT NULL");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Component).HasColumnName("component").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.LossMin).HasColumnName("loss_min").HasColumnType("double");
            entity.Property(e => e.LossMostLikely).HasColumnName("loss_most_likely").HasColumnType("double");
            entity.Property(e => e.LossMax).HasColumnName("loss_max").HasColumnType("double");
            entity.Property(e => e.Basis).HasColumnName("basis").HasColumnType("varchar(1000)").HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.RiskId, e.Component }, "uq_risk_loss_components_risk_id_component")
                .IsUnique();
            entity.HasIndex(e => e.UpdatedById, "idx_risk_loss_components_updated_by_id");

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_loss_components_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_risk_loss_components_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskTailStatistics>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_tail_statistics", t =>
                {
                    t.HasCheckConstraint("ck_risk_tail_statistics_run", "`run` >= 1 AND `run` <= 2");
                    t.HasCheckConstraint("ck_risk_tail_statistics_magnitude_source",
                        "`magnitude_source` >= 1 AND `magnitude_source` <= 2");
                    t.HasCheckConstraint("ck_risk_tail_statistics_iterations",
                        "`iterations` >= 1000 AND `iterations` <= 100000");
                    t.HasCheckConstraint("ck_risk_tail_statistics_probability_of_loss",
                        "`probability_of_loss` >= 0 AND `probability_of_loss` <= 1");
                    t.HasCheckConstraint("ck_risk_tail_statistics_mitigation_effectiveness",
                        "`mitigation_effectiveness` >= 0 AND `mitigation_effectiveness` <= 1");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Run).HasColumnName("run").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Iterations).HasColumnName("iterations").HasColumnType("int(11)");
            entity.Property(e => e.Seed).HasColumnName("seed").HasColumnType("int(11)");
            entity.Property(e => e.ConfidenceLevel).HasColumnName("confidence_level").HasColumnType("decimal(4,3)")
                .HasPrecision(4, 3);
            entity.Property(e => e.LefMin).HasColumnName("lef_min").HasColumnType("double");
            entity.Property(e => e.LefMostLikely).HasColumnName("lef_most_likely").HasColumnType("double");
            entity.Property(e => e.LefMax).HasColumnName("lef_max").HasColumnType("double");
            entity.Property(e => e.MagnitudeMin).HasColumnName("magnitude_min").HasColumnType("double");
            entity.Property(e => e.MagnitudeMostLikely).HasColumnName("magnitude_most_likely").HasColumnType("double");
            entity.Property(e => e.MagnitudeMax).HasColumnName("magnitude_max").HasColumnType("double");
            entity.Property(e => e.MagnitudeSource).HasColumnName("magnitude_source").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.MitigationEffectiveness).HasColumnName("mitigation_effectiveness")
                .HasColumnType("double");
            entity.Property(e => e.ExpectedLoss).HasColumnName("expected_loss").HasColumnType("double");
            entity.Property(e => e.ExpectedLossCiLow).HasColumnName("expected_loss_ci_low").HasColumnType("double");
            entity.Property(e => e.ExpectedLossCiHigh).HasColumnName("expected_loss_ci_high").HasColumnType("double");
            entity.Property(e => e.P95).HasColumnName("p95").HasColumnType("double");
            entity.Property(e => e.P95CiLow).HasColumnName("p95_ci_low").HasColumnType("double");
            entity.Property(e => e.P95CiHigh).HasColumnName("p95_ci_high").HasColumnType("double");
            entity.Property(e => e.Cvar95).HasColumnName("cvar95").HasColumnType("double");
            entity.Property(e => e.Cvar95CiLow).HasColumnName("cvar95_ci_low").HasColumnType("double");
            entity.Property(e => e.Cvar95CiHigh).HasColumnName("cvar95_ci_high").HasColumnType("double");
            entity.Property(e => e.ProbabilityOfLoss).HasColumnName("probability_of_loss").HasColumnType("double");
            entity.Property(e => e.ConditionalLoss).HasColumnName("conditional_loss").HasColumnType("double");
            entity.Property(e => e.ComputedAt).HasColumnName("computed_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.RiskId, e.Run }, "uq_risk_tail_statistics_risk_id_run").IsUnique();

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_tail_statistics_risk_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RiskTailComponent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_tail_components", t => t.HasCheckConstraint("ck_risk_tail_components_component",
                    "`component` >= 1 AND `component` <= 7"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.TailStatisticsId).HasColumnName("tail_statistics_id").HasColumnType("int(11)");
            entity.Property(e => e.Component).HasColumnName("component").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.LossMin).HasColumnName("loss_min").HasColumnType("double");
            entity.Property(e => e.LossMostLikely).HasColumnName("loss_most_likely").HasColumnType("double");
            entity.Property(e => e.LossMax).HasColumnName("loss_max").HasColumnType("double");
            entity.Property(e => e.ExpectedLoss).HasColumnName("expected_loss").HasColumnType("double");
            entity.Property(e => e.Cvar95).HasColumnName("cvar95").HasColumnType("double");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.TailStatisticsId, e.Component },
                "uq_risk_tail_components_tail_statistics_id_component").IsUnique();

            entity.HasOne(e => e.TailStatistics)
                .WithMany(s => s.Components)
                .HasForeignKey(e => e.TailStatisticsId)
                .HasConstraintName("fk_risk_tail_components_tail_statistics_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RiskCorrelation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_correlations", t =>
                {
                    t.HasCheckConstraint("ck_risk_correlations_order", "`risk_a_id` < `risk_b_id`");
                    t.HasCheckConstraint("ck_risk_correlations_coefficient",
                        "`coefficient` >= 0 AND `coefficient` <= 1");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskAId).HasColumnName("risk_a_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskBId).HasColumnName("risk_b_id").HasColumnType("int(11)");
            entity.Property(e => e.Coefficient).HasColumnName("coefficient").HasColumnType("decimal(4,3)")
                .HasPrecision(4, 3);
            entity.Property(e => e.Rationale).HasColumnName("rationale").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.RiskAId, e.RiskBId }, "uq_risk_correlations_risk_a_id_risk_b_id")
                .IsUnique();
            entity.HasIndex(e => e.RiskBId, "idx_risk_correlations_risk_b_id");
            entity.HasIndex(e => e.UpdatedById, "idx_risk_correlations_updated_by_id");

            entity.HasOne(e => e.RiskA)
                .WithMany()
                .HasForeignKey(e => e.RiskAId)
                .HasConstraintName("fk_risk_correlations_risk_a_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RiskB)
                .WithMany()
                .HasForeignKey(e => e.RiskBId)
                .HasConstraintName("fk_risk_correlations_risk_b_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_risk_correlations_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskAppetiteTailLimit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_appetite_tail_limits", t =>
                {
                    t.HasCheckConstraint("ck_risk_appetite_tail_limits_any",
                        "`max_scenario_expected_loss` IS NOT NULL OR `max_scenario_p95` IS NOT NULL OR " +
                        "`max_scenario_cvar95` IS NOT NULL OR `max_portfolio_expected_loss` IS NOT NULL OR " +
                        "`max_portfolio_p95` IS NOT NULL OR `max_portfolio_cvar95` IS NOT NULL");
                    t.HasCheckConstraint("ck_risk_appetite_tail_limits_non_negative",
                        "(`max_scenario_expected_loss` IS NULL OR `max_scenario_expected_loss` >= 0) AND " +
                        "(`max_scenario_p95` IS NULL OR `max_scenario_p95` >= 0) AND " +
                        "(`max_scenario_cvar95` IS NULL OR `max_scenario_cvar95` >= 0) AND " +
                        "(`max_portfolio_expected_loss` IS NULL OR `max_portfolio_expected_loss` >= 0) AND " +
                        "(`max_portfolio_p95` IS NULL OR `max_portfolio_p95` >= 0) AND " +
                        "(`max_portfolio_cvar95` IS NULL OR `max_portfolio_cvar95` >= 0)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.AppetiteId).HasColumnName("appetite_id").HasColumnType("int(11)");
            entity.Property(e => e.MaxScenarioExpectedLoss).HasColumnName("max_scenario_expected_loss")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.MaxScenarioP95).HasColumnName("max_scenario_p95")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.MaxScenarioCvar95).HasColumnName("max_scenario_cvar95")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.MaxPortfolioExpectedLoss).HasColumnName("max_portfolio_expected_loss")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.MaxPortfolioP95).HasColumnName("max_portfolio_p95")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.MaxPortfolioCvar95).HasColumnName("max_portfolio_cvar95")
                .HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            entity.Property(e => e.Rationale).HasColumnName("rationale").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.AppetiteId, "uq_risk_appetite_tail_limits_appetite_id").IsUnique();
            entity.HasIndex(e => e.UpdatedById, "idx_risk_appetite_tail_limits_updated_by_id");

            entity.HasOne(e => e.Appetite)
                .WithMany()
                .HasForeignKey(e => e.AppetiteId)
                .HasConstraintName("fk_risk_appetite_tail_limits_appetite_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_risk_appetite_tail_limits_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
