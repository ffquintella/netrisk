using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9ThirdParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "third_parties",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    legal_name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tax_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    country = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    website = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_id = table.Column<int>(type: "int(11)", nullable: true),
                    owner_id = table.Column<int>(type: "int(11)", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    is_cloud_provider = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_identity_provider = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    processes_personal_data = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    subprocessors_declared_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    contract_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contract_start = table.Column<DateTime>(type: "datetime", nullable: true),
                    contract_end = table.Column<DateTime>(type: "datetime", nullable: true),
                    sla_availability_percent = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: true),
                    contracted_rto_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    contracted_rpo_minutes = table.Column<int>(type: "int(11)", nullable: true),
                    vulnerability_fix_days = table.Column<int>(type: "int(11)", nullable: true),
                    right_to_audit = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    audit_clause_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    exit_plan = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    exit_plan_reviewed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    exit_plan_tested_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    data_portability = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    terminated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_third_parties_contract_dates", "`contract_start` IS NULL OR `contract_end` IS NULL OR `contract_end` >= `contract_start`");
                    table.CheckConstraint("ck_third_parties_non_negative", "(`contracted_rto_minutes` IS NULL OR `contracted_rto_minutes` >= 0) AND (`contracted_rpo_minutes` IS NULL OR `contracted_rpo_minutes` >= 0) AND (`vulnerability_fix_days` IS NULL OR `vulnerability_fix_days` >= 0)");
                    table.CheckConstraint("ck_third_parties_sla_availability", "`sla_availability_percent` IS NULL OR (`sla_availability_percent` > 0 AND `sla_availability_percent` <= 100)");
                    table.CheckConstraint("ck_third_parties_status", "`status` >= 1 AND `status` <= 4");
                    table.CheckConstraint("ck_third_parties_terminated", "(`status` = 4 AND `terminated_at` IS NOT NULL) OR (`status` <> 4 AND `terminated_at` IS NULL)");
                    table.ForeignKey(
                        name: "fk_third_parties_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_parties_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_parties_owner_id",
                        column: x => x.owner_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_parties_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_assessments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: false),
                    variant = table.Column<int>(type: "int(11)", nullable: false),
                    framework_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expected_question_count = table.Column<int>(type: "int(11)", nullable: false),
                    responded_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    valid_until = table.Column<DateTime>(type: "datetime", nullable: true),
                    evidence_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    answers_updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    answers_updated_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    voided_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    voided_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    void_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_third_party_assessments_expected_question_count", "`expected_question_count` >= 1 AND `expected_question_count` <= 2000");
                    table.CheckConstraint("ck_third_party_assessments_variant", "`variant` >= 1 AND `variant` <= 4");
                    table.CheckConstraint("ck_third_party_assessments_voided", "(`voided_at` IS NULL AND `void_reason` IS NULL) OR (`voided_at` IS NOT NULL AND `void_reason` IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_third_party_assessments_answers_updated_by_id",
                        column: x => x.answers_updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_party_assessments_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_party_assessments_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_third_party_assessments_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_data_locations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: false),
                    country = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    region = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    purpose = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_third_party_data_locations_purpose", "`purpose` >= 1 AND `purpose` <= 4");
                    table.ForeignKey(
                        name: "fk_third_party_data_locations_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_party_data_locations_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_third_party_links_kind", "`kind` >= 1 AND `kind` <= 3");
                    table.ForeignKey(
                        name: "fk_third_party_links_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_party_links_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_third_party_links_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_sboms",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: false),
                    component_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    component_version = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    format = table.Column<int>(type: "int(11)", nullable: false),
                    spec_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    serial_number = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_size_bytes = table.Column<int>(type: "int(11)", nullable: false),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    component_count = table.Column<int>(type: "int(11)", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    uploaded_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_third_party_sboms_counts", "`document_size_bytes` >= 0 AND `component_count` >= 0");
                    table.CheckConstraint("ck_third_party_sboms_format", "`format` >= 1 AND `format` <= 2");
                    table.ForeignKey(
                        name: "fk_third_party_sboms_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_third_party_sboms_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_subprocessors",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subprocessor_third_party_id = table.Column<int>(type: "int(11)", nullable: true),
                    service = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    country = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    processes_personal_data = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_third_party_subprocessors_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_third_party_subprocessors_subprocessor_third_party_id",
                        column: x => x.subprocessor_third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_third_party_subprocessors_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_assessment_answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    assessment_id = table.Column<int>(type: "int(11)", nullable: false),
                    question_id = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    answer = table.Column<int>(type: "int(11)", nullable: false),
                    preferred_answer = table.Column<int>(type: "int(11)", nullable: true),
                    weight = table.Column<int>(type: "int(11)", nullable: false),
                    critical = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_third_party_assessment_answers_answer", "`answer` >= 1 AND `answer` <= 4");
                    table.CheckConstraint("ck_third_party_assessment_answers_preferred_answer", "`preferred_answer` IS NULL OR (`preferred_answer` >= 1 AND `preferred_answer` <= 2)");
                    table.CheckConstraint("ck_third_party_assessment_answers_weight", "`weight` >= 1 AND `weight` <= 100");
                    table.ForeignKey(
                        name: "fk_third_party_assessment_answers_assessment_id",
                        column: x => x.assessment_id,
                        principalTable: "third_party_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "third_party_sbom_components",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    sbom_id = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    purl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    license = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_third_party_sbom_components_sbom_id",
                        column: x => x.sbom_id,
                        principalTable: "third_party_sboms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_third_parties_created_by_id",
                table: "third_parties",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_parties_entity_id",
                table: "third_parties",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_parties_owner_id",
                table: "third_parties",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_parties_status",
                table: "third_parties",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_third_parties_updated_by_id",
                table: "third_parties",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_third_parties_name",
                table: "third_parties",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_third_party_assessment_answers_assessment_id_question_id",
                table: "third_party_assessment_answers",
                columns: new[] { "assessment_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_third_party_assessments_answers_updated_by_id",
                table: "third_party_assessments",
                column: "answers_updated_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_assessments_created_by_id",
                table: "third_party_assessments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_assessments_third_party_id",
                table: "third_party_assessments",
                column: "third_party_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_assessments_voided_by_id",
                table: "third_party_assessments",
                column: "voided_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_data_locations_created_by_id",
                table: "third_party_data_locations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_third_party_data_locations_third_party_id_country_purpose",
                table: "third_party_data_locations",
                columns: new[] { "third_party_id", "country", "purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_third_party_links_created_by_id",
                table: "third_party_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_links_entity_id",
                table: "third_party_links",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_third_party_links_third_party_id_entity_id",
                table: "third_party_links",
                columns: new[] { "third_party_id", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_third_party_sbom_components_name",
                table: "third_party_sbom_components",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_sbom_components_sbom_id",
                table: "third_party_sbom_components",
                column: "sbom_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_sboms_uploaded_by_id",
                table: "third_party_sboms",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_third_party_sboms_third_party_id_document_sha256",
                table: "third_party_sboms",
                columns: new[] { "third_party_id", "document_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_third_party_subprocessors_created_by_id",
                table: "third_party_subprocessors",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_third_party_subprocessors_subprocessor_third_party_id",
                table: "third_party_subprocessors",
                column: "subprocessor_third_party_id");

            migrationBuilder.CreateIndex(
                name: "uq_third_party_subprocessors_third_party_id_name",
                table: "third_party_subprocessors",
                columns: new[] { "third_party_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "third_party_assessment_answers");

            migrationBuilder.DropTable(
                name: "third_party_data_locations");

            migrationBuilder.DropTable(
                name: "third_party_links");

            migrationBuilder.DropTable(
                name: "third_party_sbom_components");

            migrationBuilder.DropTable(
                name: "third_party_subprocessors");

            migrationBuilder.DropTable(
                name: "third_party_assessments");

            migrationBuilder.DropTable(
                name: "third_party_sboms");

            migrationBuilder.DropTable(
                name: "third_parties");
        }
    }
}
