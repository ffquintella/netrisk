using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track4IntegrationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "criticality",
                table: "hosts",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "hosts",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "external_provider",
                table: "hosts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "os_version",
                table: "hosts",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "risk_score",
                table: "hosts",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "risk_score_source",
                table: "hosts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "risk_score_updated_at",
                table: "hosts",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "cyber_risk_index",
                table: "entities",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "posture_grade",
                table: "entities",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "posture_source",
                table: "entities",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "posture_updated_at",
                table: "entities",
                type: "datetime",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identity_providers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    protocol = table.Column<int>(type: "int(11)", nullable: false),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    authority = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    client_id = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_client_secret = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scopes = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    metadata_url = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    metadata_xml = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sp_entity_id = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    acs_url = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    require_signed_assertions = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    clock_skew_seconds = table.Column<int>(type: "int(11)", nullable: false),
                    supports_single_logout = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    claim_mapping_json = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    group_mapping_json = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    jit_provisioning = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    default_role_id = table.Column<int>(type: "int(11)", nullable: true),
                    default_entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_providers_default_entity_id",
                        column: x => x.default_entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_identity_providers_default_role_id",
                        column: x => x.default_role_id,
                        principalTable: "role",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "integration_sync_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    integration = table.Column<int>(type: "int(11)", nullable: false),
                    connection_id = table.Column<int>(type: "int(11)", nullable: true),
                    connection_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    finished_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    created_count = table.Column<int>(type: "int(11)", nullable: false),
                    updated_count = table.Column<int>(type: "int(11)", nullable: false),
                    skipped_count = table.Column<int>(type: "int(11)", nullable: false),
                    failed_count = table.Column<int>(type: "int(11)", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error_message = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "issue_tracker_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider = table.Column<int>(type: "int(11)", nullable: false),
                    base_url = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_key = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issue_type = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    auth_user = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_token = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_webhook_secret = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    priority_mapping_json = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title_template = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description_template = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    default_labels = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    auto_create_min_severity = table.Column<int>(type: "int(11)", nullable: true),
                    push_finding_updates = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    poll_interval_minutes = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_tracker_connections_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_issue_tracker_connections_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "mfa_recovery_codes",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    code_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    used_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_recovery_codes_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mfa_recovery_codes_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "notification_channels",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    configuration_json = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secrets_encrypted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    fallback_channel_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_channels_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notification_channels_fallback_channel_id",
                        column: x => x.fallback_channel_id,
                        principalTable: "notification_channels",
                        principalColumn: "id");
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "securityscorecard_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    domain = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    base_url = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_api_token = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sync_interval_hours = table.Column<int>(type: "int(11)", nullable: false),
                    sync_vulnerabilities = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sync_issues = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_sync_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_sync_status = table.Column<int>(type: "int(11)", nullable: true),
                    last_sync_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_securityscorecard_connections_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "trendmicro_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    region = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    base_url = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    encrypted_api_key = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sync_interval_hours = table.Column<int>(type: "int(11)", nullable: false),
                    sync_vulnerabilities = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sync_risk_scores = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    virtual_patch_closes_finding = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    push_exemptions = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_sync_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_sync_status = table.Column<int>(type: "int(11)", nullable: true),
                    last_sync_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_trendmicro_connections_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "webauthn_credentials",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    credential_id = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    public_key = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sign_count = table.Column<long>(type: "bigint(20)", nullable: false),
                    aaguid = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    attestation_format = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_backup_eligible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_backed_up = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_webauthn_credentials_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "scim_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    identity_provider_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_scim_tokens_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scim_tokens_identity_provider_id",
                        column: x => x.identity_provider_id,
                        principalTable: "identity_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scim_tokens_revoked_by_id",
                        column: x => x.revoked_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "finding_issue_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    vulnerability_id = table.Column<int>(type: "int(11)", nullable: false),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    issue_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issue_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issue_url = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_synced_status = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_sync_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    sync_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_change_from_remote = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    has_conflict = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    conflict_detail = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_finding_issue_links_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_finding_issue_links_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_finding_issue_links_vulnerability_id",
                        column: x => x.vulnerability_id,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "issue_status_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    external_status = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    action = table.Column<int>(type: "int(11)", nullable: false),
                    outbound_transition = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_status_mappings_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "notification_subscriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    event_type = table.Column<int>(type: "int(11)", nullable: false),
                    channel_id = table.Column<int>(type: "int(11)", nullable: false),
                    min_severity = table.Column<int>(type: "int(11)", nullable: true),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    digest_window_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_subscriptions_channel_id",
                        column: x => x.channel_id,
                        principalTable: "notification_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_subscriptions_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "security_scorecard_factors",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    factor_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    score = table.Column<int>(type: "int(11)", nullable: false),
                    grade = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issue_count = table.Column<int>(type: "int(11)", nullable: true),
                    is_overall = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    captured_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_ssc_factors_connection_id",
                        column: x => x.connection_id,
                        principalTable: "securityscorecard_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ssc_factors_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "scim_request_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    token_id = table.Column<int>(type: "int(11)", nullable: true),
                    method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    path = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status_code = table.Column<int>(type: "int(11)", nullable: false),
                    target = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    outcome = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_scim_request_logs_token_id",
                        column: x => x.token_id,
                        principalTable: "scim_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    subscription_id = table.Column<int>(type: "int(11)", nullable: true),
                    channel_id = table.Column<int>(type: "int(11)", nullable: true),
                    event_type = table.Column<int>(type: "int(11)", nullable: false),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    attempts = table.Column<int>(type: "int(11)", nullable: false),
                    title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payload_json = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<int>(type: "int(11)", nullable: true),
                    subject_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    digest_due_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_channel_id",
                        column: x => x.channel_id,
                        principalTable: "notification_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "notification_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_hosts_external_provider_id",
                table: "hosts",
                columns: new[] { "external_provider", "external_id" });

            migrationBuilder.CreateIndex(
                name: "idx_hosts_risk_score",
                table: "hosts",
                column: "risk_score");

            migrationBuilder.CreateIndex(
                name: "idx_finding_issue_links_created_by_id",
                table: "finding_issue_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_finding_issue_links_has_conflict",
                table: "finding_issue_links",
                column: "has_conflict");

            migrationBuilder.CreateIndex(
                name: "idx_finding_issue_links_vulnerability_id",
                table: "finding_issue_links",
                column: "vulnerability_id");

            migrationBuilder.CreateIndex(
                name: "uq_finding_issue_links_connection_issue",
                table: "finding_issue_links",
                columns: new[] { "connection_id", "issue_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_identity_providers_default_entity_id",
                table: "identity_providers",
                column: "default_entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_identity_providers_default_role_id",
                table: "identity_providers",
                column: "default_role_id");

            migrationBuilder.CreateIndex(
                name: "idx_identity_providers_protocol_enabled",
                table: "identity_providers",
                columns: new[] { "protocol", "enabled" });

            migrationBuilder.CreateIndex(
                name: "uq_identity_providers_name",
                table: "identity_providers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_integration_sync_logs_integration_started",
                table: "integration_sync_logs",
                columns: new[] { "integration", "started_at" });

            migrationBuilder.CreateIndex(
                name: "uq_issue_status_mappings_connection_status",
                table: "issue_status_mappings",
                columns: new[] { "connection_id", "external_status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_issue_tracker_connections_created_by_id",
                table: "issue_tracker_connections",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_issue_tracker_connections_entity_id",
                table: "issue_tracker_connections",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_issue_tracker_connections_provider_enabled",
                table: "issue_tracker_connections",
                columns: new[] { "provider", "enabled" });

            migrationBuilder.CreateIndex(
                name: "uq_issue_tracker_connections_name",
                table: "issue_tracker_connections",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_mfa_recovery_codes_created_by_id",
                table: "mfa_recovery_codes",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_mfa_recovery_codes_user_used",
                table: "mfa_recovery_codes",
                columns: new[] { "user_id", "used_at" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_channels_created_by_id",
                table: "notification_channels",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_notification_channels_fallback_channel_id",
                table: "notification_channels",
                column: "fallback_channel_id");

            migrationBuilder.CreateIndex(
                name: "uq_notification_channels_name",
                table: "notification_channels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_notification_deliveries_channel_id",
                table: "notification_deliveries",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "idx_notification_deliveries_status_created_at",
                table: "notification_deliveries",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_deliveries_subscription_id",
                table: "notification_deliveries",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "idx_notification_subscriptions_channel_id",
                table: "notification_subscriptions",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "idx_notification_subscriptions_entity_id",
                table: "notification_subscriptions",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_notification_subscriptions_event_enabled",
                table: "notification_subscriptions",
                columns: new[] { "event_type", "enabled" });

            migrationBuilder.CreateIndex(
                name: "idx_scim_request_logs_occurred_at",
                table: "scim_request_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "idx_scim_request_logs_token_id",
                table: "scim_request_logs",
                column: "token_id");

            migrationBuilder.CreateIndex(
                name: "idx_scim_tokens_created_by_id",
                table: "scim_tokens",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_scim_tokens_identity_provider_id",
                table: "scim_tokens",
                column: "identity_provider_id");

            migrationBuilder.CreateIndex(
                name: "idx_scim_tokens_revoked_by_id",
                table: "scim_tokens",
                column: "revoked_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_scim_tokens_key_id",
                table: "scim_tokens",
                column: "key_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ssc_factors_connection_factor_captured",
                table: "security_scorecard_factors",
                columns: new[] { "connection_id", "factor_name", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "idx_ssc_factors_entity_id",
                table: "security_scorecard_factors",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_securityscorecard_connections_entity_id",
                table: "securityscorecard_connections",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_securityscorecard_connections_name",
                table: "securityscorecard_connections",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_trendmicro_connections_entity_id",
                table: "trendmicro_connections",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_trendmicro_connections_name",
                table: "trendmicro_connections",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_webauthn_credentials_user_id",
                table: "webauthn_credentials",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_webauthn_credentials_credential_id",
                table: "webauthn_credentials",
                column: "credential_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finding_issue_links");

            migrationBuilder.DropTable(
                name: "integration_sync_logs");

            migrationBuilder.DropTable(
                name: "issue_status_mappings");

            migrationBuilder.DropTable(
                name: "mfa_recovery_codes");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "scim_request_logs");

            migrationBuilder.DropTable(
                name: "security_scorecard_factors");

            migrationBuilder.DropTable(
                name: "trendmicro_connections");

            migrationBuilder.DropTable(
                name: "webauthn_credentials");

            migrationBuilder.DropTable(
                name: "issue_tracker_connections");

            migrationBuilder.DropTable(
                name: "notification_subscriptions");

            migrationBuilder.DropTable(
                name: "scim_tokens");

            migrationBuilder.DropTable(
                name: "securityscorecard_connections");

            migrationBuilder.DropTable(
                name: "notification_channels");

            migrationBuilder.DropTable(
                name: "identity_providers");

            migrationBuilder.DropIndex(
                name: "idx_hosts_external_provider_id",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "idx_hosts_risk_score",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "criticality",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "external_provider",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "os_version",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "risk_score",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "risk_score_source",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "risk_score_updated_at",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "cyber_risk_index",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "posture_grade",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "posture_source",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "posture_updated_at",
                table: "entities");
        }
    }
}
