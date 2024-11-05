using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class FirstMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int(6) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    value = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    creation_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    client_ip = table.Column<string>(type: "varchar(255)", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "assessments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    created = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "audit",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int(11)", nullable: false),
                    Type = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TableName = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    OldValues = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewValues = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AffectedColumns = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrimaryKey = table.Column<string>(type: "varchar(255)", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "client_registration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    ExternalId = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Hostname = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    LoggedAccount = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    RegistrationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "'0000-00-00 00:00:00'")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    LastVerificationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "'0000-00-00 00:00:00'")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    Status = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, defaultValueSql: "'requested'", collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "close_reason",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "closures",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    closure_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    close_reason = table.Column<int>(type: "int(11)", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "contributing_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    subject = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    weight = table.Column<float>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "contributing_risks_impact",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    contributing_risks_id = table.Column<int>(type: "int(11)", nullable: false),
                    value = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "contributing_risks_likelihood",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    value = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "control_class",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "control_maturity",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "control_phase",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "control_priority",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "control_type",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "custom_risk_model_values",
                columns: table => new
                {
                    impact = table.Column<int>(type: "int(11)", nullable: false),
                    likelihood = table.Column<int>(type: "int(11)", nullable: false),
                    value = table.Column<double>(type: "double(3,1)", nullable: false)
                },
                constraints: table =>
                {
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "entities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DefinitionName = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefinitionVersion = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    Updated = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    CreatedBy = table.Column<int>(type: "int(11)", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int(11)", nullable: false),
                    Status = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Parent = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_parent",
                        column: x => x.Parent,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "failed_login_attempts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    expired = table.Column<sbyte>(type: "tinyint(4)", nullable: true, defaultValueSql: "'0'"),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    ip = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true, defaultValueSql: "'0.0.0.0'", collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "family",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "file_type_extensions",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "file_types",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    control_id = table.Column<int>(type: "int(11)", nullable: false),
                    framework = table.Column<int>(type: "int(11)", nullable: false),
                    reference_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_test_audits",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    test_id = table.Column<int>(type: "int(11)", nullable: false),
                    tester = table.Column<int>(type: "int(11)", nullable: false),
                    test_frequency = table.Column<int>(type: "int(11)", nullable: false),
                    last_date = table.Column<DateOnly>(type: "date", nullable: false),
                    next_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    objective = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    test_steps = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    approximate_time = table.Column<int>(type: "int(11)", nullable: false),
                    expected_results = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    framework_control_id = table.Column<int>(type: "int(11)", nullable: false),
                    desired_frequency = table.Column<int>(type: "int(11)", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_test_comments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    test_audit_id = table.Column<int>(type: "int(11)", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    user = table.Column<int>(type: "int(11)", nullable: false),
                    comment = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_test_results",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    test_audit_id = table.Column<int>(type: "int(11)", nullable: false),
                    test_result = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    summary = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    test_date = table.Column<DateOnly>(type: "date", nullable: false),
                    submitted_by = table.Column<int>(type: "int(11)", nullable: false),
                    submission_date = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_test_results_to_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    test_results_id = table.Column<int>(type: "int(11)", nullable: true),
                    risk_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_tests",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    tester = table.Column<int>(type: "int(11)", nullable: false),
                    test_frequency = table.Column<int>(type: "int(11)", nullable: false),
                    last_date = table.Column<DateOnly>(type: "date", nullable: false),
                    next_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    objective = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    test_steps = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    approximate_time = table.Column<int>(type: "int(11)", nullable: false),
                    expected_results = table.Column<string>(type: "mediumtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    framework_control_id = table.Column<int>(type: "int(11)", nullable: false),
                    desired_frequency = table.Column<int>(type: "int(11)", nullable: true),
                    status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    created_at = table.Column<DateOnly>(type: "date", nullable: true),
                    additional_stakeholders = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_to_framework",
                columns: table => new
                {
                    control_id = table.Column<int>(type: "int(11)", nullable: false),
                    framework_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.control_id, x.framework_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_control_type_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    control_id = table.Column<int>(type: "int(11)", nullable: false),
                    control_type_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "framework_controls",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    short_name = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    long_name = table.Column<byte[]>(type: "blob", nullable: true),
                    description = table.Column<byte[]>(type: "blob", nullable: true),
                    supplemental_guidance = table.Column<byte[]>(type: "blob", nullable: true),
                    control_owner = table.Column<int>(type: "int(11)", nullable: true),
                    control_class = table.Column<int>(type: "int(11)", nullable: true),
                    control_phase = table.Column<int>(type: "int(11)", nullable: true),
                    control_number = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    control_maturity = table.Column<int>(type: "int(11)", nullable: false),
                    desired_maturity = table.Column<int>(type: "int(11)", nullable: false),
                    control_priority = table.Column<int>(type: "int(11)", nullable: true),
                    control_status = table.Column<bool>(type: "tinyint(1)", nullable: true, defaultValueSql: "'1'"),
                    family = table.Column<int>(type: "int(11)", nullable: true),
                    submission_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    last_audit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_audit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    desired_frequency = table.Column<int>(type: "int(11)", nullable: true),
                    mitigation_percent = table.Column<int>(type: "int(11)", nullable: false),
                    status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    deleted = table.Column<sbyte>(type: "tinyint(4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "frameworks",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    parent = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<byte[]>(type: "blob", nullable: false),
                    description = table.Column<byte[]>(type: "blob", nullable: false),
                    status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    order = table.Column<int>(type: "int(11)", nullable: false),
                    last_audit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_audit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    desired_frequency = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "impact",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    value = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "likelihood",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    value = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    key_hash = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    creation_date = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    expiration_date = table.Column<DateTime>(type: "datetime", nullable: true),
                    data = table.Column<byte[]>(type: "blob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "location",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_accept_users",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_cost",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_effort",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_to_controls",
                columns: table => new
                {
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: false),
                    control_id = table.Column<int>(type: "int(11)", nullable: false),
                    validation_details = table.Column<string>(type: "mediumtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    validation_owner = table.Column<int>(type: "int(11)", nullable: true, defaultValueSql: "'0'"),
                    validation_mitigation_percent = table.Column<int>(type: "int(11)", nullable: true, defaultValueSql: "'0'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.mitigation_id, x.control_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "mitigation_to_team",
                columns: table => new
                {
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: false),
                    team_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.mitigation_id, x.team_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "next_step",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "nr_files",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: true, defaultValueSql: "'0'"),
                    view_type = table.Column<int>(type: "int(11)", nullable: true, defaultValueSql: "'1'"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    unique_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    size = table.Column<int>(type: "int(11)", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    user = table.Column<int>(type: "int(11)", nullable: false),
                    content = table.Column<byte[]>(type: "longblob", nullable: false),
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "pending_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    assessment_id = table.Column<int>(type: "int(11)", nullable: false),
                    assessment_answer_id = table.Column<int>(type: "int(11)", nullable: false),
                    subject = table.Column<byte[]>(type: "blob", nullable: false),
                    score = table.Column<float>(type: "float", nullable: false),
                    owner = table.Column<int>(type: "int(11)", nullable: true),
                    affected_assets = table.Column<string>(type: "text", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    comment = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    submission_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "permission_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    description = table.Column<byte[]>(type: "blob", nullable: false),
                    order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "permission_to_permission_group",
                columns: table => new
                {
                    permission_id = table.Column<int>(type: "int(11)", nullable: false),
                    permission_group_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.permission_id, x.permission_group_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    description = table.Column<byte[]>(type: "blob", nullable: false),
                    order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "planning_strategy",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "questionnaire_pending_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    questionnaire_tracking_id = table.Column<int>(type: "int(11)", nullable: false),
                    questionnaire_scoring_id = table.Column<int>(type: "int(11)", nullable: false),
                    subject = table.Column<byte[]>(type: "blob", nullable: false),
                    owner = table.Column<int>(type: "int(11)", nullable: true),
                    asset = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    comment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    submission_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "regulation",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "residual_risk_scoring_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    residual_risk = table.Column<float>(type: "float", nullable: false),
                    last_update = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "review",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "review_levels",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false),
                    value = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    grouping = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    description = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    function = table.Column<int>(type: "int(11)", nullable: false),
                    order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_function",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_grouping",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    @default = table.Column<bool>(name: "default", type: "tinyint(1)", nullable: false),
                    order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_levels",
                columns: table => new
                {
                    value = table.Column<decimal>(type: "decimal(3,1)", precision: 3, scale: 1, nullable: false),
                    name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    color = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    display_name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_scoring",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false),
                    scoring_method = table.Column<int>(type: "int(11)", nullable: false),
                    calculated_risk = table.Column<float>(type: "float", nullable: false),
                    CLASSIC_likelihood = table.Column<float>(type: "float", nullable: false, defaultValueSql: "'5'"),
                    CLASSIC_impact = table.Column<float>(type: "float", nullable: false, defaultValueSql: "'5'"),
                    Custom = table.Column<float>(type: "float", nullable: true, defaultValueSql: "'10'"),
                    contributing_score = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_scoring_contributing_impacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_scoring_id = table.Column<int>(type: "int(11)", nullable: false),
                    contributing_risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    impact = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_scoring_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    calculated_risk = table.Column<float>(type: "float", nullable: false),
                    last_update = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_to_additional_stakeholder",
                columns: table => new
                {
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.risk_id, x.user_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_to_location",
                columns: table => new
                {
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    location_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.risk_id, x.location_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_to_team",
                columns: table => new
                {
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    team_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.risk_id, x.team_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_to_technology",
                columns: table => new
                {
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    technology_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.risk_id, x.technology_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    admin = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    @default = table.Column<bool>(name: "default", type: "tinyint(1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "scoring_methods",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, defaultValueSql: "''", collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    value = table.Column<string>(type: "mediumtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.name);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "source",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "status",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    tag = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "tags_taggees",
                columns: table => new
                {
                    tag_id = table.Column<int>(type: "int(11)", nullable: false),
                    taggee_id = table.Column<int>(type: "int(11)", nullable: false),
                    type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "team",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "technology",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "test_results",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    background_class = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "test_status",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "threat_catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    grouping = table.Column<int>(type: "int(11)", nullable: false),
                    name = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    description = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "threat_grouping",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    @default = table.Column<bool>(name: "default", type: "tinyint(1)", nullable: false),
                    order = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "user_pass_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    salt = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    password = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    add_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "user_pass_reuse_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    password = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    counts = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "assessment_questions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    assessment_id = table.Column<int>(type: "int(11)", nullable: false),
                    question = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    order = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'999999'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_assessment_question",
                        column: x => x.assessment_id,
                        principalTable: "assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "entities_properties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Type = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OldValue = table.Column<string>(type: "text", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Entity = table.Column<int>(type: "int(11)", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_entity",
                        column: x => x.Entity,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "role_responsibilities",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "int(11)", nullable: false),
                    permission_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.role_id, x.permission_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_role_p_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_perm_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    value = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValueSql: "'1'"),
                    lockout = table.Column<sbyte>(type: "tinyint(4)", nullable: false),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValueSql: "'local'", collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    username = table.Column<byte[]>(type: "blob", nullable: false),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    email = table.Column<byte[]>(type: "blob", nullable: false),
                    salt = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    password = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    last_login = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_password_change_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    role_id = table.Column<int>(type: "int(11)", nullable: false),
                    lang = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    admin = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    multi_factor = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    change_password = table.Column<sbyte>(type: "tinyint(4)", nullable: false),
                    manager = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.value);
                    table.ForeignKey(
                        name: "fk_role_user",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Ip = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HostName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<short>(type: "smallint(6)", nullable: false, defaultValueSql: "'1'"),
                    Source = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, defaultValueSql: "'manual'", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegistrationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    LastVerificationDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    TeamId = table.Column<int>(type: "int(11)", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OS = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FQDN = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MacAddress = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Properties = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_host_team",
                        column: x => x.TeamId,
                        principalTable: "team",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "assessment_answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    assessment_id = table.Column<int>(type: "int(11)", nullable: false),
                    question_id = table.Column<int>(type: "int(11)", nullable: false),
                    answer = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    submit_risk = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    risk_subject = table.Column<byte[]>(type: "blob", nullable: false),
                    risk_score = table.Column<float>(type: "float", nullable: false),
                    assessment_scoring_id = table.Column<int>(type: "int(11)", nullable: false),
                    risk_owner = table.Column<int>(type: "int(11)", nullable: true),
                    order = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'999999'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_assessment_answer",
                        column: x => x.assessment_id,
                        principalTable: "assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_question_answer",
                        column: x => x.question_id,
                        principalTable: "assessment_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    LastUpdate = table.Column<DateTime>(type: "datetime", nullable: true),
                    OwnerId = table.Column<int>(type: "int(11)", nullable: true),
                    Result = table.Column<byte[]>(type: "blob", nullable: true),
                    Parameters = table.Column<byte[]>(type: "blob", nullable: true),
                    Title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancellationToken = table.Column<byte[]>(type: "blob", nullable: true),
                    Progress = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_user_job",
                        column: x => x.OwnerId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int(11)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    ReceivedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int(11)", nullable: true, defaultValueSql: "'1'"),
                    ChatId = table.Column<int>(type: "int(11)", nullable: true),
                    Type = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fK_user_message",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "nr_actions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ObjectType = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    UserId = table.Column<int>(type: "int(11)", nullable: true),
                    Message = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fx_action_user",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "permission_to_user",
                columns: table => new
                {
                    permission_id = table.Column<int>(type: "int(11)", nullable: false),
                    user_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.permission_id, x.user_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_perm_user",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_perm",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    creatorId = table.Column<int>(type: "int(11)", nullable: false),
                    fileId = table.Column<int>(type: "int(11)", nullable: true),
                    parameters = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    creationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    type = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    status = table.Column<uint>(type: "int(10) unsigned zerofill", nullable: false, defaultValueSql: "'0000000000'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_creator_id",
                        column: x => x.creatorId,
                        principalTable: "user",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_file_id",
                        column: x => x.fileId,
                        principalTable: "nr_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "user_to_team",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int(11)", nullable: false),
                    team_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.user_id, x.team_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_ut_team",
                        column: x => x.team_id,
                        principalTable: "team",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ut_user",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "assessment_runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AssessmentId = table.Column<int>(type: "int(11)", nullable: false),
                    EntityId = table.Column<int>(type: "int(11)", nullable: false),
                    RunDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    AnalystId = table.Column<int>(type: "int(11)", nullable: true),
                    Status = table.Column<int>(type: "int(11)", nullable: false),
                    Comments = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HostId = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fkAnalystId",
                        column: x => x.AnalystId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fkAssessment",
                        column: x => x.AssessmentId,
                        principalTable: "assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fkEntity",
                        column: x => x.EntityId,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fkHost",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "hosts_services",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    HostId = table.Column<int>(type: "int(11)", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Protocol = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Port = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_host",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "assessment_runs_answers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AnswerId = table.Column<int>(type: "int(11)", nullable: false),
                    QuestionId = table.Column<int>(type: "int(11)", nullable: false),
                    RunId = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fkAnswerId",
                        column: x => x.AnswerId,
                        principalTable: "assessment_answers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fkQuestionId",
                        column: x => x.QuestionId,
                        principalTable: "assessment_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fkRunId",
                        column: x => x.RunId,
                        principalTable: "assessment_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "vulnerabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Score = table.Column<double>(type: "double", nullable: true),
                    Severity = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstDetection = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    DetectionCount = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    LastDetection = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()")
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    Description = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Solution = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Comments = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<ushort>(type: "smallint(5) unsigned", nullable: false, defaultValueSql: "'1'"),
                    HostId = table.Column<int>(type: "int(11)", nullable: true),
                    AnalystId = table.Column<int>(type: "int(11)", nullable: true),
                    FixTeamId = table.Column<int>(type: "int(11)", nullable: true),
                    Technology = table.Column<string>(type: "varchar(255)", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Details = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImportSource = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImportHash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HostServiceId = table.Column<int>(type: "int(11)", nullable: true),
                    Cvss3BaseScore = table.Column<float>(type: "float", nullable: true, defaultValueSql: "'0'"),
                    Cvss3TemporalScore = table.Column<float>(type: "float", nullable: true, defaultValueSql: "'0'"),
                    Cvss3Vector = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Cvss3TemporalVector = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Cvss3ImpactScore = table.Column<float>(type: "float", nullable: true, defaultValueSql: "'0'"),
                    CvssBaseScore = table.Column<float>(type: "float", nullable: true, defaultValueSql: "'0'"),
                    CvssScoreSource = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CvssTemporalScore = table.Column<float>(type: "float", nullable: true, defaultValueSql: "'0'"),
                    CvssTemporalVector = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CvssVector = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExploitAvaliable = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ExploitCodeMaturity = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExploitabilityEasy = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExploitedByScanner = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    PatchPublicationDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ThreatIntensity = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ThreatRecency = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ThreatSources = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Cves = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VprScore = table.Column<float>(type: "float", nullable: true),
                    VulnerabilityPublicationDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Xref = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Iava = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Msft = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mskb = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_hosts_service",
                        column: x => x.HostServiceId,
                        principalTable: "hosts_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vul_ent",
                        column: x => x.EntityId,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vulnerability_host",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vulnerability_team",
                        column: x => x.FixTeamId,
                        principalTable: "team",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_vulnerarbility_user",
                        column: x => x.AnalystId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "FixRequest",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VulnerabilityId = table.Column<int>(type: "int(11)", nullable: false),
                    Identifier = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    LastInteraction = table.Column<DateTime>(type: "datetime", nullable: true)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.ComputedColumn),
                    Status = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    FixTeamId = table.Column<int>(type: "int(11)", nullable: true),
                    IsTeamFix = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    SingleFixDestination = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    FixDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastReportingUserId = table.Column<int>(type: "int(11)", nullable: true),
                    RequestingUserId = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_fixteam",
                        column: x => x.FixTeamId,
                        principalTable: "team",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_lastReportingUser",
                        column: x => x.LastReportingUserId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_requesting_user_id",
                        column: x => x.RequestingUserId,
                        principalTable: "user",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_vulnerability",
                        column: x => x.VulnerabilityId,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "vulnerabilities_to_actions",
                columns: table => new
                {
                    vulnerabilityId = table.Column<int>(type: "int(11)", nullable: false),
                    actionId = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.vulnerabilityId, x.actionId })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_vul_act2",
                        column: x => x.actionId,
                        principalTable: "nr_actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vul_act_1",
                        column: x => x.vulnerabilityId,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int(11)", nullable: true),
                    IsAnonymous = table.Column<sbyte>(type: "tinyint(4)", nullable: false),
                    CommenterName = table.Column<string>(type: "varchar(255)", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Date = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    ReplyTo = table.Column<int>(type: "int(11)", nullable: true),
                    Type = table.Column<string>(type: "varchar(255)", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FixRequestId = table.Column<int>(type: "int(11)", nullable: true),
                    RiskId = table.Column<int>(type: "int(11)", nullable: true),
                    VulnerabilityId = table.Column<int>(type: "int(11)", nullable: true),
                    HostId = table.Column<int>(type: "int(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_fix_request_comments",
                        column: x => x.FixRequestId,
                        principalTable: "FixRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_host_id_comments",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_id_comments",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_vulnerability_id_comments",
                        column: x => x.VulnerabilityId,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "mgmt_reviews",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    submission_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    review = table.Column<int>(type: "int(11)", nullable: false),
                    reviewer = table.Column<int>(type: "int(11)", nullable: false),
                    next_step = table.Column<int>(type: "int(11)", nullable: false),
                    comments = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    next_review = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "'0000-00-00'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_next_step",
                        column: x => x.next_step,
                        principalTable: "next_step",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_review_type",
                        column: x => x.review,
                        principalTable: "review",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fw_rev",
                        column: x => x.reviewer,
                        principalTable: "user",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "mitigations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    submission_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    last_update = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "'0000-00-00 00:00:00'"),
                    planning_strategy = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    mitigation_effort = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    mitigation_cost = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    mitigation_owner = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    current_solution = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    security_requirements = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    security_recommendations = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    submitted_by = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    planning_date = table.Column<DateOnly>(type: "date", nullable: false),
                    mitigation_percent = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_mitigation_cost",
                        column: x => x.mitigation_cost,
                        principalTable: "mitigation_cost",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_mitigation_effort",
                        column: x => x.mitigation_effort,
                        principalTable: "mitigation_effort",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_mitigation_owner",
                        column: x => x.mitigation_owner,
                        principalTable: "user",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_planning_strategy",
                        column: x => x.planning_strategy,
                        principalTable: "planning_strategy",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_submitted_by",
                        column: x => x.submitted_by,
                        principalTable: "user",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    subject = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    reference_id = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValueSql: "''", collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    regulation = table.Column<int>(type: "int(11)", nullable: true),
                    control_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    source = table.Column<int>(type: "int(11)", nullable: true),
                    category = table.Column<int>(type: "int(11)", nullable: true),
                    owner = table.Column<int>(type: "int(11)", nullable: false),
                    manager = table.Column<int>(type: "int(11)", nullable: false),
                    assessment = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    notes = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    submission_date = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "current_timestamp()"),
                    last_update = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "'0000-00-00 00:00:00'"),
                    mitigation_id = table.Column<int>(type: "int(11)", nullable: true),
                    project_id = table.Column<int>(type: "int(11)", nullable: true),
                    close_id = table.Column<int>(type: "int(11)", nullable: true),
                    submitted_by = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'"),
                    risk_catalog_mapping = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    threat_catalog_mapping = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    template_group_id = table.Column<int>(type: "int(11)", nullable: false, defaultValueSql: "'1'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_risk_category",
                        column: x => x.category,
                        principalTable: "category",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_risk_mitigation",
                        column: x => x.mitigation_id,
                        principalTable: "mitigations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_risk_source",
                        column: x => x.source,
                        principalTable: "source",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "risk_to_entity",
                columns: table => new
                {
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    entity_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.risk_id, x.entity_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_risk_id",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "risks_to_vulnerabilities",
                columns: table => new
                {
                    risk_id = table.Column<int>(type: "int(11)", nullable: false),
                    vulnerability_id = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.risk_id, x.vulnerability_id })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_rv_r",
                        column: x => x.risk_id,
                        principalTable: "risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rv_v",
                        column: x => x.vulnerability_id,
                        principalTable: "vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_api_keys_ip",
                table: "api_keys",
                column: "client_ip");

            migrationBuilder.CreateIndex(
                name: "idx_api_keys_value",
                table: "api_keys",
                column: "value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fk_assessment_answer",
                table: "assessment_answers",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "fk_question_answer",
                table: "assessment_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "fk_assessment_question",
                table: "assessment_questions",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "fkAnalystId",
                table: "assessment_runs",
                column: "AnalystId");

            migrationBuilder.CreateIndex(
                name: "fkAssessment",
                table: "assessment_runs",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "fkEntity",
                table: "assessment_runs",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "fkHost",
                table: "assessment_runs",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "idxStatus",
                table: "assessment_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "fkAnswerId",
                table: "assessment_runs_answers",
                column: "AnswerId");

            migrationBuilder.CreateIndex(
                name: "fkQuestionId",
                table: "assessment_runs_answers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "fkRunId",
                table: "assessment_runs_answers",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "idx_audit_cols",
                table: "audit",
                column: "AffectedColumns")
                .Annotation("MySql:IndexPrefixLength", new[] { 768 });

            migrationBuilder.CreateIndex(
                name: "idx_audit_date",
                table: "audit",
                column: "DateTime");

            migrationBuilder.CreateIndex(
                name: "idx_audit_newVal",
                table: "audit",
                column: "NewValues")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "idx_audit_oldValues",
                table: "audit",
                column: "OldValues")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "idx_audit_pk",
                table: "audit",
                column: "PrimaryKey");

            migrationBuilder.CreateIndex(
                name: "idx_audit_table",
                table: "audit",
                column: "TableName");

            migrationBuilder.CreateIndex(
                name: "idx_audit_type",
                table: "audit",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "idx_audit_userid",
                table: "audit",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ExternalId",
                table: "client_registration",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "closures_close_reason_idx",
                table: "closures",
                column: "close_reason");

            migrationBuilder.CreateIndex(
                name: "closures_user_id_idx",
                table: "closures",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "fk_fix_request_comments",
                table: "comments",
                column: "FixRequestId");

            migrationBuilder.CreateIndex(
                name: "fk_host_id_comments",
                table: "comments",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "fk_risk_id_comments",
                table: "comments",
                column: "RiskId");

            migrationBuilder.CreateIndex(
                name: "fk_user_id_comments",
                table: "comments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "fk_vulnerability_id_comments",
                table: "comments",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "idx-commenterName",
                table: "comments",
                column: "CommenterName");

            migrationBuilder.CreateIndex(
                name: "idx-full-text",
                table: "comments",
                column: "Text")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "idx-type-comments",
                table: "comments",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "contributing_risks_id",
                table: "contributing_risks_impact",
                column: "contributing_risks_id");

            migrationBuilder.CreateIndex(
                name: "cri_index",
                table: "contributing_risks_impact",
                columns: new[] { "contributing_risks_id", "value" });

            migrationBuilder.CreateIndex(
                name: "cri_value_idx",
                table: "contributing_risks_impact",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "crl_index",
                table: "contributing_risks_likelihood",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "impact_likelihood_unique",
                table: "custom_risk_model_values",
                columns: new[] { "impact", "likelihood" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fk_parent",
                table: "entities",
                column: "Parent");

            migrationBuilder.CreateIndex(
                name: "idx_definition_name",
                table: "entities",
                column: "DefinitionName");

            migrationBuilder.CreateIndex(
                name: "fk_entity",
                table: "entities_properties",
                column: "Entity");

            migrationBuilder.CreateIndex(
                name: "idx_name",
                table: "entities_properties",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "idx_value",
                table: "entities_properties",
                column: "Value")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "name1",
                table: "file_type_extensions",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "name",
                table: "file_types",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fk_fixteam",
                table: "FixRequest",
                column: "FixTeamId");

            migrationBuilder.CreateIndex(
                name: "fk_lastReportingUser",
                table: "FixRequest",
                column: "LastReportingUserId");

            migrationBuilder.CreateIndex(
                name: "fk_requesting_user_id",
                table: "FixRequest",
                column: "RequestingUserId");

            migrationBuilder.CreateIndex(
                name: "fk_vulnerability",
                table: "FixRequest",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "idx_identifier",
                table: "FixRequest",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "control_id",
                table: "framework_control_mappings",
                column: "control_id");

            migrationBuilder.CreateIndex(
                name: "framework",
                table: "framework_control_mappings",
                column: "framework");

            migrationBuilder.CreateIndex(
                name: "id",
                table: "framework_control_tests",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "framework_id",
                table: "framework_control_to_framework",
                columns: new[] { "framework_id", "control_id" });

            migrationBuilder.CreateIndex(
                name: "framework_controls_deleted_idx",
                table: "framework_controls",
                column: "deleted");

            migrationBuilder.CreateIndex(
                name: "fk_host_team",
                table: "hosts",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "fk_host",
                table: "hosts_services",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "idx_name1",
                table: "hosts_services",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "idx_port",
                table: "hosts_services",
                column: "Port");

            migrationBuilder.CreateIndex(
                name: "idx_protocol",
                table: "hosts_services",
                column: "Protocol");

            migrationBuilder.CreateIndex(
                name: "impact_index",
                table: "impact",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "fk_user_job",
                table: "jobs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "idx_started",
                table: "jobs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "idx_status",
                table: "jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_updated",
                table: "jobs",
                column: "LastUpdate");

            migrationBuilder.CreateIndex(
                name: "likelihood_index",
                table: "likelihood",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "expiration_date_idx",
                table: "links",
                column: "expiration_date");

            migrationBuilder.CreateIndex(
                name: "key_type_idx",
                table: "links",
                columns: new[] { "key_hash", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fK_user_message",
                table: "messages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "idx_created_at",
                table: "messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_msg_type",
                table: "messages",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "idx_received_at",
                table: "messages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "idx_status1",
                table: "messages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "fk_next_step",
                table: "mgmt_reviews",
                column: "next_step");

            migrationBuilder.CreateIndex(
                name: "fk_review_type",
                table: "mgmt_reviews",
                column: "review");

            migrationBuilder.CreateIndex(
                name: "fk_risk",
                table: "mgmt_reviews",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "fw_rev",
                table: "mgmt_reviews",
                column: "reviewer");

            migrationBuilder.CreateIndex(
                name: "mau_risk_id_idx",
                table: "mitigation_accept_users",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "mau_risk_user_idx",
                table: "mitigation_accept_users",
                columns: new[] { "risk_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "mau_user_idx",
                table: "mitigation_accept_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "control_id1",
                table: "mitigation_to_controls",
                columns: new[] { "control_id", "mitigation_id" });

            migrationBuilder.CreateIndex(
                name: "mtg2ctrl_control_idx",
                table: "mitigation_to_controls",
                column: "control_id");

            migrationBuilder.CreateIndex(
                name: "mtg2ctrl_mtg_idx",
                table: "mitigation_to_controls",
                column: "mitigation_id");

            migrationBuilder.CreateIndex(
                name: "mtg2team_mtg_id",
                table: "mitigation_to_team",
                column: "mitigation_id");

            migrationBuilder.CreateIndex(
                name: "mtg2team_team_id",
                table: "mitigation_to_team",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "team_id",
                table: "mitigation_to_team",
                columns: new[] { "team_id", "mitigation_id" });

            migrationBuilder.CreateIndex(
                name: "fk_mitigation_cost",
                table: "mitigations",
                column: "mitigation_cost");

            migrationBuilder.CreateIndex(
                name: "fk_mitigation_effort",
                table: "mitigations",
                column: "mitigation_effort");

            migrationBuilder.CreateIndex(
                name: "fk_mitigation_owner",
                table: "mitigations",
                column: "mitigation_owner");

            migrationBuilder.CreateIndex(
                name: "fk_planning_strategy",
                table: "mitigations",
                column: "planning_strategy");

            migrationBuilder.CreateIndex(
                name: "fk_risks",
                table: "mitigations",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "fk_submitted_by",
                table: "mitigations",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "fx_action_user",
                table: "nr_actions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "idx_object_type",
                table: "nr_actions",
                column: "ObjectType")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "idx_mitigation_id",
                table: "nr_files",
                column: "mitigation_id");

            migrationBuilder.CreateIndex(
                name: "idx_risk_id",
                table: "nr_files",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "name2",
                table: "permission_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "permission_group_id",
                table: "permission_to_permission_group",
                columns: new[] { "permission_group_id", "permission_id" });

            migrationBuilder.CreateIndex(
                name: "user_id1",
                table: "permission_to_user",
                columns: new[] { "user_id", "permission_id" });

            migrationBuilder.CreateIndex(
                name: "key",
                table: "permissions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fk_creator_id",
                table: "reports",
                column: "creatorId");

            migrationBuilder.CreateIndex(
                name: "fk_file_id",
                table: "reports",
                column: "fileId");

            migrationBuilder.CreateIndex(
                name: "idx_name2",
                table: "reports",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "risk_id",
                table: "residual_risk_scoring_history",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "rrsh_last_update_idx",
                table: "residual_risk_scoring_history",
                column: "last_update");

            migrationBuilder.CreateIndex(
                name: "risk_levels_name_idx",
                table: "risk_levels",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "risk_levels_value_idx",
                table: "risk_levels",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "calculated_risk",
                table: "risk_scoring",
                column: "calculated_risk");

            migrationBuilder.CreateIndex(
                name: "id1",
                table: "risk_scoring",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "contributing_risk_id",
                table: "risk_scoring_contributing_impacts",
                column: "contributing_risk_id");

            migrationBuilder.CreateIndex(
                name: "risk_scoring_id",
                table: "risk_scoring_contributing_impacts",
                column: "risk_scoring_id");

            migrationBuilder.CreateIndex(
                name: "rsci_impact_idx",
                table: "risk_scoring_contributing_impacts",
                column: "impact");

            migrationBuilder.CreateIndex(
                name: "rsci_index",
                table: "risk_scoring_contributing_impacts",
                columns: new[] { "risk_scoring_id", "contributing_risk_id" });

            migrationBuilder.CreateIndex(
                name: "risk_id1",
                table: "risk_scoring_history",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "rsh_last_update_idx",
                table: "risk_scoring_history",
                column: "last_update");

            migrationBuilder.CreateIndex(
                name: "user_id",
                table: "risk_to_additional_stakeholder",
                columns: new[] { "user_id", "risk_id" });

            migrationBuilder.CreateIndex(
                name: "fk_entity_id",
                table: "risk_to_entity",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "location_id",
                table: "risk_to_location",
                columns: new[] { "location_id", "risk_id" });

            migrationBuilder.CreateIndex(
                name: "risk2team_risk_id",
                table: "risk_to_team",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "risk2team_team_id",
                table: "risk_to_team",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "team_id1",
                table: "risk_to_team",
                columns: new[] { "team_id", "risk_id" });

            migrationBuilder.CreateIndex(
                name: "technology_id",
                table: "risk_to_technology",
                columns: new[] { "technology_id", "risk_id" });

            migrationBuilder.CreateIndex(
                name: "category",
                table: "risks",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "close_id",
                table: "risks",
                column: "close_id");

            migrationBuilder.CreateIndex(
                name: "fk_risk_mitigation",
                table: "risks",
                column: "mitigation_id");

            migrationBuilder.CreateIndex(
                name: "manager",
                table: "risks",
                column: "manager");

            migrationBuilder.CreateIndex(
                name: "owner",
                table: "risks",
                column: "owner");

            migrationBuilder.CreateIndex(
                name: "project_id",
                table: "risks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "regulation",
                table: "risks",
                column: "regulation");

            migrationBuilder.CreateIndex(
                name: "source",
                table: "risks",
                column: "source");

            migrationBuilder.CreateIndex(
                name: "status",
                table: "risks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "submitted_by",
                table: "risks",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "fk_rv_v",
                table: "risks_to_vulnerabilities",
                column: "vulnerability_id");

            migrationBuilder.CreateIndex(
                name: "default",
                table: "role",
                column: "default",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "permission_id",
                table: "role_responsibilities",
                columns: new[] { "permission_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "tag_unique",
                table: "tags",
                column: "tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "tag_taggee_unique",
                table: "tags_taggees",
                columns: new[] { "tag_id", "taggee_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "taggee_type",
                table: "tags_taggees",
                columns: new[] { "taggee_id", "type" });

            migrationBuilder.CreateIndex(
                name: "name_unique",
                table: "test_results",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fk_role_user",
                table: "user",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "team_id2",
                table: "user_to_team",
                columns: new[] { "team_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "fk_hosts_service",
                table: "vulnerabilities",
                column: "HostServiceId");

            migrationBuilder.CreateIndex(
                name: "fk_vul_ent",
                table: "vulnerabilities",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "fk_vulnerability_host",
                table: "vulnerabilities",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "fk_vulnerability_team",
                table: "vulnerabilities",
                column: "FixTeamId");

            migrationBuilder.CreateIndex(
                name: "fk_vulnerarbility_user",
                table: "vulnerabilities",
                column: "AnalystId");

            migrationBuilder.CreateIndex(
                name: "idx_status2",
                table: "vulnerabilities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_technology",
                table: "vulnerabilities",
                column: "Technology");

            migrationBuilder.CreateIndex(
                name: "idx_title",
                table: "vulnerabilities",
                column: "Title")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "fk_vul_act2",
                table: "vulnerabilities_to_actions",
                column: "actionId");

            migrationBuilder.AddForeignKey(
                name: "fk_risk_id_comments",
                table: "comments",
                column: "RiskId",
                principalTable: "risks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_risk",
                table: "mgmt_reviews",
                column: "risk_id",
                principalTable: "risks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_risks",
                table: "mitigations",
                column: "risk_id",
                principalTable: "risks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mitigation_owner",
                table: "mitigations");

            migrationBuilder.DropForeignKey(
                name: "fk_submitted_by",
                table: "mitigations");

            migrationBuilder.DropForeignKey(
                name: "fk_risks",
                table: "mitigations");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "assessment_runs_answers");

            migrationBuilder.DropTable(
                name: "audit");

            migrationBuilder.DropTable(
                name: "client_registration");

            migrationBuilder.DropTable(
                name: "close_reason");

            migrationBuilder.DropTable(
                name: "closures");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "contributing_risks");

            migrationBuilder.DropTable(
                name: "contributing_risks_impact");

            migrationBuilder.DropTable(
                name: "contributing_risks_likelihood");

            migrationBuilder.DropTable(
                name: "control_class");

            migrationBuilder.DropTable(
                name: "control_maturity");

            migrationBuilder.DropTable(
                name: "control_phase");

            migrationBuilder.DropTable(
                name: "control_priority");

            migrationBuilder.DropTable(
                name: "control_type");

            migrationBuilder.DropTable(
                name: "custom_risk_model_values");

            migrationBuilder.DropTable(
                name: "entities_properties");

            migrationBuilder.DropTable(
                name: "failed_login_attempts");

            migrationBuilder.DropTable(
                name: "family");

            migrationBuilder.DropTable(
                name: "file_type_extensions");

            migrationBuilder.DropTable(
                name: "file_types");

            migrationBuilder.DropTable(
                name: "framework_control_mappings");

            migrationBuilder.DropTable(
                name: "framework_control_test_audits");

            migrationBuilder.DropTable(
                name: "framework_control_test_comments");

            migrationBuilder.DropTable(
                name: "framework_control_test_results");

            migrationBuilder.DropTable(
                name: "framework_control_test_results_to_risks");

            migrationBuilder.DropTable(
                name: "framework_control_tests");

            migrationBuilder.DropTable(
                name: "framework_control_to_framework");

            migrationBuilder.DropTable(
                name: "framework_control_type_mappings");

            migrationBuilder.DropTable(
                name: "framework_controls");

            migrationBuilder.DropTable(
                name: "frameworks");

            migrationBuilder.DropTable(
                name: "impact");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "likelihood");

            migrationBuilder.DropTable(
                name: "links");

            migrationBuilder.DropTable(
                name: "location");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "mgmt_reviews");

            migrationBuilder.DropTable(
                name: "mitigation_accept_users");

            migrationBuilder.DropTable(
                name: "mitigation_to_controls");

            migrationBuilder.DropTable(
                name: "mitigation_to_team");

            migrationBuilder.DropTable(
                name: "pending_risks");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropTable(
                name: "permission_to_permission_group");

            migrationBuilder.DropTable(
                name: "permission_to_user");

            migrationBuilder.DropTable(
                name: "questionnaire_pending_risks");

            migrationBuilder.DropTable(
                name: "regulation");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "residual_risk_scoring_history");

            migrationBuilder.DropTable(
                name: "review_levels");

            migrationBuilder.DropTable(
                name: "risk_catalog");

            migrationBuilder.DropTable(
                name: "risk_function");

            migrationBuilder.DropTable(
                name: "risk_grouping");

            migrationBuilder.DropTable(
                name: "risk_levels");

            migrationBuilder.DropTable(
                name: "risk_scoring");

            migrationBuilder.DropTable(
                name: "risk_scoring_contributing_impacts");

            migrationBuilder.DropTable(
                name: "risk_scoring_history");

            migrationBuilder.DropTable(
                name: "risk_to_additional_stakeholder");

            migrationBuilder.DropTable(
                name: "risk_to_entity");

            migrationBuilder.DropTable(
                name: "risk_to_location");

            migrationBuilder.DropTable(
                name: "risk_to_team");

            migrationBuilder.DropTable(
                name: "risk_to_technology");

            migrationBuilder.DropTable(
                name: "risks_to_vulnerabilities");

            migrationBuilder.DropTable(
                name: "role_responsibilities");

            migrationBuilder.DropTable(
                name: "scoring_methods");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "status");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "tags_taggees");

            migrationBuilder.DropTable(
                name: "technology");

            migrationBuilder.DropTable(
                name: "test_results");

            migrationBuilder.DropTable(
                name: "test_status");

            migrationBuilder.DropTable(
                name: "threat_catalog");

            migrationBuilder.DropTable(
                name: "threat_grouping");

            migrationBuilder.DropTable(
                name: "user_pass_history");

            migrationBuilder.DropTable(
                name: "user_pass_reuse_history");

            migrationBuilder.DropTable(
                name: "user_to_team");

            migrationBuilder.DropTable(
                name: "vulnerabilities_to_actions");

            migrationBuilder.DropTable(
                name: "assessment_answers");

            migrationBuilder.DropTable(
                name: "assessment_runs");

            migrationBuilder.DropTable(
                name: "FixRequest");

            migrationBuilder.DropTable(
                name: "next_step");

            migrationBuilder.DropTable(
                name: "review");

            migrationBuilder.DropTable(
                name: "nr_files");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "nr_actions");

            migrationBuilder.DropTable(
                name: "assessment_questions");

            migrationBuilder.DropTable(
                name: "vulnerabilities");

            migrationBuilder.DropTable(
                name: "assessments");

            migrationBuilder.DropTable(
                name: "hosts_services");

            migrationBuilder.DropTable(
                name: "entities");

            migrationBuilder.DropTable(
                name: "hosts");

            migrationBuilder.DropTable(
                name: "team");

            migrationBuilder.DropTable(
                name: "user");

            migrationBuilder.DropTable(
                name: "role");

            migrationBuilder.DropTable(
                name: "risks");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropTable(
                name: "mitigations");

            migrationBuilder.DropTable(
                name: "source");

            migrationBuilder.DropTable(
                name: "mitigation_cost");

            migrationBuilder.DropTable(
                name: "mitigation_effort");

            migrationBuilder.DropTable(
                name: "planning_strategy");
        }
    }
}
