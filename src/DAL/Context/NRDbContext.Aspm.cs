using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Track 3 (ASPM) schema: finding lifecycle and audit trail (3.2), deduplication and scan-import
/// log (3.3), SLA policy and notification guard (3.4), and CI API tokens (3.5).
///
/// Configured in this partial rather than the generated <c>OnModelCreating</c> so that file stays
/// regenerable, and named per the Track 6 convention throughout — snake_case columns via
/// <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c> constraint prefixes, int-backed enums
/// with explicit conversions, <c>tinyint(1)</c> booleans, UTC <c>datetime</c> temporal columns.
/// New schema is expected to be born compliant rather than added to the drift.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<FindingStatusHistory> FindingStatusHistories { get; set; } = null!;

    public virtual DbSet<RiskAcceptance> RiskAcceptances { get; set; } = null!;

    public virtual DbSet<RiskAcceptanceFinding> RiskAcceptanceFindings { get; set; } = null!;

    public virtual DbSet<ScanImport> ScanImports { get; set; } = null!;

    public virtual DbSet<ScannerDedupConfiguration> ScannerDedupConfigurations { get; set; } = null!;

    public virtual DbSet<ScannerDedupConfigurationHistory> ScannerDedupConfigurationHistories { get; set; } = null!;

    public virtual DbSet<SlaConfiguration> SlaConfigurations { get; set; } = null!;

    public virtual DbSet<SlaNotification> SlaNotifications { get; set; } = null!;

    public virtual DbSet<ApiToken> ApiTokens { get; set; } = null!;

    private static void ConfigureAspm(ModelBuilder modelBuilder)
    {
        ConfigureVulnerabilityAspmColumns(modelBuilder);
        ConfigureFindingStatusHistory(modelBuilder);
        ConfigureRiskAcceptance(modelBuilder);
        ConfigureScanImports(modelBuilder);
        ConfigureDedupConfiguration(modelBuilder);
        ConfigureSla(modelBuilder);
        ConfigureApiTokens(modelBuilder);
        ConfigureAcceptanceAttachments(modelBuilder);
    }

    /// <summary>
    /// The file attachments an acceptance carries as evidence (3.2.3). Configured here rather than
    /// alongside the generated <c>NrFile</c> mapping so the whole Track 3 surface stays in one file.
    /// </summary>
    private static void ConfigureAcceptanceAttachments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NrFile>(entity =>
        {
            entity.Property(e => e.RiskAcceptanceId)
                .HasColumnName("risk_acceptance_id")
                .HasColumnType("int(11)");

            entity.HasIndex(e => e.RiskAcceptanceId, "idx_files_risk_acceptance_id");

            // Deleting an acceptance takes its evidence with it: the attachments have no meaning
            // detached from the decision they document.
            entity.HasOne(e => e.RiskAcceptance)
                .WithMany()
                .HasForeignKey(e => e.RiskAcceptanceId)
                .HasConstraintName("fk_files_risk_acceptance_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureVulnerabilityAspmColumns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vulnerability>(entity =>
        {
            entity.Property(e => e.LifecycleStatus)
                .HasColumnName("status_id")
                .HasColumnType("int(11)")
                .HasDefaultValue(FindingStatus.Active)
                // 0 is not a member of FindingStatus, so the CLR default genuinely means "not set"
                // and the database default should apply. Declaring the sentinel explicitly is what
                // tells EF that, instead of it warning that 0 might have been meant literally.
                .HasSentinel(default)
                .HasConversion<int>();

            // 64 hex characters: the dedup key is a SHA-256 digest, or a tool id hashed to the same
            // width so every key is one comparable, indexable shape.
            entity.Property(e => e.DedupKey).HasColumnName("dedup_key").HasMaxLength(64);
            entity.Property(e => e.DedupStrategy).HasColumnName("dedup_strategy").HasMaxLength(32);
            entity.Property(e => e.SlaDueDate).HasColumnName("sla_due_date").HasColumnType("datetime");
            entity.Property(e => e.RuleId).HasColumnName("rule_id").HasMaxLength(255);
            entity.Property(e => e.ToolUniqueId).HasColumnName("tool_unique_id").HasMaxLength(255);
            entity.Property(e => e.Location).HasColumnName("location").HasColumnType("text");
            entity.Property(e => e.Component).HasColumnName("component").HasMaxLength(255);
            entity.Property(e => e.ComponentVersion).HasColumnName("component_version").HasMaxLength(255);
            entity.Property(e => e.FixedInVersion).HasColumnName("fixed_in_version").HasMaxLength(255);
            entity.Property(e => e.RawSeverity).HasColumnName("raw_severity").HasMaxLength(64);
            entity.Property(e => e.Cwes).HasColumnName("cwes").HasColumnType("text");
            entity.Property(e => e.LastImportId).HasColumnName("last_import_id").HasColumnType("int(11)");
            entity.Property(e => e.DuplicateOfId).HasColumnName("duplicate_of_id").HasColumnType("int(11)");

            // The dedup lookup is one indexed equality read per imported finding, so this index is
            // the difference between an import that scales and one that does not.
            entity.HasIndex(e => e.DedupKey, "idx_vulnerabilities_dedup_key");
            entity.HasIndex(e => e.LifecycleStatus, "idx_vulnerabilities_status_id");
            entity.HasIndex(e => e.SlaDueDate, "idx_vulnerabilities_sla_due_date");
            entity.HasIndex(e => e.DuplicateOfId, "idx_vulnerabilities_duplicate_of_id");

            // Self-reference for the canonical finding of a duplicate. Restrict rather than cascade:
            // deleting the canonical finding must not silently delete the duplicates that point at
            // it, and NoAction leaves the constraint to the database, which is where the numbered
            // SQL declares it as ON DELETE SET NULL.
            entity.HasOne<Vulnerability>()
                .WithMany()
                .HasForeignKey(e => e.DuplicateOfId)
                .HasConstraintName("fk_vulnerabilities_duplicate_of_id")
                .OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureFindingStatusHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FindingStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("finding_status_history")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.VulnerabilityId).HasColumnName("vulnerability_id").HasColumnType("int(11)");
            entity.Property(e => e.FromStatus).HasColumnName("from_status_id").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.ToStatus).HasColumnName("to_status_id").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.ChangedAt).HasColumnName("changed_at").HasColumnType("datetime");
            entity.Property(e => e.Source).HasColumnName("source").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Justification).HasColumnName("justification").HasColumnType("text");
            entity.Property(e => e.RiskAcceptanceId).HasColumnName("risk_acceptance_id").HasColumnType("int(11)");
            entity.Property(e => e.DuplicateOfId).HasColumnName("duplicate_of_id").HasColumnType("int(11)");

            // The timeline is always read as "this finding, newest first".
            entity.HasIndex(e => new { e.VulnerabilityId, e.ChangedAt }, "idx_fsh_vulnerability_changed_at");
            entity.HasIndex(e => e.UserId, "idx_fsh_user_id");
            entity.HasIndex(e => e.RiskAcceptanceId, "idx_fsh_risk_acceptance_id");

            // History follows its finding into deletion — an orphan timeline for a finding nobody
            // can look up is not evidence of anything.
            entity.HasOne(e => e.Vulnerability)
                .WithMany(v => v.StatusHistory)
                .HasForeignKey(e => e.VulnerabilityId)
                .HasConstraintName("fk_fsh_vulnerability_id")
                .OnDelete(DeleteBehavior.Cascade);

            // The actor is kept even if their account goes away: "user 14 accepted this" with the
            // account deleted is still a better audit record than a row with no actor at all, so
            // the reference is nulled rather than the row removed.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_fsh_user_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RiskAcceptance)
                .WithMany()
                .HasForeignKey(e => e.RiskAcceptanceId)
                .HasConstraintName("fk_fsh_risk_acceptance_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureRiskAcceptance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskAcceptance>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_acceptances")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.BusinessJustification).HasColumnName("business_justification")
                .HasColumnType("text");
            entity.Property(e => e.AuthorizingManagerId).HasColumnName("authorizing_manager_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime");
            entity.Property(e => e.CompensatingControls).HasColumnName("compensating_controls")
                .HasColumnType("text");
            entity.Property(e => e.ResidualScoreSnapshot).HasColumnName("residual_score_snapshot");
            entity.Property(e => e.Status).HasColumnName("status_id").HasColumnType("int(11)")
                .HasDefaultValue(RiskAcceptanceStatus.Active)
                // As above: 0 is not a member of RiskAcceptanceStatus.
                .HasSentinel(default)
                .HasConversion<int>();
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedById).HasColumnName("revoked_by_id").HasColumnType("int(11)");
            entity.Property(e => e.RevocationReason).HasColumnName("revocation_reason").HasColumnType("text");
            entity.Property(e => e.LastWarningDaysBefore).HasColumnName("last_warning_days_before")
                .HasColumnType("int(11)");

            // The expiry job's query is "active acceptances ordered by expiry", and the
            // expiring-within-30-days filter reads the same index.
            entity.HasIndex(e => new { e.Status, e.ExpiresAt }, "idx_ra_status_expires_at");
            entity.HasIndex(e => e.AuthorizingManagerId, "idx_ra_authorizing_manager_id");
            entity.HasIndex(e => e.EntityId, "idx_ra_entity_id");

            entity.HasOne(e => e.AuthorizingManager)
                .WithMany()
                .HasForeignKey(e => e.AuthorizingManagerId)
                .HasConstraintName("fk_ra_authorizing_manager_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_ra_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RevokedBy)
                .WithMany()
                .HasForeignKey(e => e.RevokedById)
                .HasConstraintName("fk_ra_revoked_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_ra_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskAcceptanceFinding>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("risk_acceptance_findings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.RiskAcceptanceId).HasColumnName("risk_acceptance_id").HasColumnType("int(11)");
            entity.Property(e => e.VulnerabilityId).HasColumnName("vulnerability_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

            // The same finding twice under one acceptance would double-count it on expiry.
            entity.HasIndex(e => new { e.RiskAcceptanceId, e.VulnerabilityId }, "uq_raf_acceptance_finding")
                .IsUnique();
            entity.HasIndex(e => e.VulnerabilityId, "idx_raf_vulnerability_id");

            entity.HasOne(e => e.RiskAcceptance)
                .WithMany(a => a.Findings)
                .HasForeignKey(e => e.RiskAcceptanceId)
                .HasConstraintName("fk_raf_risk_acceptance_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Vulnerability)
                .WithMany(v => v.RiskAcceptances)
                .HasForeignKey(e => e.VulnerabilityId)
                .HasConstraintName("fk_raf_vulnerability_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureScanImports(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanImport>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("scan_imports")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Importer).HasColumnName("importer").HasMaxLength(64);
            entity.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(512);
            entity.Property(e => e.FileId).HasColumnName("file_id").HasMaxLength(128);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.JobId).HasColumnName("job_id").HasColumnType("int(11)");
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("datetime");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at").HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)");
            entity.Property(e => e.NewCount).HasColumnName("new_count").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedCount).HasColumnName("updated_count").HasColumnType("int(11)");
            entity.Property(e => e.DuplicateCount).HasColumnName("duplicate_count").HasColumnType("int(11)");
            entity.Property(e => e.ClosedCount).HasColumnName("closed_count").HasColumnType("int(11)");
            entity.Property(e => e.SkippedCount).HasColumnName("skipped_count").HasColumnType("int(11)");
            entity.Property(e => e.WarningCount).HasColumnName("warning_count").HasColumnType("int(11)");
            entity.Property(e => e.NewBySeverity).HasColumnName("new_by_severity").HasMaxLength(255);
            entity.Property(e => e.Warnings).HasColumnName("warnings").HasColumnType("longtext");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);

            // The idempotency guarantee is enforced by the database, not by a check-then-insert in
            // the service: two concurrent CI retries would both pass the check.
            entity.HasIndex(e => e.IdempotencyKey, "uq_scan_imports_idempotency_key").IsUnique();
            entity.HasIndex(e => new { e.Importer, e.StartedAt }, "idx_scan_imports_importer_started_at");
            entity.HasIndex(e => e.JobId, "idx_scan_imports_job_id");
            entity.HasIndex(e => e.EntityId, "idx_scan_imports_entity_id");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_scan_imports_user_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_scan_imports_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureDedupConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScannerDedupConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("scanner_dedup_configurations")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Importer).HasColumnName("importer").HasMaxLength(64);
            entity.Property(e => e.StrategyChain).HasColumnName("strategy_chain").HasMaxLength(255);
            entity.Property(e => e.HashFields).HasColumnName("hash_fields").HasMaxLength(512);
            entity.Property(e => e.AutoCloseMissing).HasColumnName("auto_close_missing")
                .HasColumnType("tinyint(1)")
                .HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by_id").HasColumnType("int(11)");

            // One configuration per importer; a second row would make "which one applies" a coin toss.
            entity.HasIndex(e => e.Importer, "uq_sdc_importer").IsUnique();

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .HasConstraintName("fk_sdc_updated_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ScannerDedupConfigurationHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("scanner_dedup_configuration_history")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Importer).HasColumnName("importer").HasMaxLength(64);
            entity.Property(e => e.OldStrategyChain).HasColumnName("old_strategy_chain").HasMaxLength(255);
            entity.Property(e => e.NewStrategyChain).HasColumnName("new_strategy_chain").HasMaxLength(255);
            entity.Property(e => e.OldHashFields).HasColumnName("old_hash_fields").HasMaxLength(512);
            entity.Property(e => e.NewHashFields).HasColumnName("new_hash_fields").HasMaxLength(512);
            entity.Property(e => e.OldAutoCloseMissing).HasColumnName("old_auto_close_missing")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.NewAutoCloseMissing).HasColumnName("new_auto_close_missing")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.ChangedAt).HasColumnName("changed_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.Importer, e.ChangedAt }, "idx_sdch_importer_changed_at");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_sdch_user_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureSla(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SlaConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("sla_configurations")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Severity).HasColumnName("severity").HasColumnType("int(11)");
            entity.Property(e => e.MaxTriageDays).HasColumnName("max_triage_days").HasColumnType("int(11)");
            entity.Property(e => e.MaxRemediationDays).HasColumnName("max_remediation_days")
                .HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from").HasColumnType("datetime");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to").HasColumnType("datetime");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            // Resolution reads "policy for this severity, this entity, effective at this date".
            entity.HasIndex(e => new { e.Severity, e.EntityId, e.EffectiveFrom }, "idx_slac_severity_entity_from");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_slac_entity_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_slac_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SlaNotification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("sla_notifications")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.VulnerabilityId).HasColumnName("vulnerability_id").HasColumnType("int(11)");
            entity.Property(e => e.ThresholdDays).HasColumnName("threshold_days").HasColumnType("int(11)");
            entity.Property(e => e.NotifiedAt).HasColumnName("notified_at").HasColumnType("datetime");
            entity.Property(e => e.DueDate).HasColumnName("due_date").HasColumnType("datetime");
            entity.Property(e => e.RecipientUserId).HasColumnName("recipient_user_id").HasColumnType("int(11)");

            // The idempotence guard, enforced by the database: (finding, threshold, due date) can
            // only be notified once. The due date is part of the key so that moving a deadline
            // legitimately re-arms the warning.
            entity.HasIndex(e => new { e.VulnerabilityId, e.ThresholdDays, e.DueDate },
                    "uq_slan_vulnerability_threshold_due")
                .IsUnique();

            entity.HasOne(e => e.Vulnerability)
                .WithMany()
                .HasForeignKey(e => e.VulnerabilityId)
                .HasConstraintName("fk_slan_vulnerability_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RecipientUser)
                .WithMany()
                .HasForeignKey(e => e.RecipientUserId)
                .HasConstraintName("fk_slan_recipient_user_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureApiTokens(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("api_tokens")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.KeyId).HasColumnName("key_id").HasMaxLength(64);
            entity.Property(e => e.SecretHash).HasColumnName("secret_hash").HasMaxLength(255);
            entity.Property(e => e.Scopes).HasColumnName("scopes").HasMaxLength(512);
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedById).HasColumnName("revoked_by_id").HasColumnType("int(11)");
            entity.Property(e => e.RateLimitPerMinute).HasColumnName("rate_limit_per_minute")
                .HasColumnType("int(11)");

            // Authentication is one indexed lookup on the presented key id.
            entity.HasIndex(e => e.KeyId, "uq_api_tokens_key_id").IsUnique();
            entity.HasIndex(e => e.UserId, "idx_api_tokens_user_id");
            entity.HasIndex(e => e.EntityId, "idx_api_tokens_entity_id");

            // Deleting the user a token acts as must take the token with it: a credential that
            // outlives its identity is a credential nobody owns.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_api_tokens_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_api_tokens_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RevokedBy)
                .WithMany()
                .HasForeignKey(e => e.RevokedById)
                .HasConstraintName("fk_api_tokens_revoked_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_api_tokens_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
