using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Track 4 (Integrations &amp; Notification Channels) schema: notification channels, subscriptions
/// and the delivery log (4.1); issue-tracker connections, status mappings and finding↔issue links
/// (4.2); identity providers, SCIM tokens and audit, WebAuthn credentials and recovery codes (4.3);
/// Trend Micro Vision One (4.4) and SecurityScorecard (4.5) connections and factor history; plus the
/// shared integration sync log and the posture columns on <c>hosts</c> and <c>entities</c>.
///
/// Configured in this partial rather than the generated <c>OnModelCreating</c> so that file stays
/// regenerable, and named per the Track 6 convention throughout — snake_case columns via
/// <c>HasColumnName</c>, <c>fk_</c>/<c>idx_</c>/<c>uq_</c> constraint prefixes, int-backed enums with
/// explicit conversions, <c>tinyint(1)</c> booleans, UTC <c>datetime</c> temporal columns, and
/// <c>varchar(n)</c>/<c>text</c> for strings. New schema is born compliant rather than added to the
/// drift.
/// </summary>
public partial class NRDbContext
{
    public virtual DbSet<NotificationChannel> NotificationChannels { get; set; } = null!;

    public virtual DbSet<NotificationSubscription> NotificationSubscriptions { get; set; } = null!;

    public virtual DbSet<NotificationDelivery> NotificationDeliveries { get; set; } = null!;

    public virtual DbSet<IssueTrackerConnection> IssueTrackerConnections { get; set; } = null!;

    public virtual DbSet<IssueStatusMapping> IssueStatusMappings { get; set; } = null!;

    public virtual DbSet<FindingIssueLink> FindingIssueLinks { get; set; } = null!;

    public virtual DbSet<IdentityProvider> IdentityProviders { get; set; } = null!;

    public virtual DbSet<ScimToken> ScimTokens { get; set; } = null!;

    public virtual DbSet<ScimRequestLog> ScimRequestLogs { get; set; } = null!;

    public virtual DbSet<WebAuthnCredential> WebAuthnCredentials { get; set; } = null!;

    public virtual DbSet<MfaRecoveryCode> MfaRecoveryCodes { get; set; } = null!;

    public virtual DbSet<TrendMicroConnection> TrendMicroConnections { get; set; } = null!;

    public virtual DbSet<SecurityScorecardConnection> SecurityScorecardConnections { get; set; } = null!;

    public virtual DbSet<SecurityScorecardFactor> SecurityScorecardFactors { get; set; } = null!;

    public virtual DbSet<IntegrationSyncLog> IntegrationSyncLogs { get; set; } = null!;

    private static void ConfigureIntegrations(ModelBuilder modelBuilder)
    {
        ConfigureNotificationChannels(modelBuilder);
        ConfigureNotificationSubscriptions(modelBuilder);
        ConfigureNotificationDeliveries(modelBuilder);
        ConfigureIssueTrackerConnections(modelBuilder);
        ConfigureIssueStatusMappings(modelBuilder);
        ConfigureFindingIssueLinks(modelBuilder);
        ConfigureIdentityProviders(modelBuilder);
        ConfigureScim(modelBuilder);
        ConfigureWebAuthn(modelBuilder);
        ConfigureTrendMicro(modelBuilder);
        ConfigureSecurityScorecard(modelBuilder);
        ConfigureIntegrationSyncLogs(modelBuilder);
        ConfigureHostIntegrationColumns(modelBuilder);
        ConfigureEntityPostureColumns(modelBuilder);
    }

    private static void ConfigureNotificationChannels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationChannel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("notification_channels")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.ConfigurationJson).HasColumnName("configuration_json")
                .HasColumnType("text");
            entity.Property(e => e.SecretsEncrypted).HasColumnName("secrets_encrypted")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.FallbackChannelId).HasColumnName("fallback_channel_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            // Two channels may not share a name: the subscription matrix identifies a channel by its
            // label, and two "SOC Slack" rows make it impossible to tell which one is misconfigured.
            entity.HasIndex(e => e.Name, "uq_notification_channels_name").IsUnique();
            entity.HasIndex(e => e.FallbackChannelId, "idx_notification_channels_fallback_channel_id");
            // Named explicitly so the FK index follows the Track 6 `idx_` convention instead of
            // EF's generated `IX_` name.
            entity.HasIndex(e => e.CreatedById, "idx_notification_channels_created_by_id");

            // Restrict, not cascade: deleting the email channel that a Slack channel falls back to
            // must fail loudly rather than quietly removing the fallback (or the Slack channel with
            // it). An operator who really wants it gone edits the fallback first.
            entity.HasOne(e => e.FallbackChannel)
                .WithMany()
                .HasForeignKey(e => e.FallbackChannelId)
                .HasConstraintName("fk_notification_channels_fallback_channel_id")
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_notification_channels_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureNotificationSubscriptions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationSubscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("notification_subscriptions")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.EventType).HasColumnName("event_type").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("int(11)");
            entity.Property(e => e.MinSeverity).HasColumnName("min_severity").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.DigestWindowMinutes).HasColumnName("digest_window_minutes")
                .HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            // The dispatcher's hot query is "enabled subscriptions for this event", so the index
            // leads with the event type.
            entity.HasIndex(e => new { e.EventType, e.Enabled }, "idx_notification_subscriptions_event_enabled");
            entity.HasIndex(e => e.ChannelId, "idx_notification_subscriptions_channel_id");
            entity.HasIndex(e => e.EntityId, "idx_notification_subscriptions_entity_id");

            // Cascade: a subscription without its channel cannot deliver anything, so it is not a
            // row worth keeping.
            entity.HasOne(e => e.Channel)
                .WithMany(c => c.Subscriptions)
                .HasForeignKey(e => e.ChannelId)
                .HasConstraintName("fk_notification_subscriptions_channel_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_notification_subscriptions_entity_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureNotificationDeliveries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("notification_deliveries")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id").HasColumnType("int(11)");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("int(11)");
            entity.Property(e => e.EventType).HasColumnName("event_type").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Attempts).HasColumnName("attempts").HasColumnType("int(11)");
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(512);
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("longtext");
            entity.Property(e => e.LastError).HasColumnName("last_error").HasColumnType("text");
            entity.Property(e => e.Severity).HasColumnName("severity").HasColumnType("int(11)");
            entity.Property(e => e.SubjectType).HasColumnName("subject_type").HasMaxLength(64);
            entity.Property(e => e.SubjectId).HasColumnName("subject_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.LastAttemptAt).HasColumnName("last_attempt_at").HasColumnType("datetime");
            entity.Property(e => e.DeliveredAt).HasColumnName("delivered_at").HasColumnType("datetime");
            entity.Property(e => e.DigestDueAt).HasColumnName("digest_due_at").HasColumnType("datetime");

            // The retry job's query is "pending or retrying, oldest first".
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "idx_notification_deliveries_status_created_at");
            entity.HasIndex(e => e.SubscriptionId, "idx_notification_deliveries_subscription_id");
            entity.HasIndex(e => e.ChannelId, "idx_notification_deliveries_channel_id");

            // SetNull rather than cascade: deleting a misconfigured subscription must not erase the
            // evidence that it failed to deliver anything for a month.
            entity.HasOne(e => e.Subscription)
                .WithMany()
                .HasForeignKey(e => e.SubscriptionId)
                .HasConstraintName("fk_notification_deliveries_subscription_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Channel)
                .WithMany()
                .HasForeignKey(e => e.ChannelId)
                .HasConstraintName("fk_notification_deliveries_channel_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureIssueTrackerConnections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IssueTrackerConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("issue_tracker_connections")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Provider).HasColumnName("provider").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.BaseUrl).HasColumnName("base_url").HasMaxLength(512);
            entity.Property(e => e.ProjectKey).HasColumnName("project_key").HasMaxLength(255);
            entity.Property(e => e.IssueType).HasColumnName("issue_type").HasMaxLength(128);
            entity.Property(e => e.AuthUser).HasColumnName("auth_user").HasMaxLength(255);
            entity.Property(e => e.EncryptedToken).HasColumnName("encrypted_token").HasColumnType("text");
            entity.Property(e => e.EncryptedWebhookSecret).HasColumnName("encrypted_webhook_secret")
                .HasColumnType("text");
            entity.Property(e => e.PriorityMappingJson).HasColumnName("priority_mapping_json")
                .HasColumnType("text");
            entity.Property(e => e.TitleTemplate).HasColumnName("title_template").HasColumnType("text");
            entity.Property(e => e.DescriptionTemplate).HasColumnName("description_template")
                .HasColumnType("text");
            entity.Property(e => e.DefaultLabels).HasColumnName("default_labels").HasMaxLength(512);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.AutoCreateMinSeverity).HasColumnName("auto_create_min_severity")
                .HasColumnType("int(11)");
            entity.Property(e => e.PushFindingUpdates).HasColumnName("push_finding_updates")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.PollIntervalMinutes).HasColumnName("poll_interval_minutes")
                .HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.Name, "uq_issue_tracker_connections_name").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_issue_tracker_connections_entity_id");
            entity.HasIndex(e => new { e.Provider, e.Enabled }, "idx_issue_tracker_connections_provider_enabled");
            entity.HasIndex(e => e.CreatedById, "idx_issue_tracker_connections_created_by_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_issue_tracker_connections_entity_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_issue_tracker_connections_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureIssueStatusMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IssueStatusMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("issue_status_mappings")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ExternalStatus).HasColumnName("external_status").HasMaxLength(128);
            entity.Property(e => e.Action).HasColumnName("action").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.OutboundTransition).HasColumnName("outbound_transition").HasMaxLength(128);

            // One action per external status per connection. Two rows mapping "Done" to different
            // actions is a configuration whose behaviour depends on row order.
            entity.HasIndex(e => new { e.ConnectionId, e.ExternalStatus },
                "uq_issue_status_mappings_connection_status").IsUnique();

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.StatusMappings)
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_issue_status_mappings_connection_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureFindingIssueLinks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FindingIssueLink>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("finding_issue_links")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.VulnerabilityId).HasColumnName("vulnerability_id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.IssueKey).HasColumnName("issue_key").HasMaxLength(128);
            entity.Property(e => e.IssueId).HasColumnName("issue_id").HasMaxLength(128);
            entity.Property(e => e.IssueUrl).HasColumnName("issue_url").HasMaxLength(1024);
            entity.Property(e => e.LastSyncedStatus).HasColumnName("last_synced_status").HasMaxLength(128);
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at").HasColumnType("datetime");
            entity.Property(e => e.SyncError).HasColumnName("sync_error").HasColumnType("text");
            entity.Property(e => e.LastChangeFromRemote).HasColumnName("last_change_from_remote")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.HasConflict).HasColumnName("has_conflict").HasColumnType("tinyint(1)");
            entity.Property(e => e.ConflictDetail).HasColumnName("conflict_detail").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");

            // One link per (connection, issue): re-running "create issue" for the same finding must
            // not produce two links to the same ticket, and the inbound webhook looks a link up by
            // exactly this pair.
            entity.HasIndex(e => new { e.ConnectionId, e.IssueKey }, "uq_finding_issue_links_connection_issue")
                .IsUnique();
            entity.HasIndex(e => e.VulnerabilityId, "idx_finding_issue_links_vulnerability_id");
            entity.HasIndex(e => e.HasConflict, "idx_finding_issue_links_has_conflict");
            entity.HasIndex(e => e.CreatedById, "idx_finding_issue_links_created_by_id");

            entity.HasOne(e => e.Vulnerability)
                .WithMany()
                .HasForeignKey(e => e.VulnerabilityId)
                .HasConstraintName("fk_finding_issue_links_vulnerability_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.Links)
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_finding_issue_links_connection_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_finding_issue_links_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureIdentityProviders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityProvider>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("identity_providers")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Protocol).HasColumnName("protocol").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.Authority).HasColumnName("authority").HasMaxLength(512);
            entity.Property(e => e.ClientId).HasColumnName("client_id").HasMaxLength(255);
            entity.Property(e => e.EncryptedClientSecret).HasColumnName("encrypted_client_secret")
                .HasColumnType("text");
            entity.Property(e => e.Scopes).HasColumnName("scopes").HasMaxLength(512);
            entity.Property(e => e.MetadataUrl).HasColumnName("metadata_url").HasMaxLength(512);
            entity.Property(e => e.MetadataXml).HasColumnName("metadata_xml").HasColumnType("longtext");
            entity.Property(e => e.EntityIdValue).HasColumnName("sp_entity_id").HasMaxLength(512);
            entity.Property(e => e.AssertionConsumerServiceUrl).HasColumnName("acs_url").HasMaxLength(512);
            entity.Property(e => e.RequireSignedAssertions).HasColumnName("require_signed_assertions")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.ClockSkewSeconds).HasColumnName("clock_skew_seconds").HasColumnType("int(11)");
            entity.Property(e => e.SupportsSingleLogout).HasColumnName("supports_single_logout")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.ClaimMappingJson).HasColumnName("claim_mapping_json").HasColumnType("text");
            entity.Property(e => e.GroupMappingJson).HasColumnName("group_mapping_json").HasColumnType("text");
            entity.Property(e => e.JitProvisioning).HasColumnName("jit_provisioning").HasColumnType("tinyint(1)");
            entity.Property(e => e.DefaultRoleId).HasColumnName("default_role_id").HasColumnType("int(11)");
            entity.Property(e => e.DefaultEntityId).HasColumnName("default_entity_id").HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.Name, "uq_identity_providers_name").IsUnique();
            entity.HasIndex(e => new { e.Protocol, e.Enabled }, "idx_identity_providers_protocol_enabled");
            entity.HasIndex(e => e.DefaultRoleId, "idx_identity_providers_default_role_id");
            entity.HasIndex(e => e.DefaultEntityId, "idx_identity_providers_default_entity_id");

            entity.HasOne(e => e.DefaultRole)
                .WithMany()
                .HasForeignKey(e => e.DefaultRoleId)
                .HasConstraintName("fk_identity_providers_default_role_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.DefaultEntity)
                .WithMany()
                .HasForeignKey(e => e.DefaultEntityId)
                .HasConstraintName("fk_identity_providers_default_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureScim(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScimToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("scim_tokens")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.KeyId).HasColumnName("key_id").HasMaxLength(64);
            entity.Property(e => e.SecretHash).HasColumnName("secret_hash").HasMaxLength(128);
            entity.Property(e => e.IdentityProviderId).HasColumnName("identity_provider_id")
                .HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedById).HasColumnName("revoked_by_id").HasColumnType("int(11)");

            entity.HasIndex(e => e.KeyId, "uq_scim_tokens_key_id").IsUnique();
            entity.HasIndex(e => e.IdentityProviderId, "idx_scim_tokens_identity_provider_id");
            entity.HasIndex(e => e.CreatedById, "idx_scim_tokens_created_by_id");
            entity.HasIndex(e => e.RevokedById, "idx_scim_tokens_revoked_by_id");

            entity.HasOne(e => e.IdentityProvider)
                .WithMany()
                .HasForeignKey(e => e.IdentityProviderId)
                .HasConstraintName("fk_scim_tokens_identity_provider_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_scim_tokens_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RevokedBy)
                .WithMany()
                .HasForeignKey(e => e.RevokedById)
                .HasConstraintName("fk_scim_tokens_revoked_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ScimRequestLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("scim_request_logs")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.TokenId).HasColumnName("token_id").HasColumnType("int(11)");
            entity.Property(e => e.Method).HasColumnName("method").HasMaxLength(16);
            entity.Property(e => e.Path).HasColumnName("path").HasMaxLength(512);
            entity.Property(e => e.StatusCode).HasColumnName("status_code").HasColumnType("int(11)");
            entity.Property(e => e.Target).HasColumnName("target").HasMaxLength(255);
            entity.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(512);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime");

            entity.HasIndex(e => e.OccurredAt, "idx_scim_request_logs_occurred_at");
            entity.HasIndex(e => e.TokenId, "idx_scim_request_logs_token_id");

            // SetNull: revoking and deleting a provisioning token must not delete the record of what
            // it did.
            entity.HasOne(e => e.Token)
                .WithMany()
                .HasForeignKey(e => e.TokenId)
                .HasConstraintName("fk_scim_request_logs_token_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureWebAuthn(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebAuthnCredential>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("webauthn_credentials")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.CredentialId).HasColumnName("credential_id").HasMaxLength(512);
            entity.Property(e => e.PublicKey).HasColumnName("public_key").HasColumnType("text");
            entity.Property(e => e.SignCount).HasColumnName("sign_count").HasColumnType("bigint(20)");
            entity.Property(e => e.AaGuid).HasColumnName("aaguid").HasMaxLength(64);
            entity.Property(e => e.AttestationFormat).HasColumnName("attestation_format").HasMaxLength(64);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.IsBackupEligible).HasColumnName("is_backup_eligible")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.IsBackedUp).HasColumnName("is_backed_up").HasColumnType("tinyint(1)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("datetime");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime");

            // The spec requires credential ids to be globally unique; the index enforces it rather
            // than trusting every caller to check first. 512 chars is longer than any authenticator
            // produces, and a prefix length is what MySQL would otherwise need for a longer column.
            entity.HasIndex(e => e.CredentialId, "uq_webauthn_credentials_credential_id").IsUnique();
            entity.HasIndex(e => e.UserId, "idx_webauthn_credentials_user_id");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_webauthn_credentials_user_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MfaRecoveryCode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("mfa_recovery_codes")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("int(11)");
            entity.Property(e => e.CodeHash).HasColumnName("code_hash").HasMaxLength(128);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id").HasColumnType("int(11)");
            entity.Property(e => e.UsedAt).HasColumnName("used_at").HasColumnType("datetime");

            entity.HasIndex(e => new { e.UserId, e.UsedAt }, "idx_mfa_recovery_codes_user_used");
            entity.HasIndex(e => e.CreatedById, "idx_mfa_recovery_codes_created_by_id");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_mfa_recovery_codes_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .HasConstraintName("fk_mfa_recovery_codes_created_by_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureTrendMicro(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrendMicroConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("trendmicro_connections")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Region).HasColumnName("region").HasMaxLength(32);
            entity.Property(e => e.BaseUrl).HasColumnName("base_url").HasMaxLength(512);
            entity.Property(e => e.EncryptedApiKey).HasColumnName("encrypted_api_key").HasColumnType("text");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncIntervalHours).HasColumnName("sync_interval_hours").HasColumnType("int(11)");
            entity.Property(e => e.SyncVulnerabilities).HasColumnName("sync_vulnerabilities")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncRiskScores).HasColumnName("sync_risk_scores").HasColumnType("tinyint(1)");
            entity.Property(e => e.VirtualPatchClosesFinding).HasColumnName("virtual_patch_closes_finding")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.PushExemptions).HasColumnName("push_exemptions").HasColumnType("tinyint(1)");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at").HasColumnType("datetime");
            entity.Property(e => e.LastSyncStatus).HasColumnName("last_sync_status").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.LastSyncError).HasColumnName("last_sync_error").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.Name, "uq_trendmicro_connections_name").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_trendmicro_connections_entity_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_trendmicro_connections_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureSecurityScorecard(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecurityScorecardConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("securityscorecard_connections")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Domain).HasColumnName("domain").HasMaxLength(255);
            entity.Property(e => e.BaseUrl).HasColumnName("base_url").HasMaxLength(512);
            entity.Property(e => e.EncryptedApiToken).HasColumnName("encrypted_api_token").HasColumnType("text");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncIntervalHours).HasColumnName("sync_interval_hours").HasColumnType("int(11)");
            entity.Property(e => e.SyncVulnerabilities).HasColumnName("sync_vulnerabilities")
                .HasColumnType("tinyint(1)");
            entity.Property(e => e.SyncIssues).HasColumnName("sync_issues").HasColumnType("tinyint(1)");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at").HasColumnType("datetime");
            entity.Property(e => e.LastSyncStatus).HasColumnName("last_sync_status").HasColumnType("int(11)")
                .HasConversion<int?>();
            entity.Property(e => e.LastSyncError).HasColumnName("last_sync_error").HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime");

            entity.HasIndex(e => e.Name, "uq_securityscorecard_connections_name").IsUnique();
            entity.HasIndex(e => e.EntityId, "idx_securityscorecard_connections_entity_id");

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_securityscorecard_connections_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SecurityScorecardFactor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("security_scorecard_factors")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasColumnType("int(11)");
            entity.Property(e => e.FactorName).HasColumnName("factor_name").HasMaxLength(128);
            entity.Property(e => e.Score).HasColumnName("score").HasColumnType("int(11)");
            entity.Property(e => e.Grade).HasColumnName("grade").HasMaxLength(8);
            entity.Property(e => e.IssueCount).HasColumnName("issue_count").HasColumnType("int(11)");
            entity.Property(e => e.IsOverall).HasColumnName("is_overall").HasColumnType("tinyint(1)");
            entity.Property(e => e.CapturedAt).HasColumnName("captured_at").HasColumnType("datetime");

            // The trend chart reads "this connection's factor over time", so the index leads with the
            // connection and the factor and ends with the capture date.
            entity.HasIndex(e => new { e.ConnectionId, e.FactorName, e.CapturedAt },
                "idx_ssc_factors_connection_factor_captured");
            entity.HasIndex(e => e.EntityId, "idx_ssc_factors_entity_id");

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.Factors)
                .HasForeignKey(e => e.ConnectionId)
                .HasConstraintName("fk_ssc_factors_connection_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Entity)
                .WithMany()
                .HasForeignKey(e => e.EntityId)
                .HasConstraintName("fk_ssc_factors_entity_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureIntegrationSyncLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationSyncLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("integration_sync_logs")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("int(11)");
            entity.Property(e => e.Integration).HasColumnName("integration").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id").HasColumnType("int(11)");
            entity.Property(e => e.ConnectionName).HasColumnName("connection_name").HasMaxLength(255);
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("datetime");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at").HasColumnType("datetime");
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("int(11)")
                .HasConversion<int>();
            entity.Property(e => e.CreatedCount).HasColumnName("created_count").HasColumnType("int(11)");
            entity.Property(e => e.UpdatedCount).HasColumnName("updated_count").HasColumnType("int(11)");
            entity.Property(e => e.SkippedCount).HasColumnName("skipped_count").HasColumnType("int(11)");
            entity.Property(e => e.FailedCount).HasColumnName("failed_count").HasColumnType("int(11)");
            entity.Property(e => e.Summary).HasColumnName("summary").HasColumnType("text");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("text");

            entity.HasIndex(e => new { e.Integration, e.StartedAt }, "idx_integration_sync_logs_integration_started");
        });
    }

    private static void ConfigureHostIntegrationColumns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Host>(entity =>
        {
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(255);
            entity.Property(e => e.ExternalProvider).HasColumnName("external_provider").HasMaxLength(64);
            entity.Property(e => e.OsVersion).HasColumnName("os_version").HasMaxLength(255);
            entity.Property(e => e.Criticality).HasColumnName("criticality").HasColumnType("int(11)");
            entity.Property(e => e.RiskScore).HasColumnName("risk_score").HasColumnType("int(11)");
            entity.Property(e => e.RiskScoreSource).HasColumnName("risk_score_source").HasMaxLength(64);
            entity.Property(e => e.RiskScoreUpdatedAt).HasColumnName("risk_score_updated_at")
                .HasColumnType("datetime");

            // The inventory sync's lookup is "this provider's asset with this external id", which is
            // one indexed read per device rather than a scan of the host table per device.
            entity.HasIndex(e => new { e.ExternalProvider, e.ExternalId }, "idx_hosts_external_provider_id");
            entity.HasIndex(e => e.RiskScore, "idx_hosts_risk_score");
        });
    }

    private static void ConfigureEntityPostureColumns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entity>(entity =>
        {
            entity.Property(e => e.CyberRiskIndex).HasColumnName("cyber_risk_index");
            entity.Property(e => e.PostureGrade).HasColumnName("posture_grade").HasMaxLength(8);
            entity.Property(e => e.PostureSource).HasColumnName("posture_source").HasMaxLength(64);
            entity.Property(e => e.PostureUpdatedAt).HasColumnName("posture_updated_at")
                .HasColumnType("datetime");
        });
    }
}
