using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M53, T243 and T247 — the Consolidated Risk Index (S39 §12.5; S40 §7.1–§7.2): versioned profiles, node snapshots and
/// object rows, and what the snapshot job writes beside them (attributions, node members, class days, frozen closures).
/// Track 6 naming; every CHECK is declared here so the snapshot carries the Structure script's DDL, and none names a
/// column a <c>SET NULL</c> foreign key may clear. The history tables have no entity or user foreign key (they outlive
/// both); their scope filters are in <c>NRDbContext.EntityScope.cs</c>.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<RiskIndexProfile> RiskIndexProfiles { get; set; } = null!;

    public virtual DbSet<RiskIndexSnapshot> RiskIndexSnapshots { get; set; } = null!;

    public virtual DbSet<RiskIndexObjectDay> RiskIndexObjectDays { get; set; } = null!;

    public virtual DbSet<RiskIndexObjectAttribution> RiskIndexObjectAttributions { get; set; } = null!;

    public virtual DbSet<RiskIndexNodeMember> RiskIndexNodeMembers { get; set; } = null!;

    public virtual DbSet<RiskIndexClassDay> RiskIndexClassDays { get; set; } = null!;

    public virtual DbSet<RiskIndexIncidentClosure> RiskIndexIncidentClosures { get; set; } = null!;

    public virtual DbSet<RiskIndexBridge> RiskIndexBridges { get; set; } = null!;

    public virtual DbSet<RiskIndexPreviewRequest> RiskIndexPreviewRequests { get; set; } = null!;

    public virtual DbSet<RiskIndexProfileEditor> RiskIndexProfileEditors { get; set; } = null!;

    private static void ConfigureRiskIndex(ModelBuilder modelBuilder)
    {
        ConfigureRiskIndexProfiles(modelBuilder);
        ConfigureRiskIndexSnapshots(modelBuilder);
        ConfigureRiskIndexObjectDays(modelBuilder);

        // M53, T247 (S40 §7.2, §9.3) — what the snapshot job writes beside the snapshots and object rows.
        ConfigureRiskIndexObjectAttributions(modelBuilder);
        ConfigureRiskIndexNodeMembers(modelBuilder);
        ConfigureRiskIndexClassDays(modelBuilder);
        ConfigureRiskIndexIncidentClosures(modelBuilder);

        // M53, T250 (S40 §7.2, §9.3 step 4, §9.6) — the bridge of a profile change and the draft's sensitivity previews.
        ConfigureRiskIndexBridges(modelBuilder);
        ConfigureRiskIndexPreviewRequests(modelBuilder);

        // M53 review (S40 §6.4, §7.3, §11.4) — who wrote each profile, and the committee's minutes attached to it.
        ConfigureRiskIndexProfileEditors(modelBuilder);
        ConfigureRiskIndexProfileFiles(modelBuilder);
    }

    private static void ConfigureRiskIndexProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            // The lifecycle the services enforce, held by the database too (S39 §13.1): only a draft lacks the submission;
            // an active or retired profile was approved with the committee's reference and has its start; a retired one
            // has its end; a rejected one its reason.
            entity.ToTable("risk_index_profiles", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_profiles_version", "`version` >= 1");
                    t.HasCheckConstraint("ck_risk_index_profiles_mode", "`mode` >= 1 AND `mode` <= 2");
                    t.HasCheckConstraint("ck_risk_index_profiles_status", "`status` >= 1 AND `status` <= 5");
                    t.HasCheckConstraint("ck_risk_index_profiles_submitted",
                        "`status` = 1 OR (`submitted_at` IS NOT NULL AND `justification` IS NOT NULL)");
                    t.HasCheckConstraint("ck_risk_index_profiles_approved",
                        "`status` NOT IN (3, 4) OR (`approved_at` IS NOT NULL AND `effective_from` IS NOT NULL AND `committee_decision_ref` IS NOT NULL)");
                    t.HasCheckConstraint("ck_risk_index_profiles_retired",
                        "(`status` = 4 AND `retired_at` IS NOT NULL) OR (`status` <> 4 AND `retired_at` IS NULL)");
                    t.HasCheckConstraint("ck_risk_index_profiles_rejected",
                        "`status` <> 5 OR `rejected_reason` IS NOT NULL");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Version).HasColumnName("version").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("varchar(120)").HasMaxLength(120);
            entity.Property(e => e.Preset).HasColumnName("preset").HasColumnType("varchar(40)").HasMaxLength(40);
            entity.Property(e => e.Mode).HasColumnName("mode").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Parameters).HasColumnName("parameters").HasColumnType("longtext");
            entity.Property(e => e.Justification).HasColumnName("justification").HasColumnType("text");
            entity.Property(e => e.SensitivityReport).HasColumnName("sensitivity_report").HasColumnType("longtext");
            entity.Property(e => e.CommitteeDecisionRef).HasColumnName("committee_decision_ref")
                .HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.BoardApprovalRef).HasColumnName("board_approval_ref")
                .HasColumnType("varchar(200)").HasMaxLength(200);
            entity.Property(e => e.IsRebaseline).HasColumnName("is_rebaseline").HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.RejectedReason).HasColumnName("rejected_reason").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at").HasColumnType("datetime");
            entity.Property(e => e.SubmittedById).HasColumnName("submitted_by_id").HasColumnType("int(11)");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at").HasColumnType("datetime");
            entity.Property(e => e.ApprovedById).HasColumnName("approved_by_id").HasColumnType("int(11)");
            entity.Property(e => e.ApprovalGrantSource).HasColumnName("approval_grant_source")
                .HasColumnType("varchar(32)").HasMaxLength(32);
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from").HasColumnType("datetime");
            entity.Property(e => e.RetiredAt).HasColumnName("retired_at").HasColumnType("datetime");
            entity.Property(e => e.RetiredById).HasColumnName("retired_by_id").HasColumnType("int(11)");
            entity.Property(e => e.RetireJustification).HasColumnName("retire_justification").HasColumnType("text");
            entity.Property(e => e.RetireDecisionRef).HasColumnName("retire_decision_ref")
                .HasColumnType("varchar(200)").HasMaxLength(200);

            // An exact If-Match: updated_at has second precision (S40 §6.2.1).
            entity.Property(e => e.Revision).HasColumnName("revision").HasColumnType("int(11)")
                .HasDefaultValue(0).IsConcurrencyToken();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.Version, "uq_risk_index_profiles_version").IsUnique();
            entity.HasIndex(e => e.Status, "idx_risk_index_profiles_status");
            entity.HasIndex(e => e.CreatedById, "idx_risk_index_profiles_created_by_id");
            entity.HasIndex(e => e.ApprovedById, "idx_risk_index_profiles_approved_by_id");
            entity.HasIndex(e => e.RetiredById, "idx_risk_index_profiles_retired_by_id");
            entity.HasIndex(e => e.SubmittedById, "idx_risk_index_profiles_submitted_by_id");

            // SET NULL, as every user reference of the governance tables: removing a user must not take the evidence.
            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_risk_index_profiles_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ApprovedBy)
                .WithMany()
                .HasForeignKey(e => e.ApprovedById)
                .HasConstraintName("fk_risk_index_profiles_approved_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RetiredBy)
                .WithMany()
                .HasForeignKey(e => e.RetiredById)
                .HasConstraintName("fk_risk_index_profiles_retired_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SubmittedBy)
                .WithMany()
                .HasForeignKey(e => e.SubmittedById)
                .HasConstraintName("fk_risk_index_profiles_submitted_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureRiskIndexSnapshots(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_index_snapshots", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_snapshots_mode", "`mode` >= 1 AND `mode` <= 2");
                    t.HasCheckConstraint("ck_risk_index_snapshots_scope_kind", "`scope_kind` >= 1 AND `scope_kind` <= 4");
                    t.HasCheckConstraint("ck_risk_index_snapshots_category", "`category` >= 0 AND `category` <= 6");
                    t.HasCheckConstraint("ck_risk_index_snapshots_value_displayed",
                        "`value_displayed` >= 0 AND `value_displayed` <= 100");
                    t.HasCheckConstraint("ck_risk_index_snapshots_fractions",
                        "`coverage` >= 0 AND `coverage` <= 1 AND `freshness` >= 0 AND `freshness` <= 1 AND `quality` >= 0 AND `quality` <= 1");
                    t.HasCheckConstraint("ck_risk_index_snapshots_interval",
                        "(`interval_low` IS NULL AND `interval_high` IS NULL) OR (`interval_low` IS NOT NULL AND `interval_high` IS NOT NULL AND `interval_low` <= `interval_high`)");

                    // T247: the display state belongs to the headline row, the category state to a category row.
                    t.HasCheckConstraint("ck_risk_index_snapshots_display_state",
                        "`display_state` IS NULL OR (`category` = 0 AND `display_state` >= 1 AND `display_state` <= 4)");
                    t.HasCheckConstraint("ck_risk_index_snapshots_category_state",
                        "`category_state` IS NULL OR (`category` <> 0 AND `category_state` >= 1 AND `category_state` <= 4)");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.SnapshotDate).HasColumnName("snapshot_date").HasColumnType("date");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.Mode).HasColumnName("mode").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ScopeKind).HasColumnName("scope_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ScopeRef).HasColumnName("scope_ref").HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Category).HasColumnName("category").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ValueUnfloored).HasColumnName("value_unfloored").HasColumnType("double");
            entity.Property(e => e.ValueFloored).HasColumnName("value_floored").HasColumnType("double");
            entity.Property(e => e.ValueDisplayed).HasColumnName("value_displayed").HasColumnType("int(11)");
            entity.Property(e => e.FloorReasons).HasColumnName("floor_reasons").HasColumnType("text");
            entity.Property(e => e.Coverage).HasColumnName("coverage").HasColumnType("decimal(4,3)").HasPrecision(4, 3);
            entity.Property(e => e.Freshness).HasColumnName("freshness").HasColumnType("decimal(4,3)").HasPrecision(4, 3);
            entity.Property(e => e.Quality).HasColumnName("quality").HasColumnType("decimal(4,3)").HasPrecision(4, 3);
            entity.Property(e => e.IntervalLow).HasColumnName("interval_low").HasColumnType("double");
            entity.Property(e => e.IntervalHigh).HasColumnName("interval_high").HasColumnType("double");
            entity.Property(e => e.ObjectCount).HasColumnName("object_count").HasColumnType("int(11)");
            entity.Property(e => e.PresumptionPoints).HasColumnName("presumption_points").HasColumnType("double");
            entity.Property(e => e.ExternalInputsHash).HasColumnName("external_inputs_hash")
                .HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.TopContributions).HasColumnName("top_contributions").HasColumnType("longtext");
            entity.Property(e => e.ChangeAttribution).HasColumnName("change_attribution").HasColumnType("longtext");
            entity.Property(e => e.DisplayState).HasColumnName("display_state").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.CategoryState).HasColumnName("category_state").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.Markers).HasColumnName("markers").HasColumnType("text");
            entity.Property(e => e.Chips).HasColumnName("chips").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // One row per (day, profile, node, category): the job never rewrites a day (S40 §9.3 step 2).
            entity.HasIndex(e => new { e.SnapshotDate, e.ProfileId, e.ScopeKind, e.ScopeRef, e.Category },
                "uq_risk_index_snapshots_date_profile_scope_category").IsUnique();
            entity.HasIndex(e => new { e.ScopeKind, e.ScopeRef, e.Category, e.SnapshotDate },
                "idx_risk_index_snapshots_scope_category_date");
            entity.HasIndex(e => e.EntityId, "idx_risk_index_snapshots_entity_id");
            entity.HasIndex(e => e.ProfileId, "idx_risk_index_snapshots_profile_id");

            // RESTRICT: the profile that computed a snapshot is the evidence of what its number meant.
            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_snapshots_profile_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRiskIndexObjectDays(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexObjectDay>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            // Closed domains only: the categories an object is scored in and the 1–5 criticality scale. Object kinds,
            // states, populations and asset classes grow by appending (S40 §8.3), so they carry no range CHECK.
            entity.ToTable("risk_index_object_days", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_object_days_category", "`category` >= 1 AND `category` <= 6");
                    t.HasCheckConstraint("ck_risk_index_object_days_criticality",
                        "`criticality` >= 1 AND `criticality` <= 5");
                    t.HasCheckConstraint("ck_risk_index_object_days_r_value", "`r_value` >= 0 AND `r_value` <= 1");
                    t.HasCheckConstraint("ck_risk_index_object_days_weight", "`weight` >= 0");

                    // T247: the engine inputs beside R and the weight (S39 §8.4–§8.6, §4.8).
                    t.HasCheckConstraint("ck_risk_index_object_days_engine",
                        "`freshness` >= 0 AND `freshness` <= 1 AND `coverage_effect` >= 0 AND `coverage_effect` <= 1 AND `r_max` >= 0 AND `cleared_r` >= 0 AND `cleared_r` <= 1");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.SnapshotDate).HasColumnName("snapshot_date").HasColumnType("date");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectKind).HasColumnName("object_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ObjectRef).HasColumnName("object_ref").HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.Category).HasColumnName("category").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Population).HasColumnName("population").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.RValue).HasColumnName("r_value").HasColumnType("double");
            entity.Property(e => e.Weight).HasColumnName("weight").HasColumnType("decimal(5,4)").HasPrecision(5, 4);
            entity.Property(e => e.Criticality).HasColumnName("criticality").HasColumnType("tinyint(4)");
            entity.Property(e => e.CriticalitySource).HasColumnName("criticality_source")
                .HasColumnType("varchar(32)").HasMaxLength(32);
            entity.Property(e => e.CriticalityAuthorId).HasColumnName("criticality_author_id").HasColumnType("int(11)");
            entity.Property(e => e.State).HasColumnName("state").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Attribution).HasColumnName("attribution").HasColumnType("text");
            entity.Property(e => e.Factors).HasColumnName("factors").HasColumnType("text");
            entity.Property(e => e.ScenarioInputs).HasColumnName("scenario_inputs").HasColumnType("text");
            entity.Property(e => e.Signals).HasColumnName("signals").HasColumnType("text");
            entity.Property(e => e.AssetClass).HasColumnName("asset_class").HasColumnType("int(11)").HasConversion<int?>();
            entity.Property(e => e.SourceMask).HasColumnName("source_mask").HasColumnType("int(11)").HasDefaultValue(0);
            entity.Property(e => e.Environment).HasColumnName("environment").HasColumnType("varchar(32)").HasMaxLength(32);
            entity.Property(e => e.Freshness).HasColumnName("freshness").HasColumnType("double").HasDefaultValue(1.0);
            entity.Property(e => e.CoverageEffect).HasColumnName("coverage_effect").HasColumnType("double")
                .HasDefaultValue(1.0);
            entity.Property(e => e.RMax).HasColumnName("r_max").HasColumnType("double").HasDefaultValue(1.0);
            entity.Property(e => e.ClearedR).HasColumnName("cleared_r").HasColumnType("double").HasDefaultValue(0.0);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.SnapshotDate, e.ProfileId, e.ObjectKind, e.ObjectRef, e.Category },
                "uq_risk_index_object_days_date_profile_object_category").IsUnique();
            entity.HasIndex(e => new { e.SnapshotDate, e.ProfileId, e.Category },
                "idx_risk_index_object_days_date_profile_category");
            entity.HasIndex(e => e.EntityId, "idx_risk_index_object_days_entity_id");
            entity.HasIndex(e => new { e.SnapshotDate, e.AssetClass }, "idx_risk_index_object_days_date_class");
            entity.HasIndex(e => e.ProfileId, "idx_risk_index_object_days_profile_id");

            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_object_days_profile_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRiskIndexObjectAttributions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexObjectAttribution>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_index_object_attributions")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.SnapshotDate).HasColumnName("snapshot_date").HasColumnType("date");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectKind).HasColumnName("object_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ObjectRef).HasColumnName("object_ref").HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.AttributedEntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.ScopeEntityId).HasColumnName("scope_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // A derived node reads one day's attributions to its entities (S40 §7.2); the scope filter reads scope_entity_id.
            entity.HasIndex(e => new { e.SnapshotDate, e.ProfileId, e.AttributedEntityId },
                "idx_risk_index_object_attributions_date_profile_entity");
            entity.HasIndex(e => e.ScopeEntityId, "idx_risk_index_object_attributions_scope_entity_id");
            entity.HasIndex(e => e.ProfileId, "idx_risk_index_object_attributions_profile_id");

            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_object_attributions_profile_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRiskIndexNodeMembers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexNodeMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            // A closure member is always an entity; only a scope member may be null (an object with no scope entity).
            entity.ToTable("risk_index_node_members", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_node_members_kind", "`member_kind` >= 1 AND `member_kind` <= 2");
                    t.HasCheckConstraint("ck_risk_index_node_members_member",
                        "`member_kind` = 2 OR `member_entity_id` IS NOT NULL");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.SnapshotDate).HasColumnName("snapshot_date").HasColumnType("date");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.NodeEntityId).HasColumnName("node_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.MemberKind).HasColumnName("member_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.MemberEntityId).HasColumnName("member_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.SnapshotDate, e.ProfileId, e.NodeEntityId },
                "idx_risk_index_node_members_date_profile_node");
            entity.HasIndex(e => e.ProfileId, "idx_risk_index_node_members_profile_id");

            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_node_members_profile_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRiskIndexClassDays(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexClassDay>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_index_class_days", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_class_days_tab_group", "`tab_group` >= 1 AND `tab_group` <= 5");
                    t.HasCheckConstraint("ck_risk_index_class_days_category", "`category` >= 1 AND `category` <= 6");
                    t.HasCheckConstraint("ck_risk_index_class_days_value", "`value` >= 0 AND `value` <= 100");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.SnapshotDate).HasColumnName("snapshot_date").HasColumnType("date");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.NodeEntityId).HasColumnName("node_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.TabGroup).HasColumnName("tab_group").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Category).HasColumnName("category").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("double");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // The root's rows have a null node, which a unique index does not compare; the job writes a day once.
            entity.HasIndex(e => new { e.SnapshotDate, e.ProfileId, e.NodeEntityId, e.TabGroup, e.Category },
                "uq_risk_index_class_days_date_profile_node_tab_category").IsUnique();
            entity.HasIndex(e => new { e.NodeEntityId, e.TabGroup, e.SnapshotDate },
                "idx_risk_index_class_days_node_tab_date");
            entity.HasIndex(e => e.ProfileId, "idx_risk_index_class_days_profile_id");

            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_class_days_profile_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRiskIndexIncidentClosures(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexIncidentClosure>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("PRIMARY");

            entity.ToTable("risk_index_incident_closures")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            // The incident's own id, never generated here.
            entity.Property(e => e.IncidentId).HasColumnName("incident_id").HasColumnType("int(11)").ValueGeneratedNever();
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at").HasColumnType("datetime");
            entity.Property(e => e.FrozenOn).HasColumnName("frozen_on").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
        });
    }

    private static void ConfigureRiskIndexBridges(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexBridge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_index_bridges", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_bridges_scope_kind", "`scope_kind` >= 1 AND `scope_kind` <= 4");
                    t.HasCheckConstraint("ck_risk_index_bridges_category", "`category` >= 0 AND `category` <= 6");
                    t.HasCheckConstraint("ck_risk_index_bridges_profiles", "`from_profile_id` <> `to_profile_id`");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20)");
            entity.Property(e => e.FromProfileId).HasColumnName("from_profile_id").HasColumnType("int(11)");
            entity.Property(e => e.ToProfileId).HasColumnName("to_profile_id").HasColumnType("int(11)");
            entity.Property(e => e.BridgeDate).HasColumnName("bridge_date").HasColumnType("date");
            entity.Property(e => e.ScopeKind).HasColumnName("scope_kind").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ScopeRef).HasColumnName("scope_ref").HasColumnType("varchar(64)").HasMaxLength(64);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Category).HasColumnName("category").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.ValueOld).HasColumnName("value_old").HasColumnType("double");
            entity.Property(e => e.ValueNew).HasColumnName("value_new").HasColumnType("double");
            entity.Property(e => e.Overlay).HasColumnName("overlay").HasColumnType("longtext");
            entity.Property(e => e.OverlayPartial).HasColumnName("overlay_partial").HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // One bridge per new profile, node and category (S40 §7.2): a profile is activated once.
            entity.HasIndex(e => new { e.ToProfileId, e.ScopeKind, e.ScopeRef, e.Category },
                "uq_risk_index_bridges_to_scope_category").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_risk_index_bridges_entity_id");
            entity.HasIndex(e => e.FromProfileId, "idx_risk_index_bridges_from_profile_id");

            // RESTRICT, as on the snapshots: the two profiles are the evidence of what the bridge compares.
            entity.HasOne(e => e.FromProfile)
                .WithMany()
                .HasForeignKey(e => e.FromProfileId)
                .HasConstraintName("fk_risk_index_bridges_from_profile_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ToProfile)
                .WithMany()
                .HasForeignKey(e => e.ToProfileId)
                .HasConstraintName("fk_risk_index_bridges_to_profile_id")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureRiskIndexPreviewRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexPreviewRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_index_preview_requests", t =>
                {
                    t.HasCheckConstraint("ck_risk_index_preview_requests_status", "`status` >= 1 AND `status` <= 4");
                    t.HasCheckConstraint("ck_risk_index_preview_requests_progress", "`progress` >= 0 AND `progress` <= 100");
                })
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.ParametersHash).HasColumnName("parameters_hash").HasColumnType("varchar(64)")
                .HasMaxLength(64);
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.Progress).HasColumnName("progress").HasColumnType("int(11)").HasDefaultValue(0);
            entity.Property(e => e.RequestedById).HasColumnName("requested_by_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("datetime");
            entity.Property(e => e.HeartbeatAt).HasColumnName("heartbeat_at").HasColumnType("datetime");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at").HasColumnType("datetime");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
            entity.Property(e => e.Report).HasColumnName("report").HasColumnType("longtext");

            // The job claims the oldest Queued request (S40 §9.6).
            entity.HasIndex(e => e.Status, "idx_risk_index_preview_requests_status");
            entity.HasIndex(e => e.ProfileId, "idx_risk_index_preview_requests_profile_id");
            entity.HasIndex(e => e.RequestedById, "idx_risk_index_preview_requests_requested_by_id");

            // CASCADE: deleting a draft takes its previews (S40 §7.2, §8.4).
            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_preview_requests_profile_id")
                .OnDelete(DeleteBehavior.Cascade);

            // SET NULL, as every user reference of the index: removing a user does not remove the evidence.
            entity.HasOne(e => e.RequestedBy)
                .WithMany()
                .HasForeignKey(e => e.RequestedById)
                .HasConstraintName("fk_risk_index_preview_requests_requested_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureRiskIndexProfileEditors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskIndexProfileEditor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_index_profile_editors")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id").HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.ProfileId, e.UserId }, "uq_risk_index_profile_editors_profile_id_user_id").IsUnique();
            entity.HasIndex(e => e.UserId, "idx_risk_index_profile_editors_user_id");

            // CASCADE: a deleted draft takes its editors, as it takes its previews.
            entity.HasOne(e => e.Profile)
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .HasConstraintName("fk_risk_index_profile_editors_profile_id")
                .OnDelete(DeleteBehavior.Cascade);

            // SET NULL, as every user reference of the index: removing a user does not remove the evidence.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_risk_index_profile_editors_user_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// <c>nr_files.risk_index_profile_id</c> (S40 §7.3): the committee's minutes. SET NULL, as the spec's precedent
    /// says; a profile with evidence is never a draft, and only a draft is deleted, so the evidence is never orphaned.
    /// </summary>
    private static void ConfigureRiskIndexProfileFiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NrFile>(entity =>
        {
            entity.Property(e => e.RiskIndexProfileId).HasColumnName("risk_index_profile_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.RiskIndexProfileId, "idx_nr_files_risk_index_profile_id");

            entity.HasOne<RiskIndexProfile>()
                .WithMany()
                .HasForeignKey(e => e.RiskIndexProfileId)
                .HasConstraintName("fk_nr_files_risk_index_profile_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
