using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track46JiraServiceManagementAndAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "environment",
                table: "hosts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "owner",
                table: "hosts",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "vulnerability_id",
                table: "finding_issue_links",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AddColumn<int>(
                name: "incident_id",
                table: "finding_issue_links",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "risk_id",
                table: "finding_issue_links",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_kind",
                table: "finding_issue_links",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "jira_asset_objects",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    object_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    object_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    object_type_id = table.Column<int>(type: "int(11)", nullable: true),
                    object_type_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mapped_name = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mapped_owner = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mapped_environment = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mapped_active = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    attributes_json = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_kind = table.Column<int>(type: "int(11)", nullable: false),
                    target_host_id = table.Column<int>(type: "int(11)", nullable: true),
                    target_entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    match_reason = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_remote = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_at_remote = table.Column<DateTime>(type: "datetime", nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    import_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_asset_objects_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_jira_asset_objects_target_entity_id",
                        column: x => x.target_entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_jira_asset_objects_target_host_id",
                        column: x => x.target_host_id,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_connection_settings",
                columns: table => new
                {
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    deployment = table.Column<int>(type: "int(11)", nullable: false),
                    jsm_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    service_desk_id = table.Column<int>(type: "int(11)", nullable: true),
                    service_desk_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    request_type_filter = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    import_slas = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    sla_breach_notifications = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    default_link_target_kind = table.Column<int>(type: "int(11)", nullable: false),
                    last_jsm_sync_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    assets_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    assets_workspace_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assets_schema_id = table.Column<int>(type: "int(11)", nullable: true),
                    assets_schema_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_assets_sync_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.connection_id);
                    table.ForeignKey(
                        name: "fk_jira_connection_settings_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_field_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    direction = table.Column<int>(type: "int(11)", nullable: false),
                    netrisk_field = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    jira_field_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    jira_field_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    jira_field_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transform = table.Column<int>(type: "int(11)", nullable: false),
                    constant_value = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_field_mappings_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_object_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    object_type_id = table.Column<int>(type: "int(11)", nullable: false),
                    object_type_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_kind = table.Column<int>(type: "int(11)", nullable: false),
                    aql_filter = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    match_strategy = table.Column<int>(type: "int(11)", nullable: false),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    create_missing = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    update_existing = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    deactivate_missing = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_imported_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_object_mappings_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_jira_object_mappings_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_service_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    issue_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issue_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    service_desk_id = table.Column<int>(type: "int(11)", nullable: true),
                    request_type_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    request_type_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    summary = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status_category = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reporter_account_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reporter_display_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    organization_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    priority_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assignee_display_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_remote = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_at_remote = table.Column<DateTime>(type: "datetime", nullable: true),
                    is_closed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    request_url = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    first_seen_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    sync_error = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_service_requests_connection_id",
                        column: x => x.connection_id,
                        principalTable: "issue_tracker_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_queue_imports",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    connection_id = table.Column<int>(type: "int(11)", nullable: false),
                    service_desk_id = table.Column<int>(type: "int(11)", nullable: false),
                    queue_id = table.Column<int>(type: "int(11)", nullable: false),
                    queue_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    link_target_kind = table.Column<int>(type: "int(11)", nullable: true),
                    max_requests = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_queue_imports_connection_id",
                        column: x => x.connection_id,
                        principalTable: "jira_connection_settings",
                        principalColumn: "connection_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_object_attribute_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    mapping_id = table.Column<int>(type: "int(11)", nullable: false),
                    source_attribute_id = table.Column<int>(type: "int(11)", nullable: true),
                    source_attribute_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_field = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transform = table.Column<int>(type: "int(11)", nullable: false),
                    is_identity = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    constant_value = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_object_attribute_mappings_mapping_id",
                        column: x => x.mapping_id,
                        principalTable: "jira_object_mappings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "jira_request_slas",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    request_id = table.Column<int>(type: "int(11)", nullable: false),
                    metric_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    metric_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_ongoing = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    breached = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    paused = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    goal_duration_ms = table.Column<long>(type: "bigint(20)", nullable: true),
                    elapsed_ms = table.Column<long>(type: "bigint(20)", nullable: true),
                    remaining_ms = table.Column<long>(type: "bigint(20)", nullable: true),
                    cycle_start_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    cycle_stop_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    captured_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_jira_request_slas_request_id",
                        column: x => x.request_id,
                        principalTable: "jira_service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_finding_issue_links_incident_id",
                table: "finding_issue_links",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "idx_finding_issue_links_risk_id",
                table: "finding_issue_links",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "idx_jira_asset_objects_target_entity_id",
                table: "jira_asset_objects",
                column: "target_entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_jira_asset_objects_target_host_id",
                table: "jira_asset_objects",
                column: "target_host_id");

            migrationBuilder.CreateIndex(
                name: "uq_jira_asset_objects_connection_object",
                table: "jira_asset_objects",
                columns: new[] { "connection_id", "object_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_jira_field_mappings_connection_direction_field",
                table: "jira_field_mappings",
                columns: new[] { "connection_id", "direction", "jira_field_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_jira_object_attribute_mappings_mapping_target",
                table: "jira_object_attribute_mappings",
                columns: new[] { "mapping_id", "target_field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_jira_object_mappings_created_by_id",
                table: "jira_object_mappings",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_jira_object_mappings_connection_object_type",
                table: "jira_object_mappings",
                columns: new[] { "connection_id", "object_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_jira_queue_imports_connection_queue",
                table: "jira_queue_imports",
                columns: new[] { "connection_id", "queue_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_jira_request_slas_breached_ongoing",
                table: "jira_request_slas",
                columns: new[] { "breached", "is_ongoing" });

            migrationBuilder.CreateIndex(
                name: "uq_jira_request_slas_request_metric_cycle",
                table: "jira_request_slas",
                columns: new[] { "request_id", "metric_name", "cycle_start_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_jira_service_requests_connection_closed",
                table: "jira_service_requests",
                columns: new[] { "connection_id", "is_closed" });

            migrationBuilder.CreateIndex(
                name: "idx_jira_service_requests_updated_at_remote",
                table: "jira_service_requests",
                column: "updated_at_remote");

            migrationBuilder.CreateIndex(
                name: "uq_jira_service_requests_connection_key",
                table: "jira_service_requests",
                columns: new[] { "connection_id", "issue_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_finding_issue_links_incident_id",
                table: "finding_issue_links",
                column: "incident_id",
                principalTable: "incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_finding_issue_links_risk_id",
                table: "finding_issue_links",
                column: "risk_id",
                principalTable: "risks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_finding_issue_links_incident_id",
                table: "finding_issue_links");

            migrationBuilder.DropForeignKey(
                name: "fk_finding_issue_links_risk_id",
                table: "finding_issue_links");

            migrationBuilder.DropTable(
                name: "jira_asset_objects");

            migrationBuilder.DropTable(
                name: "jira_field_mappings");

            migrationBuilder.DropTable(
                name: "jira_object_attribute_mappings");

            migrationBuilder.DropTable(
                name: "jira_queue_imports");

            migrationBuilder.DropTable(
                name: "jira_request_slas");

            migrationBuilder.DropTable(
                name: "jira_object_mappings");

            migrationBuilder.DropTable(
                name: "jira_connection_settings");

            migrationBuilder.DropTable(
                name: "jira_service_requests");

            migrationBuilder.DropIndex(
                name: "idx_finding_issue_links_incident_id",
                table: "finding_issue_links");

            migrationBuilder.DropIndex(
                name: "idx_finding_issue_links_risk_id",
                table: "finding_issue_links");

            migrationBuilder.DropColumn(
                name: "environment",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "owner",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "incident_id",
                table: "finding_issue_links");

            migrationBuilder.DropColumn(
                name: "risk_id",
                table: "finding_issue_links");

            migrationBuilder.DropColumn(
                name: "target_kind",
                table: "finding_issue_links");

            migrationBuilder.AlterColumn<int>(
                name: "vulnerability_id",
                table: "finding_issue_links",
                type: "int(11)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldNullable: true);
        }
    }
}
