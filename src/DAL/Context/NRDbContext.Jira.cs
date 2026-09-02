using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Track 4 milestone 4.6 (Jira Service Management &amp; Assets) schema: the Jira facet of an
/// issue-tracker connection, the mirrored service-desk requests and their SLA cycles, the three
/// configurable mappings (Jira fields, Assets object types, Assets attributes), and the imported
/// Assets register.
///
/// A partial of its own rather than more of <c>NRDbContext.Integrations.cs</c>, which is already
/// 700-odd lines covering five milestones: one file per milestone is what keeps a review of this
/// change from being a review of all of Track 4.
///
/// Named per the Track 6 convention throughout — snake_case columns via <c>HasColumnName</c>,
/// <c>fk_</c>/<c>idx_</c>/<c>uq_</c> constraint prefixes, int-backed enums with explicit
/// conversions, <c>tinyint(1)</c> booleans, UTC <c>datetime</c> temporal columns, and
/// <c>varchar(n)</c>/<c>text</c> for strings — never <c>char(n)</c>, which EF Core 10 reads as a
/// primitive collection of <c>char</c> and dies on.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<JiraConnectionSettings> JiraConnectionSettings { get; set; } = null!;

    public virtual DbSet<JiraQueueImport> JiraQueueImports { get; set; } = null!;

    public virtual DbSet<JiraServiceRequest> JiraServiceRequests { get; set; } = null!;

    public virtual DbSet<JiraRequestSla> JiraRequestSlas { get; set; } = null!;

    public virtual DbSet<JiraFieldMapping> JiraFieldMappings { get; set; } = null!;

    public virtual DbSet<JiraObjectMapping> JiraObjectMappings { get; set; } = null!;

    public virtual DbSet<JiraObjectAttributeMapping> JiraObjectAttributeMappings { get; set; } = null!;

    public virtual DbSet<JiraAssetObject> JiraAssetObjects { get; set; } = null!;

    private static void ConfigureJira(ModelBuilder modelBuilder)
    {
        ConfigureJiraConnectionSettings(modelBuilder);
        ConfigureJiraQueueImports(modelBuilder);
        ConfigureJiraServiceRequests(modelBuilder);
        ConfigureJiraRequestSlas(modelBuilder);
        ConfigureJiraFieldMappings(modelBuilder);
        ConfigureJiraObjectMappings(modelBuilder);
        ConfigureJiraObjectAttributeMappings(modelBuilder);
        ConfigureJiraAssetObjects(modelBuilder);
        ConfigureIssueLinkTargets(modelBuilder);
        ConfigureHostAssetColumns(modelBuilder);
    }

    private static void ConfigureJiraConnectionSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraConnectionSettings>(entity =>
        {
            // The connection id is the key. A surrogate id would allow two settings rows for one
            // connection, and nothing in the code could then say which of them is in force.
            entity.HasKey(e => e.ConnectionId).HasName("PRIMARY");

            entity.ToTable("jira_connection_settings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)")
                .ValueGeneratedNever();
            entity.Property(e => e.Deployment).HasColumnName("deployment").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.JsmEnabled).HasColumnName("jsm_enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.ServiceDeskId).HasColumnName("service_desk_id").HasColumnType("int(11)");
            entity.Property(e => e.ServiceDeskName).HasColumnName("service_desk_name").HasMaxLength(255);
            entity.Property(e => e.RequestTypeFilter).HasColumnName("request_type_filter").HasMaxLength(512);
            entity.Property(e => e.ImportSlas).HasColumnName("import_slas").HasColumnType("tinyint(1)");
            entity.Property(e => e.SlaBreachNotifications).HasColumnName("sla_breach_notifications")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.DefaultLinkTargetKind).HasColumnName("default_link_target_kind")
                .HasColumnType("int(11)").HasConversion<int>();
            entity.Property(e => e.LastJsmSyncAt).HasColumnName("last_jsm_sync_at").HasColumnType("datetime");
            entity.Property(e => e.AssetsEnabled).HasColumnName("assets_enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.AssetsWorkspaceId).HasColumnName("assets_workspace_id").HasMaxLength(128);
            entity.Property(e => e.AssetsSchemaId).HasColumnName("assets_schema_id").HasColumnType("int(11)");
            entity.Property(e => e.AssetsSchemaName).HasColumnName("assets_schema_name").HasMaxLength(255);
            entity.Property(e => e.LastAssetsSyncAt).HasColumnName("last_assets_sync_at")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasOne(e => e.Connection)
                .WithOne(c => c.JiraSettings)
                .HasForeignKey<JiraConnectionSettings>(e => e.ConnectionId)
                .HasConstraintName("fk_jira_connection_settings_connection_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureJiraQueueImports(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraQueueImport>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_queue_imports")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ServiceDeskId).HasColumnName("service_desk_id").HasColumnType("int(11)");
            entity.Property(e => e.QueueId).HasColumnName("queue_id").HasColumnType("int(11)");
            entity.Property(e => e.QueueName).HasColumnName("queue_name").HasMaxLength(255);
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.LinkTargetKind).HasColumnName("link_target_kind")
                .HasColumnType("int(11)").HasConversion<int?>();
            entity.Property(e => e.MaxRequests).HasColumnName("max_requests").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // One row per queue per connection: two rows for the same queue would import it twice
            // per sync, and the second pass would only ever be a no-op that costs rate limit.
            entity.HasIndex(e => new { e.ConnectionId, e.QueueId }, "uq_jira_queue_imports_connection_queue")
                .IsUnique();

            entity.HasOne(e => e.Settings)
                .WithMany(s => s.QueueImports)
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_jira_queue_imports_connection_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureJiraServiceRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraServiceRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_service_requests")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.IssueKey).HasColumnName("issue_key").HasMaxLength(128);
            entity.Property(e => e.IssueId).HasColumnName("issue_id").HasMaxLength(128);
            entity.Property(e => e.ServiceDeskId).HasColumnName("service_desk_id").HasColumnType("int(11)");
            entity.Property(e => e.RequestTypeId).HasColumnName("request_type_id").HasMaxLength(128);
            entity.Property(e => e.RequestTypeName).HasColumnName("request_type_name").HasMaxLength(255);
            entity.Property(e => e.Summary).HasColumnName("summary").HasColumnType("text");
            entity.Property(e => e.StatusName).HasColumnName("status_name").HasMaxLength(128);
            entity.Property(e => e.StatusCategory).HasColumnName("status_category").HasMaxLength(64);
            entity.Property(e => e.ReporterAccountId).HasColumnName("reporter_account_id").HasMaxLength(128);
            entity.Property(e => e.ReporterDisplayName).HasColumnName("reporter_display_name")
                .HasMaxLength(255);
            entity.Property(e => e.OrganizationName).HasColumnName("organization_name").HasMaxLength(255);
            entity.Property(e => e.PriorityName).HasColumnName("priority_name").HasMaxLength(128);
            entity.Property(e => e.AssigneeDisplayName).HasColumnName("assignee_display_name")
                .HasMaxLength(255);
            entity.Property(e => e.CreatedAtRemote).HasColumnName("created_at_remote").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAtRemote).HasColumnName("updated_at_remote").HasColumnType("datetime");
            entity.Property(e => e.IsClosed).HasColumnName("is_closed").HasColumnType("tinyint(1)");
            entity.Property(e => e.RequestUrl).HasColumnName("request_url").HasMaxLength(1024);
            entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").HasColumnType("datetime");
            entity.Property(e => e.LastSyncedAt).HasColumnName("last_synced_at").HasColumnType("datetime");
            entity.Property(e => e.SyncError).HasColumnName("sync_error").HasColumnType("text");

            // The upsert key. Without it a re-sync of the same queue appends a second copy of every
            // request, which is the failure mode of every mirror written without one.
            entity.HasIndex(e => new { e.ConnectionId, e.IssueKey },
                "uq_jira_service_requests_connection_key").IsUnique();
            entity.HasIndex(e => new { e.ConnectionId, e.IsClosed },
                "idx_jira_service_requests_connection_closed");
            entity.HasIndex(e => e.UpdatedAtRemote, "idx_jira_service_requests_updated_at_remote");

            entity.HasOne(e => e.Connection)
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_jira_service_requests_connection_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureJiraRequestSlas(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraRequestSla>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_request_slas")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("int(11)");
            entity.Property(e => e.MetricId).HasColumnName("metric_id").HasMaxLength(128);
            entity.Property(e => e.MetricName).HasColumnName("metric_name").HasMaxLength(255);
            entity.Property(e => e.IsOngoing).HasColumnName("is_ongoing").HasColumnType("tinyint(1)");
            entity.Property(e => e.Breached).HasColumnName("breached").HasColumnType("tinyint(1)");
            entity.Property(e => e.Paused).HasColumnName("paused").HasColumnType("tinyint(1)");
            entity.Property(e => e.GoalDurationMs).HasColumnName("goal_duration_ms").HasColumnType("bigint(20)");
            entity.Property(e => e.ElapsedMs).HasColumnName("elapsed_ms").HasColumnType("bigint(20)");
            entity.Property(e => e.RemainingMs).HasColumnName("remaining_ms").HasColumnType("bigint(20)");
            entity.Property(e => e.CycleStartAt).HasColumnName("cycle_start_at").HasColumnType("datetime");
            entity.Property(e => e.CycleStopAt).HasColumnName("cycle_stop_at").HasColumnType("datetime");
            entity.Property(e => e.CapturedAt).HasColumnName("captured_at").HasColumnType("datetime");

            // The cycle start is part of the key because a reopened request starts a second cycle of
            // the same metric, and keying on (request, metric) alone would overwrite the first
            // breach with the second cycle's clean state.
            entity.HasIndex(e => new { e.RequestId, e.MetricName, e.CycleStartAt },
                "uq_jira_request_slas_request_metric_cycle").IsUnique();
            entity.HasIndex(e => new { e.Breached, e.IsOngoing }, "idx_jira_request_slas_breached_ongoing");

            entity.HasOne(e => e.Request)
                .WithMany(r => r.Slas)
                .HasForeignKey(e => e.RequestId)
                .HasConstraintName("fk_jira_request_slas_request_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureJiraFieldMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraFieldMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_field_mappings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.Direction).HasColumnName("direction").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.NetRiskField).HasColumnName("netrisk_field").HasMaxLength(128);
            entity.Property(e => e.JiraFieldId).HasColumnName("jira_field_id").HasMaxLength(128);
            entity.Property(e => e.JiraFieldName).HasColumnName("jira_field_name").HasMaxLength(255);
            entity.Property(e => e.JiraFieldType).HasColumnName("jira_field_type").HasMaxLength(64);
            entity.Property(e => e.Transform).HasColumnName("transform").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.ConstantValue).HasColumnName("constant_value").HasColumnType("text");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");

            // One writer per Jira field per direction. Two rows targeting customfield_10012 is a
            // configuration whose result depends on row order, which is the kind of bug that gets
            // reported as "the integration is flaky".
            entity.HasIndex(e => new { e.ConnectionId, e.Direction, e.JiraFieldId },
                "uq_jira_field_mappings_connection_direction_field").IsUnique();

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.FieldMappings)
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_jira_field_mappings_connection_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureJiraObjectMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraObjectMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_object_mappings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectTypeId).HasColumnName("object_type_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectTypeName).HasColumnName("object_type_name").HasMaxLength(255);
            entity.Property(e => e.TargetKind).HasColumnName("target_kind").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.AqlFilter).HasColumnName("aql_filter").HasColumnType("text");
            entity.Property(e => e.MatchStrategy).HasColumnName("match_strategy").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.CreateMissing).HasColumnName("create_missing").HasColumnType("tinyint(1)");
            entity.Property(e => e.UpdateExisting).HasColumnName("update_existing").HasColumnType("tinyint(1)");
            entity.Property(e => e.DeactivateMissing).HasColumnName("deactivate_missing")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.LastImportedAt).HasColumnName("last_imported_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => new { e.ConnectionId, e.ObjectTypeId },
                "uq_jira_object_mappings_connection_object_type").IsUnique();
            entity.HasIndex(e => e.CreatedById, "idx_jira_object_mappings_created_by_id");

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.ObjectMappings)
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_jira_object_mappings_connection_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_jira_object_mappings_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureJiraObjectAttributeMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraObjectAttributeMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_object_attribute_mappings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.MappingId).HasColumnName("mapping_id").HasColumnType("int(11)");
            entity.Property(e => e.SourceAttributeId).HasColumnName("source_attribute_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.SourceAttributeName).HasColumnName("source_attribute_name")
                .HasMaxLength(255);
            entity.Property(e => e.TargetField).HasColumnName("target_field").HasMaxLength(128);
            entity.Property(e => e.Transform).HasColumnName("transform").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.IsIdentity).HasColumnName("is_identity").HasColumnType("tinyint(1)");
            entity.Property(e => e.ConstantValue).HasColumnName("constant_value").HasColumnType("text");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasColumnType("int(11)");

            // One source per target field. Two attributes writing Environment would make the imported
            // value depend on which row the importer happened to read last.
            entity.HasIndex(e => new { e.MappingId, e.TargetField },
                "uq_jira_object_attribute_mappings_mapping_target").IsUnique();

            entity.HasOne(e => e.Mapping)
                .WithMany(m => m.AttributeMappings)
                .HasForeignKey(e => e.MappingId)
                .HasConstraintName("fk_jira_object_attribute_mappings_mapping_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureJiraAssetObjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JiraAssetObject>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("jira_asset_objects")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectId).HasColumnName("object_id").HasMaxLength(128);
            entity.Property(e => e.ObjectKey).HasColumnName("object_key").HasMaxLength(128);
            entity.Property(e => e.ObjectTypeId).HasColumnName("object_type_id").HasColumnType("int(11)");
            entity.Property(e => e.ObjectTypeName).HasColumnName("object_type_name").HasMaxLength(255);
            entity.Property(e => e.Label).HasColumnName("label").HasMaxLength(512);
            entity.Property(e => e.MappedName).HasColumnName("mapped_name").HasMaxLength(512);
            entity.Property(e => e.MappedOwner).HasColumnName("mapped_owner").HasMaxLength(255);
            entity.Property(e => e.MappedEnvironment).HasColumnName("mapped_environment").HasMaxLength(64);
            entity.Property(e => e.MappedActive).HasColumnName("mapped_active").HasColumnType("tinyint(1)");
            entity.Property(e => e.AttributesJson).HasColumnName("attributes_json").HasColumnType("longtext");
            entity.Property(e => e.TargetKind).HasColumnName("target_kind").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.TargetHostId).HasColumnName("target_host_id").HasColumnType("int(11)");
            entity.Property(e => e.TargetEntityId).HasColumnName("target_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.MatchReason).HasColumnName("match_reason").HasMaxLength(128);
            entity.Property(e => e.CreatedAtRemote).HasColumnName("created_at_remote").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAtRemote).HasColumnName("updated_at_remote").HasColumnType("datetime");
            entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").HasColumnType("datetime");
            entity.Property(e => e.LastSyncedAt).HasColumnName("last_synced_at").HasColumnType("datetime");
            entity.Property(e => e.ImportError).HasColumnName("import_error").HasColumnType("text");

            entity.HasIndex(e => new { e.ConnectionId, e.ObjectId },
                "uq_jira_asset_objects_connection_object").IsUnique();
            entity.HasIndex(e => e.TargetHostId, "idx_jira_asset_objects_target_host_id");
            entity.HasIndex(e => e.TargetEntityId, "idx_jira_asset_objects_target_entity_id");

            entity.HasOne(e => e.Connection)
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_jira_asset_objects_connection_id")
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull, not Cascade: deleting a host must not delete the record that Jira reported it.
            // The audit row saying "this Assets object mapped to a host that has since been removed"
            // is exactly the row somebody needs when the machine reappears next import.
            entity.HasOne(e => e.TargetHost)
                .WithMany()
                .HasForeignKey(e => e.TargetHostId)
                .HasConstraintName("fk_jira_asset_objects_target_host_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TargetEntity)
                .WithMany()
                .HasForeignKey(e => e.TargetEntityId)
                .HasConstraintName("fk_jira_asset_objects_target_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// The 4.6 widening of <c>finding_issue_links</c> from findings to findings, incidents and risks.
    ///
    /// <c>vulnerability_id</c> becomes nullable and gains two siblings. Configured here rather than
    /// edited into <see cref="ConfigureFindingIssueLinks"/> so the milestone's whole schema footprint
    /// is one file; EF merges the two configurations for the same entity.
    /// </summary>
    private static void ConfigureIssueLinkTargets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FindingIssueLink>(entity =>
        {
            entity.Property(e => e.TargetKind).HasColumnName("target_kind").HasColumnType("int(11)")
                .HasConversion<int>().HasDefaultValue(IssueLinkTargetKind.Finding);
            entity.Property(e => e.IncidentId).HasColumnName("incident_id").HasColumnType("int(11)");
            entity.Property(e => e.RiskId).HasColumnName("risk_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.IncidentId, "idx_finding_issue_links_incident_id");
            entity.HasIndex(e => e.RiskId, "idx_finding_issue_links_risk_id");

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .HasConstraintName("fk_finding_issue_links_incident_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Risk)
                .WithMany()
                .HasForeignKey(e => e.RiskId)
                .HasConstraintName("fk_finding_issue_links_risk_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>The two CMDB columns 4.6 adds to <c>hosts</c> (see <c>Host.Integrations.cs</c>).</summary>
    private static void ConfigureHostAssetColumns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Host>(entity =>
        {
            entity.Property(e => e.Environment).HasColumnName("environment").HasMaxLength(64);
            entity.Property(e => e.Owner).HasColumnName("owner").HasMaxLength(255);
        });
    }
}
