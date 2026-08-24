using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track3AspmSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "component",
                table: "vulnerabilities",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "component_version",
                table: "vulnerabilities",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "cwes",
                table: "vulnerabilities",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "dedup_key",
                table: "vulnerabilities",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "dedup_strategy",
                table: "vulnerabilities",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "duplicate_of_id",
                table: "vulnerabilities",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fixed_in_version",
                table: "vulnerabilities",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "last_import_id",
                table: "vulnerabilities",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "vulnerabilities",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "raw_severity",
                table: "vulnerabilities",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "rule_id",
                table: "vulnerabilities",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "sla_due_date",
                table: "vulnerabilities",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status_id",
                table: "vulnerabilities",
                type: "int(11)",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "tool_unique_id",
                table: "vulnerabilities",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "risk_acceptance_id",
                table: "nr_files",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "api_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_hash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scopes = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    rate_limit_per_minute = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_tokens_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_api_tokens_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_api_tokens_revoked_by_id",
                        column: x => x.revoked_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_api_tokens_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_acceptances",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    business_justification = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authorizing_manager_id = table.Column<int>(type: "int(11)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    compensating_controls = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    residual_score_snapshot = table.Column<double>(type: "double", nullable: true),
                    status_id = table.Column<int>(type: "int(11)", nullable: false, defaultValue: 1),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    revoked_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    revocation_reason = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_warning_days_before = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_ra_authorizing_manager_id",
                        column: x => x.authorizing_manager_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ra_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ra_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ra_revoked_by_id",
                        column: x => x.revoked_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "scan_imports",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    importer = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    job_id = table.Column<int>(type: "int(11)", nullable: true),
                    started_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    finished_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    new_count = table.Column<int>(type: "int(11)", nullable: false),
                    updated_count = table.Column<int>(type: "int(11)", nullable: false),
                    duplicate_count = table.Column<int>(type: "int(11)", nullable: false),
                    closed_count = table.Column<int>(type: "int(11)", nullable: false),
                    skipped_count = table.Column<int>(type: "int(11)", nullable: false),
                    warning_count = table.Column<int>(type: "int(11)", nullable: false),
                    new_by_severity = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    warnings = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error_message = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    idempotency_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_scan_imports_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scan_imports_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "scanner_dedup_configuration_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    importer = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    old_strategy_chain = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_strategy_chain = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    old_hash_fields = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_hash_fields = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    old_auto_close_missing = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    new_auto_close_missing = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_sdch_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "scanner_dedup_configurations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    importer = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strategy_chain = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hash_fields = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    auto_close_missing = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_sdc_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "sla_configurations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    severity = table.Column<int>(type: "int(11)", nullable: false),
                    max_triage_days = table.Column<int>(type: "int(11)", nullable: false),
                    max_remediation_days = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    effective_from = table.Column<DateTime>(type: "datetime", nullable: false),
                    effective_to = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_slac_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_slac_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "sla_notifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    vulnerability_id = table.Column<int>(type: "int(11)", nullable: false),
                    threshold_days = table.Column<int>(type: "int(11)", nullable: false),
                    notified_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    due_date = table.Column<DateTime>(type: "datetime", nullable: false),
                    recipient_user_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_slan_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_slan_vulnerability_id",
                        column: x => x.vulnerability_id,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "finding_status_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    vulnerability_id = table.Column<int>(type: "int(11)", nullable: false),
                    from_status_id = table.Column<int>(type: "int(11)", nullable: true),
                    to_status_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    source = table.Column<int>(type: "int(11)", nullable: false),
                    justification = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    risk_acceptance_id = table.Column<int>(type: "int(11)", nullable: true),
                    duplicate_of_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_fsh_risk_acceptance_id",
                        column: x => x.risk_acceptance_id,
                        principalTable: "risk_acceptances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_fsh_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_fsh_vulnerability_id",
                        column: x => x.vulnerability_id,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_acceptance_findings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_acceptance_id = table.Column<int>(type: "int(11)", nullable: false),
                    vulnerability_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_raf_risk_acceptance_id",
                        column: x => x.risk_acceptance_id,
                        principalTable: "risk_acceptances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_raf_vulnerability_id",
                        column: x => x.vulnerability_id,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_vulnerabilities_dedup_key",
                table: "vulnerabilities",
                column: "dedup_key");

            migrationBuilder.CreateIndex(
                name: "idx_vulnerabilities_duplicate_of_id",
                table: "vulnerabilities",
                column: "duplicate_of_id");

            migrationBuilder.CreateIndex(
                name: "idx_vulnerabilities_sla_due_date",
                table: "vulnerabilities",
                column: "sla_due_date");

            migrationBuilder.CreateIndex(
                name: "idx_vulnerabilities_status_id",
                table: "vulnerabilities",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "idx_files_risk_acceptance_id",
                table: "nr_files",
                column: "risk_acceptance_id");

            migrationBuilder.CreateIndex(
                name: "idx_api_tokens_entity_id",
                table: "api_tokens",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_api_tokens_user_id",
                table: "api_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_tokens_created_by_id",
                table: "api_tokens",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_api_tokens_revoked_by_id",
                table: "api_tokens",
                column: "revoked_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_api_tokens_key_id",
                table: "api_tokens",
                column: "key_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_fsh_risk_acceptance_id",
                table: "finding_status_history",
                column: "risk_acceptance_id");

            migrationBuilder.CreateIndex(
                name: "idx_fsh_user_id",
                table: "finding_status_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_fsh_vulnerability_changed_at",
                table: "finding_status_history",
                columns: new[] { "vulnerability_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "idx_raf_vulnerability_id",
                table: "risk_acceptance_findings",
                column: "vulnerability_id");

            migrationBuilder.CreateIndex(
                name: "uq_raf_acceptance_finding",
                table: "risk_acceptance_findings",
                columns: new[] { "risk_acceptance_id", "vulnerability_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ra_authorizing_manager_id",
                table: "risk_acceptances",
                column: "authorizing_manager_id");

            migrationBuilder.CreateIndex(
                name: "idx_ra_entity_id",
                table: "risk_acceptances",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_ra_status_expires_at",
                table: "risk_acceptances",
                columns: new[] { "status_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_acceptances_created_by_id",
                table: "risk_acceptances",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_acceptances_revoked_by_id",
                table: "risk_acceptances",
                column: "revoked_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_scan_imports_entity_id",
                table: "scan_imports",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_scan_imports_importer_started_at",
                table: "scan_imports",
                columns: new[] { "importer", "started_at" });

            migrationBuilder.CreateIndex(
                name: "idx_scan_imports_job_id",
                table: "scan_imports",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_imports_user_id",
                table: "scan_imports",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_scan_imports_idempotency_key",
                table: "scan_imports",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_sdch_importer_changed_at",
                table: "scanner_dedup_configuration_history",
                columns: new[] { "importer", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_scanner_dedup_configuration_history_user_id",
                table: "scanner_dedup_configuration_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_scanner_dedup_configurations_updated_by_id",
                table: "scanner_dedup_configurations",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_sdc_importer",
                table: "scanner_dedup_configurations",
                column: "importer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_slac_severity_entity_from",
                table: "sla_configurations",
                columns: new[] { "severity", "entity_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_sla_configurations_created_by_id",
                table: "sla_configurations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_sla_configurations_entity_id",
                table: "sla_configurations",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_sla_notifications_recipient_user_id",
                table: "sla_notifications",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_slan_vulnerability_threshold_due",
                table: "sla_notifications",
                columns: new[] { "vulnerability_id", "threshold_days", "due_date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_files_risk_acceptance_id",
                table: "nr_files",
                column: "risk_acceptance_id",
                principalTable: "risk_acceptances",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_vulnerabilities_duplicate_of_id",
                table: "vulnerabilities",
                column: "duplicate_of_id",
                principalTable: "vulnerabilities",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_risk_acceptance_id",
                table: "nr_files");

            migrationBuilder.DropForeignKey(
                name: "fk_vulnerabilities_duplicate_of_id",
                table: "vulnerabilities");

            migrationBuilder.DropTable(
                name: "api_tokens");

            migrationBuilder.DropTable(
                name: "finding_status_history");

            migrationBuilder.DropTable(
                name: "risk_acceptance_findings");

            migrationBuilder.DropTable(
                name: "scan_imports");

            migrationBuilder.DropTable(
                name: "scanner_dedup_configuration_history");

            migrationBuilder.DropTable(
                name: "scanner_dedup_configurations");

            migrationBuilder.DropTable(
                name: "sla_configurations");

            migrationBuilder.DropTable(
                name: "sla_notifications");

            migrationBuilder.DropTable(
                name: "risk_acceptances");

            migrationBuilder.DropIndex(
                name: "idx_vulnerabilities_dedup_key",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "idx_vulnerabilities_duplicate_of_id",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "idx_vulnerabilities_sla_due_date",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "idx_vulnerabilities_status_id",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "idx_files_risk_acceptance_id",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "component",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "component_version",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "cwes",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "dedup_key",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "dedup_strategy",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "duplicate_of_id",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "fixed_in_version",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "last_import_id",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "location",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "raw_severity",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "rule_id",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "sla_due_date",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "status_id",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "tool_unique_id",
                table: "vulnerabilities");

            migrationBuilder.DropColumn(
                name: "risk_acceptance_id",
                table: "nr_files");
        }
    }
}
