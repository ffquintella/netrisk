using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.5 — the eleven mandatory flags and Gate A (S46 §4): <c>risk_flags</c>, one row per risk and
/// flag with its declared and derived halves, and <c>risk_decisions</c>, the insert-only Phase 4 decision
/// log.
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/
/// <c>uq_</c>/<c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for enums,
/// <c>tinyint(1)</c> for booleans, <c>varchar(n)</c> or <c>text</c> and never <c>char(n)</c>. Every CHECK is
/// declared on the model too, so the snapshot carries the same DDL as <c>Structure/94.sql</c>. No inverse
/// navigation is added to <see cref="Risk"/>, so the payload of <c>GET /Risks</c> does not change. Both
/// tables are scoped through the risk, in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RiskFlag> RiskFlags { get; set; } = null!;

    public virtual DbSet<RiskDecision> RiskDecisions { get; set; } = null!;

    private static void ConfigureRiskFlags(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskFlag>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_flags", t => t.HasCheckConstraint("ck_risk_flags_flag",
                    "`flag` >= 1 AND `flag` <= 12"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Flag).HasColumnName("flag").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Declared).HasColumnName("declared").HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.DeclaredReason).HasColumnName("declared_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.DeclaredAt).HasColumnName("declared_at").HasColumnType("datetime");
            entity.Property(e => e.DeclaredById).HasColumnName("declared_by_id").HasColumnType("int(11)");
            entity.Property(e => e.Derived).HasColumnName("derived").HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.DerivedBasis).HasColumnName("derived_basis").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.DerivedWeight).HasColumnName("derived_weight").HasColumnType("decimal(4,2)")
                .HasPrecision(4, 2);
            entity.Property(e => e.DerivedChangedAt).HasColumnName("derived_changed_at").HasColumnType("datetime");
            entity.Property(e => e.DerivedNote).HasColumnName("derived_note").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.Ignore(e => e.IsSet);

            entity.HasIndex(e => new { e.RiskId, e.Flag }, "uq_risk_flags_risk_id_flag").IsUnique();
            entity.HasIndex(e => e.Flag, "idx_risk_flags_flag");
            entity.HasIndex(e => e.DeclaredById, "idx_risk_flags_declared_by_id");

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_flags_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DeclaredBy)
                .WithMany()
                .HasForeignKey(e => e.DeclaredById)
                .HasConstraintName("fk_risk_flags_declared_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskDecision>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_decisions", t =>
                {
                    t.HasCheckConstraint("ck_risk_decisions_decision", "`decision` >= 1 AND `decision` <= 4");
                    t.HasCheckConstraint("ck_risk_decisions_source", "`source` >= 1 AND `source` <= 2");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Decision).HasColumnName("decision").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Source).HasColumnName("source").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Reason).HasColumnName("reason").HasColumnType("text");
            entity.Property(e => e.GateAConditions).HasColumnName("gate_a_conditions").HasColumnType("varchar(64)")
                .HasMaxLength(64);
            entity.Property(e => e.DecidedAt).HasColumnName("decided_at").HasColumnType("datetime");
            entity.Property(e => e.DecidedById).HasColumnName("decided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.EscalatedAt).HasColumnName("escalated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.RiskId, e.DecidedAt }, "idx_risk_decisions_risk_id_decided_at");
            entity.HasIndex(e => e.DecidedById, "idx_risk_decisions_decided_by_id");

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_decisions_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DecidedBy)
                .WithMany()
                .HasForeignKey(e => e.DecidedById)
                .HasConstraintName("fk_risk_decisions_decided_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
