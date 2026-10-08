using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.12 — AI governance (S53 §4): <c>ai_models</c> (the model inventory: purpose, kind, source and vendor, version,
/// status, risk tier, human oversight, owner, unit and how long an evaluation stays current), <c>ai_model_data_links</c>
/// (the data records a model uses — the <c>organizationData</c> nodes the Stage 9.11 catalogue is keyed by),
/// <c>ai_model_metric_readings</c> (the insert-only evaluation history: accuracy, precision, recall, calibration, drift and
/// the human override rate), <c>ai_model_overrides</c> (a person's decision contrary to the model, with author and reason)
/// and <c>ai_model_risks</c> (the register's risks that involve a model — what derives flag 11).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enums, <c>varchar(n)</c> and never
/// <c>char(n)</c>, <c>decimal(18,6)</c> for metric values. Every CHECK is declared on the model too, so the snapshot carries
/// the same DDL as <c>Structure/101.sql</c>, and no CHECK names a column a <c>SET NULL</c> foreign key may clear.
///
/// <b>A model is never deleted</b> (S53 D9): its readings, overrides and risk links reference it with <c>RESTRICT</c>, so
/// the evidence cannot go with it; the data links are a declaration and go with the model or the data node
/// (<c>CASCADE</c>). The vendor is a third party referenced with <c>RESTRICT</c> and counted by
/// <c>ThirdPartyReferences</c>. No inverse navigation on <see cref="Entity"/>, <see cref="User"/>, <see cref="Risk"/> or
/// <see cref="ThirdParty"/>, so no existing payload changes. The query filters are in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<AiModel> AiModels { get; set; } = null!;

    public virtual DbSet<AiModelDataLink> AiModelDataLinks { get; set; } = null!;

    public virtual DbSet<AiModelMetricReading> AiModelMetricReadings { get; set; } = null!;

    public virtual DbSet<AiModelOverride> AiModelOverrides { get; set; } = null!;

    public virtual DbSet<AiModelRisk> AiModelRisks { get; set; } = null!;

    private static void ConfigureAiGovernance(ModelBuilder modelBuilder)
    {
        ConfigureAiModels(modelBuilder);
        ConfigureAiModelEvidence(modelBuilder);
    }

    private static void ConfigureAiModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiModel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("ai_models", t =>
                {
                    t.HasCheckConstraint("ck_ai_models_kind", "`kind` >= 1 AND `kind` <= 7");
                    t.HasCheckConstraint("ck_ai_models_source", "`source` >= 1 AND `source` <= 3");
                    t.HasCheckConstraint("ck_ai_models_status", "`status` >= 1 AND `status` <= 4");
                    t.HasCheckConstraint("ck_ai_models_risk_tier",
                        "`risk_tier` IS NULL OR (`risk_tier` >= 1 AND `risk_tier` <= 3)");
                    t.HasCheckConstraint("ck_ai_models_human_oversight",
                        "`human_oversight` IS NULL OR (`human_oversight` >= 1 AND `human_oversight` <= 3)");
                    t.HasCheckConstraint("ck_ai_models_max_evaluation_age_days",
                        "`max_evaluation_age_days` >= 1 AND `max_evaluation_age_days` <= 1096");
                    t.HasCheckConstraint("ck_ai_models_retired",
                        "(`status` = 4 AND `retired_at` IS NOT NULL AND `retire_reason` IS NOT NULL) OR (`status` <> 4 AND `retired_at` IS NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Purpose).HasColumnName("purpose").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Source).HasColumnName("source").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ThirdPartyId).HasColumnName("third_party_id").HasColumnType("int(11)");
            entity.Property(e => e.Version).HasColumnName("version").HasColumnType("varchar(100)").HasMaxLength(100);
            entity.Property(e => e.VersionSince).HasColumnName("version_since").HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.RiskTier).HasColumnName("risk_tier").HasColumnType("int(11)").HasConversion<int?>();
            entity.Property(e => e.HumanOversight).HasColumnName("human_oversight").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.OwnerId).HasColumnName("owner_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.MaxEvaluationAgeDays).HasColumnName("max_evaluation_age_days").HasColumnType("int(11)");
            entity.Property(e => e.DataDeclaredAt).HasColumnName("data_declared_at").HasColumnType("datetime");
            entity.Property(e => e.Notes).HasColumnName("notes").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.RetiredAt).HasColumnName("retired_at").HasColumnType("datetime");
            entity.Property(e => e.RetiredById).HasColumnName("retired_by_id").HasColumnType("int(11)");
            entity.Property(e => e.RetireReason).HasColumnName("retire_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            // One row per model: two rows for one model would split its evaluation and its risks (S53 §4.1). The collation
            // is case-insensitive, so "Triage GPT" and "triage gpt" collide.
            entity.HasIndex(e => e.Name, "uq_ai_models_name").IsUnique();
            entity.HasIndex(e => e.ThirdPartyId, "idx_ai_models_third_party_id");
            entity.HasIndex(e => e.EntityId, "idx_ai_models_entity_id");
            entity.HasIndex(e => e.OwnerId, "idx_ai_models_owner_id");
            entity.HasIndex(e => e.Status, "idx_ai_models_status");
            entity.HasIndex(e => e.RetiredById, "idx_ai_models_retired_by_id");
            entity.HasIndex(e => e.CreatedById, "idx_ai_models_created_by_id");
            entity.HasIndex(e => e.UpdatedById, "idx_ai_models_updated_by_id");

            // RESTRICT: a third party named as a model's vendor is in use (S53 §4.1, ThirdPartyReferences).
            entity.HasOne(e => e.ThirdParty)
                .WithMany()
                .HasForeignKey(e => e.ThirdPartyId)
                .HasConstraintName("fk_ai_models_third_party_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_ai_models_entity_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .HasConstraintName("fk_ai_models_owner_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RetiredBy)
                .WithMany()
                .HasForeignKey(e => e.RetiredById)
                .HasConstraintName("fk_ai_models_retired_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_ai_models_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_ai_models_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AiModelDataLink>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("ai_model_data_links", t => t.HasCheckConstraint("ck_ai_model_data_links_data_usage",
                    "`data_usage` >= 1 AND `data_usage` <= 5"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ModelId).HasColumnName("model_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Usage).HasColumnName("data_usage").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.ModelId, e.EntityId, e.Usage }, "uq_ai_model_data_links_model_id_entity_id_data_usage")
                .IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_ai_model_data_links_entity_id");
            entity.HasIndex(e => e.CreatedById, "idx_ai_model_data_links_created_by_id");

            entity.HasOne(e => e.Model)
                .WithMany(m => m.DataLinks)
                .HasForeignKey(e => e.ModelId)
                .HasConstraintName("fk_ai_model_data_links_model_id")
                .OnDelete(DeleteBehavior.Cascade);

            // CASCADE: deleting the data record removes what pointed at it, as a third-party or RIPD link does.
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_ai_model_data_links_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_ai_model_data_links_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureAiModelEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiModelMetricReading>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("ai_model_metric_readings", t =>
                {
                    t.HasCheckConstraint("ck_ai_model_metric_readings_metric", "`metric` >= 1 AND `metric` <= 6");
                    // A fraction for every metric but drift (5), a non-negative statistic.
                    t.HasCheckConstraint("ck_ai_model_metric_readings_value",
                        "`value` >= 0 AND (`metric` = 5 OR `value` <= 1)");
                    t.HasCheckConstraint("ck_ai_model_metric_readings_period",
                        "(`period_start` IS NULL AND `period_end` IS NULL) OR (`period_start` IS NOT NULL AND `period_end` IS NOT NULL AND `period_end` > `period_start`)");
                    t.HasCheckConstraint("ck_ai_model_metric_readings_sample_size",
                        "`sample_size` IS NULL OR `sample_size` >= 1");
                    // The override rate is computed from the overrides of a period over the outputs reviewed in it (S53 D8).
                    t.HasCheckConstraint("ck_ai_model_metric_readings_override_rate",
                        "(`metric` = 6 AND `period_start` IS NOT NULL AND `sample_size` IS NOT NULL AND `override_count` IS NOT NULL AND `override_count` >= 0 AND `override_count` <= `sample_size`) OR (`metric` <> 6 AND `override_count` IS NULL)");
                    t.HasCheckConstraint("ck_ai_model_metric_readings_void",
                        "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ModelId).HasColumnName("model_id").HasColumnType("int(11)");
            entity.Property(e => e.Metric).HasColumnName("metric").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("decimal(18,6)").HasPrecision(18, 6);
            entity.Property(e => e.ModelVersion).HasColumnName("model_version").HasColumnType("varchar(100)")
                .HasMaxLength(100);
            entity.Property(e => e.MeasuredAt).HasColumnName("measured_at").HasColumnType("datetime");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start").HasColumnType("datetime");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end").HasColumnType("datetime");
            entity.Property(e => e.SampleSize).HasColumnName("sample_size").HasColumnType("int(11)");
            entity.Property(e => e.OverrideCount).HasColumnName("override_count").HasColumnType("int(11)");
            entity.Property(e => e.Method).HasColumnName("method").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.EvidenceReference).HasColumnName("evidence_reference").HasColumnType("varchar(500)")
                .HasMaxLength(500);
            entity.Property(e => e.RecordedById).HasColumnName("recorded_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedAt).HasColumnName("voided_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedById).HasColumnName("voided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);

            entity.HasIndex(e => new { e.ModelId, e.Metric, e.MeasuredAt },
                "idx_ai_model_metric_readings_model_id_metric_measured_at");
            entity.HasIndex(e => e.RecordedById, "idx_ai_model_metric_readings_recorded_by_id");
            entity.HasIndex(e => e.VoidedById, "idx_ai_model_metric_readings_voided_by_id");

            // RESTRICT: the evaluation history is evidence and does not go with anything (S53 D9).
            entity.HasOne(e => e.Model)
                .WithMany()
                .HasForeignKey(e => e.ModelId)
                .HasConstraintName("fk_ai_model_metric_readings_model_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RecordedBy)
                .WithMany()
                .HasForeignKey(e => e.RecordedById)
                .HasConstraintName("fk_ai_model_metric_readings_recorded_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.VoidedBy)
                .WithMany()
                .HasForeignKey(e => e.VoidedById)
                .HasConstraintName("fk_ai_model_metric_readings_voided_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AiModelOverride>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("ai_model_overrides", t => t.HasCheckConstraint("ck_ai_model_overrides_void",
                    "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ModelId).HasColumnName("model_id").HasColumnType("int(11)");
            entity.Property(e => e.ModelVersion).HasColumnName("model_version").HasColumnType("varchar(100)")
                .HasMaxLength(100);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime");
            entity.Property(e => e.ModelOutput).HasColumnName("model_output").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.HumanDecision).HasColumnName("human_decision").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);
            entity.Property(e => e.Reason).HasColumnName("reason").HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.RecordedById).HasColumnName("recorded_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedAt).HasColumnName("voided_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedById).HasColumnName("voided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);

            entity.HasIndex(e => new { e.ModelId, e.OccurredAt }, "idx_ai_model_overrides_model_id_occurred_at");
            entity.HasIndex(e => e.RecordedById, "idx_ai_model_overrides_recorded_by_id");
            entity.HasIndex(e => e.VoidedById, "idx_ai_model_overrides_voided_by_id");

            entity.HasOne(e => e.Model)
                .WithMany()
                .HasForeignKey(e => e.ModelId)
                .HasConstraintName("fk_ai_model_overrides_model_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RecordedBy)
                .WithMany()
                .HasForeignKey(e => e.RecordedById)
                .HasConstraintName("fk_ai_model_overrides_recorded_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.VoidedBy)
                .WithMany()
                .HasForeignKey(e => e.VoidedById)
                .HasConstraintName("fk_ai_model_overrides_voided_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AiModelRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("ai_model_risks")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ModelId).HasColumnName("model_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.Note).HasColumnName("note").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.ModelId, e.RiskId }, "uq_ai_model_risks_model_id_risk_id").IsUnique();
            entity.HasIndex(e => e.RiskId, "idx_ai_model_risks_risk_id");
            entity.HasIndex(e => e.CreatedById, "idx_ai_model_risks_created_by_id");

            entity.HasOne(e => e.Model)
                .WithMany()
                .HasForeignKey(e => e.ModelId)
                .HasConstraintName("fk_ai_model_risks_model_id")
                .OnDelete(DeleteBehavior.Restrict);

            // CASCADE: a deleted risk takes its links, as every child of a risk does; the trail stays.
            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_ai_model_risks_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_ai_model_risks_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
