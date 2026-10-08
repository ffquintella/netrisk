using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Stage 9.8 — KRIs, mandatory reassessment triggers and the methodology's metrics (S49 §4): <c>kris</c> (the
/// indicator, its source and its Phase 0 tolerance), <c>kri_readings</c> (its insert-only history), <c>kri_risks</c>
/// (the risks it governs), <c>reassessment_events</c> (the six Phase 7 triggers, declared or detected) and
/// <c>risk_reassessment_triggers</c> (an event applied to one risk, once).
///
/// Named per the Track 6 convention: snake_case columns via <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c>/
/// <c>ck_</c> prefixes, UTC <c>datetime</c>, <c>int</c> + <c>HasConversion</c> for the enums, <c>varchar(n)</c> and
/// never <c>char(n)</c>, <c>decimal(18,4)</c> for indicator values (S49 D15). Every CHECK is declared on the model too,
/// so the snapshot carries the same DDL as <c>Structure/97.sql</c>. No inverse navigation on <see cref="Risk"/>,
/// <see cref="Incident"/> or <see cref="User"/>, so the payloads of <c>GET /Risks</c> and <c>GET /Incidents</c> do not
/// change. The query filters are in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<Kri> Kris { get; set; } = null!;

    public virtual DbSet<KriReading> KriReadings { get; set; } = null!;

    public virtual DbSet<KriRisk> KriRisks { get; set; } = null!;

    public virtual DbSet<ReassessmentEvent> ReassessmentEvents { get; set; } = null!;

    public virtual DbSet<RiskReassessmentTrigger> RiskReassessmentTriggers { get; set; } = null!;

    private static void ConfigureMonitoring(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Kri>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("kris", t =>
                {
                    t.HasCheckConstraint("ck_kris_category", "`category` >= 1 AND `category` <= 4");
                    t.HasCheckConstraint("ck_kris_direction", "`direction` >= 1 AND `direction` <= 2");
                    t.HasCheckConstraint("ck_kris_max_reading_age_days",
                        "`max_reading_age_days` >= 1 AND `max_reading_age_days` <= 366");
                    t.HasCheckConstraint("ck_kris_warning_side",
                        "`warning_threshold` IS NULL OR (`direction` = 1 AND `warning_threshold` < `tolerance_threshold`) OR (`direction` = 2 AND `warning_threshold` > `tolerance_threshold`)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.Category).HasColumnName("category").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Source).HasColumnName("source").HasColumnType("varchar(500)").HasMaxLength(500);
            entity.Property(e => e.Unit).HasColumnName("unit").HasColumnType("varchar(50)").HasMaxLength(50);
            entity.Property(e => e.Direction).HasColumnName("direction").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ToleranceThreshold).HasColumnName("tolerance_threshold")
                .HasColumnType("decimal(18,4)").HasPrecision(18, 4);
            entity.Property(e => e.WarningThreshold).HasColumnName("warning_threshold")
                .HasColumnType("decimal(18,4)").HasPrecision(18, 4);
            entity.Property(e => e.ToleranceRationale).HasColumnName("tolerance_rationale")
                .HasColumnType("varchar(2000)").HasMaxLength(2000);
            entity.Property(e => e.MaxReadingAgeDays).HasColumnName("max_reading_age_days").HasColumnType("int(11)");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.RetiredAt).HasColumnName("retired_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.OwnerId, "idx_kris_owner_id");
            entity.HasIndex(e => e.EntityId, "idx_kris_entity_id");
            entity.HasIndex(e => e.UpdatedById, "idx_kris_updated_by_id");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .HasConstraintName("fk_kris_owner_id")
                .OnDelete(DeleteBehavior.SetNull);

            // SET NULL rather than CASCADE: deleting a unit must not delete the evidence a gate decided on. The KRI
            // becomes organization-wide, which only an unrestricted caller can then edit.
            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_kris_entity_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_kris_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<KriReading>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("kri_readings", t => t.HasCheckConstraint("ck_kri_readings_void",
                    "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.KriId).HasColumnName("kri_id").HasColumnType("int(11)");
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("decimal(18,4)").HasPrecision(18, 4);
            entity.Property(e => e.ObservedAt).HasColumnName("observed_at").HasColumnType("datetime");
            entity.Property(e => e.Note).HasColumnName("note").HasColumnType("varchar(1000)").HasMaxLength(1000);
            entity.Property(e => e.RecordedById).HasColumnName("recorded_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedAt).HasColumnName("voided_at").HasColumnType("datetime");
            entity.Property(e => e.VoidedById).HasColumnName("voided_by_id").HasColumnType("int(11)");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason").HasColumnType("varchar(1000)")
                .HasMaxLength(1000);

            entity.HasIndex(e => new { e.KriId, e.ObservedAt }, "idx_kri_readings_kri_id_observed_at");
            entity.HasIndex(e => e.RecordedById, "idx_kri_readings_recorded_by_id");
            entity.HasIndex(e => e.VoidedById, "idx_kri_readings_voided_by_id");

            entity.HasOne(e => e.Kri)
                .WithMany()
                .HasForeignKey(e => e.KriId)
                .HasConstraintName("fk_kri_readings_kri_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RecordedBy)
                .WithMany()
                .HasForeignKey(e => e.RecordedById)
                .HasConstraintName("fk_kri_readings_recorded_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.VoidedBy)
                .WithMany()
                .HasForeignKey(e => e.VoidedById)
                .HasConstraintName("fk_kri_readings_voided_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<KriRisk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("kri_risks")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.KriId).HasColumnName("kri_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.KriId, e.RiskId }, "uq_kri_risks_kri_id_risk_id").IsUnique();
            entity.HasIndex(e => e.RiskId, "idx_kri_risks_risk_id");
            entity.HasIndex(e => e.CreatedById, "idx_kri_risks_created_by_id");

            entity.HasOne(e => e.Kri)
                .WithMany()
                .HasForeignKey(e => e.KriId)
                .HasConstraintName("fk_kri_risks_kri_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_kri_risks_risk_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_kri_risks_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReassessmentEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("reassessment_events", t =>
                {
                    t.HasCheckConstraint("ck_reassessment_events_trigger_type",
                        "`trigger_type` >= 1 AND `trigger_type` <= 6");
                    t.HasCheckConstraint("ck_reassessment_events_origin", "`origin` >= 1 AND `origin` <= 2");
                    t.HasCheckConstraint("ck_reassessment_events_kri_origin",
                        "(`origin` = 2 AND `kri_id` IS NOT NULL AND `kri_reading_id` IS NOT NULL AND `trigger_type` = 6) OR (`origin` = 1 AND `kri_id` IS NULL AND `kri_reading_id` IS NULL AND `kri_breach_ended_at` IS NULL)");
                    t.HasCheckConstraint("ck_reassessment_events_incident_type",
                        "`incident_id` IS NULL OR `trigger_type` = 3");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.TriggerType).HasColumnName("trigger_type").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Origin).HasColumnName("origin").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Title).HasColumnName("title").HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("varchar(2000)")
                .HasMaxLength(2000);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id").HasColumnType("int(11)");
            entity.Property(e => e.KriId).HasColumnName("kri_id").HasColumnType("int(11)");
            entity.Property(e => e.KriReadingId).HasColumnName("kri_reading_id").HasColumnType("int(11)");
            entity.Property(e => e.KriBreachEndedAt).HasColumnName("kri_breach_ended_at").HasColumnType("datetime");
            entity.Property(e => e.DeclaredById).HasColumnName("declared_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // Several NULLs are allowed in a unique index, so each holds only for the rows that set it: one event per
            // incident, one per opening reading (S49 D8).
            entity.HasIndex(e => e.IncidentId, "uq_reassessment_events_incident_id").IsUnique();
            entity.HasIndex(e => e.KriReadingId, "uq_reassessment_events_kri_reading_id").IsUnique();
            entity.HasIndex(e => e.KriId, "idx_reassessment_events_kri_id");
            entity.HasIndex(e => e.DeclaredById, "idx_reassessment_events_declared_by_id");

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .HasConstraintName("fk_reassessment_events_incident_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Kri)
                .WithMany()
                .HasForeignKey(e => e.KriId)
                .HasConstraintName("fk_reassessment_events_kri_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.KriReading)
                .WithMany()
                .HasForeignKey(e => e.KriReadingId)
                .HasConstraintName("fk_reassessment_events_kri_reading_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DeclaredBy)
                .WithMany()
                .HasForeignKey(e => e.DeclaredById)
                .HasConstraintName("fk_reassessment_events_declared_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskReassessmentTrigger>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_reassessment_triggers")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EventId).HasColumnName("event_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");
            entity.Property(e => e.RaisedAt).HasColumnName("raised_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.EventId, e.RiskId }, "uq_risk_reassessment_triggers_event_id_risk_id")
                .IsUnique();
            entity.HasIndex(e => e.RiskId, "idx_risk_reassessment_triggers_risk_id");

            entity.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .HasConstraintName("fk_risk_reassessment_triggers_event_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_risk_reassessment_triggers_risk_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
