using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track9DataCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dpias",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int(11)", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    residual_risk = table.Column<int>(type: "int(11)", nullable: true),
                    performed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    next_review_due_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    approved_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    retired_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    retired_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    retire_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_dpias_approved", "`status` <> 2 OR `approved_at` IS NOT NULL");
                    table.CheckConstraint("ck_dpias_residual_risk", "`residual_risk` IS NULL OR (`residual_risk` >= 1 AND `residual_risk` <= 3)");
                    table.CheckConstraint("ck_dpias_retired", "(`status` = 3 AND `retired_at` IS NOT NULL AND `retire_reason` IS NOT NULL) OR (`status` <> 3 AND `retired_at` IS NULL)");
                    table.CheckConstraint("ck_dpias_status", "`status` >= 1 AND `status` <= 3");
                    table.ForeignKey(
                        name: "fk_dpias_approved_by_id",
                        column: x => x.approved_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dpias_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dpias_retired_by_id",
                        column: x => x.retired_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dpias_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "legal_requirements",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    third_party_id = table.Column<int>(type: "int(11)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_legal_requirements_kind", "`kind` >= 1 AND `kind` <= 4");
                    table.ForeignKey(
                        name: "fk_legal_requirements_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_legal_requirements_third_party_id",
                        column: x => x.third_party_id,
                        principalTable: "third_parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_legal_requirements_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "dpia_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    dpia_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    kind = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_dpia_links_kind", "`kind` >= 1 AND `kind` <= 2");
                    table.ForeignKey(
                        name: "fk_dpia_links_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dpia_links_dpia_id",
                        column: x => x.dpia_id,
                        principalTable: "dpias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dpia_links_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "data_catalogue_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false),
                    personal_data = table.Column<int>(type: "int(11)", nullable: true),
                    involves_minors = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    large_volume = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    strategic_research = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    data_subjects = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    data_categories = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retention_period_months = table.Column<int>(type: "int(11)", nullable: true),
                    retention_trigger = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retention_basis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retention_requirement_id = table.Column<int>(type: "int(11)", nullable: true),
                    retention_review_due_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    retention_reviewed_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    international_transfer = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    transfer_mechanism = table.Column<int>(type: "int(11)", nullable: true),
                    notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    updated_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_data_catalogue_entries_personal_data", "`personal_data` IS NULL OR (`personal_data` >= 1 AND `personal_data` <= 4)");
                    table.CheckConstraint("ck_data_catalogue_entries_retention_period", "`retention_period_months` IS NULL OR (`retention_period_months` >= 0 AND `retention_period_months` <= 1200)");
                    table.CheckConstraint("ck_data_catalogue_entries_transfer_mechanism", "`transfer_mechanism` IS NULL OR (`transfer_mechanism` >= 1 AND `transfer_mechanism` <= 12)");
                    table.ForeignKey(
                        name: "fk_data_catalogue_entries_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_data_catalogue_entries_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_catalogue_entries_retention_requirement_id",
                        column: x => x.retention_requirement_id,
                        principalTable: "legal_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_catalogue_entries_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "risk_legal_requirements",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    legal_requirement_id = table.Column<int>(type: "int(11)", nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_legal_requirements_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_legal_requirements_legal_requirement_id",
                        column: x => x.legal_requirement_id,
                        principalTable: "legal_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_risk_legal_requirements_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "data_catalogue_locations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entry_id = table.Column<int>(type: "int(11)", nullable: false),
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
                    table.CheckConstraint("ck_data_catalogue_locations_purpose", "`purpose` >= 1 AND `purpose` <= 4");
                    table.ForeignKey(
                        name: "fk_data_catalogue_locations_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_data_catalogue_locations_entry_id",
                        column: x => x.entry_id,
                        principalTable: "data_catalogue_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "data_catalogue_purposes",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entry_id = table.Column<int>(type: "int(11)", nullable: false),
                    purpose = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    legal_basis = table.Column<int>(type: "int(11)", nullable: true),
                    legal_requirement_id = table.Column<int>(type: "int(11)", nullable: true),
                    basis_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    created_by_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.CheckConstraint("ck_data_catalogue_purposes_legal_basis", "`legal_basis` IS NULL OR (`legal_basis` >= 1 AND `legal_basis` <= 18)");
                    table.ForeignKey(
                        name: "fk_data_catalogue_purposes_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_data_catalogue_purposes_entry_id",
                        column: x => x.entry_id,
                        principalTable: "data_catalogue_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_catalogue_purposes_legal_requirement_id",
                        column: x => x.legal_requirement_id,
                        principalTable: "legal_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_data_catalogue_entries_created_by_id",
                table: "data_catalogue_entries",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_data_catalogue_entries_retention_requirement_id",
                table: "data_catalogue_entries",
                column: "retention_requirement_id");

            migrationBuilder.CreateIndex(
                name: "idx_data_catalogue_entries_updated_by_id",
                table: "data_catalogue_entries",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_data_catalogue_entries_entity_id",
                table: "data_catalogue_entries",
                column: "entity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_data_catalogue_locations_created_by_id",
                table: "data_catalogue_locations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_data_catalogue_locations_entry_id_country_purpose",
                table: "data_catalogue_locations",
                columns: new[] { "entry_id", "country", "purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_data_catalogue_purposes_created_by_id",
                table: "data_catalogue_purposes",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_data_catalogue_purposes_legal_requirement_id",
                table: "data_catalogue_purposes",
                column: "legal_requirement_id");

            migrationBuilder.CreateIndex(
                name: "uq_data_catalogue_purposes_entry_id_purpose",
                table: "data_catalogue_purposes",
                columns: new[] { "entry_id", "purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dpia_links_created_by_id",
                table: "dpia_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_dpia_links_entity_id",
                table: "dpia_links",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "uq_dpia_links_dpia_id_entity_id",
                table: "dpia_links",
                columns: new[] { "dpia_id", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dpias_approved_by_id",
                table: "dpias",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_dpias_created_by_id",
                table: "dpias",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_dpias_retired_by_id",
                table: "dpias",
                column: "retired_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_dpias_status",
                table: "dpias",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_dpias_updated_by_id",
                table: "dpias",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_legal_requirements_created_by_id",
                table: "legal_requirements",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_legal_requirements_third_party_id",
                table: "legal_requirements",
                column: "third_party_id");

            migrationBuilder.CreateIndex(
                name: "idx_legal_requirements_updated_by_id",
                table: "legal_requirements",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "uq_legal_requirements_code",
                table: "legal_requirements",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_risk_legal_requirements_created_by_id",
                table: "risk_legal_requirements",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_legal_requirements_legal_requirement_id",
                table: "risk_legal_requirements",
                column: "legal_requirement_id");

            migrationBuilder.CreateIndex(
                name: "uq_risk_legal_requirements_risk_id_legal_requirement_id",
                table: "risk_legal_requirements",
                columns: new[] { "risk_id", "legal_requirement_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_catalogue_locations");

            migrationBuilder.DropTable(
                name: "data_catalogue_purposes");

            migrationBuilder.DropTable(
                name: "dpia_links");

            migrationBuilder.DropTable(
                name: "risk_legal_requirements");

            migrationBuilder.DropTable(
                name: "data_catalogue_entries");

            migrationBuilder.DropTable(
                name: "dpias");

            migrationBuilder.DropTable(
                name: "legal_requirements");
        }
    }
}
