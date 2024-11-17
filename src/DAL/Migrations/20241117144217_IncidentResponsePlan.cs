using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class IncidentResponsePlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncidentResponsePlanId",
                table: "risks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncidentResponsePlanExecutionId",
                table: "nr_files",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncidentResponsePlanId",
                table: "nr_files",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncidentResponsePlanTaskId",
                table: "nr_files",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentResponsePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Description = table.Column<string>(type: "text", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    CreationDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    CreatedById = table.Column<int>(type: "int(11)", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()"),
                    UpdatedById = table.Column<int>(type: "int(11)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    HasBeenTested = table.Column<bool>(type: "boolean(1)", nullable: false, defaultValueSql: "0"),
                    HasBeenUpdated = table.Column<bool>(type: "boolean(1)", nullable: false, defaultValueSql: "0"),
                    HasBeenExercised = table.Column<bool>(type: "boolean(1)", nullable: false, defaultValueSql: "0"),
                    HasBeenReviewed = table.Column<bool>(type: "boolean(1)", nullable: false, defaultValueSql: "0"),
                    HasBeenApproved = table.Column<bool>(type: "boolean(1)", nullable: false, defaultValueSql: "0"),
                    LastTestDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastExerciseDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastReviewDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovalDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastTestedById = table.Column<int>(type: "int(11)", nullable: false),
                    LastExercisedById = table.Column<int>(type: "int(11)", nullable: false),
                    LastReviewedById = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_irp_last_exercised_by",
                        column: x => x.LastExercisedById,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irp_last_reviewed_by",
                        column: x => x.LastReviewedById,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irp_last_tested_by",
                        column: x => x.LastTestedById,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irp_user",
                        column: x => x.CreatedById,
                        principalTable: "user",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_irp_user_update",
                        column: x => x.UpdatedById,
                        principalTable: "user",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "IncidentResponsePlanTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    CreationDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByValue = table.Column<int>(type: "int(11)", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByValue = table.Column<int>(type: "int(11)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    HasBeenTested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ExecutionOrder = table.Column<int>(type: "int", nullable: false),
                    LastTestDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastTestedById = table.Column<int>(type: "int(11)", nullable: true),
                    EstimatedDuration = table.Column<TimeSpan>(type: "time(6)", nullable: true),
                    LastActualDuration = table.Column<TimeSpan>(type: "time(6)", nullable: true),
                    AssignedToId = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsParallel = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSequential = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsOptional = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SuccessCriteria = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    FailureCriteria = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    CompletionCriteria = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    VerificationCriteria = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    TaskType = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    ConditionToProceed = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    ConditionToSkip = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentResponsePlanTasks_user_CreatedByValue",
                        column: x => x.CreatedByValue,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentResponsePlanTasks_user_UpdatedByValue",
                        column: x => x.UpdatedByValue,
                        principalTable: "user",
                        principalColumn: "value");
                    table.ForeignKey(
                        name: "fk_irp_task_last_tested_by",
                        column: x => x.LastTestedById,
                        principalTable: "entities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "fk_irp_task_plan",
                        column: x => x.PlanId,
                        principalTable: "IncidentResponsePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "IncidentResponsePlanExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ExecutionDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    ExecutedById = table.Column<int>(type: "int(11)", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsTest = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsExercise = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExecutionTrigger = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    ExecutionResult = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb3_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb3"),
                    TaskId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_irp_executions_plan",
                        column: x => x.PlanId,
                        principalTable: "IncidentResponsePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irp_executions_user",
                        column: x => x.ExecutedById,
                        principalTable: "entities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "fk_irp_task_executions",
                        column: x => x.TaskId,
                        principalTable: "IncidentResponsePlanTasks",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateTable(
                name: "IncidentResponsePlanTaskToEntity",
                columns: table => new
                {
                    IncidentResponsePlanTaskId = table.Column<int>(type: "int(11)", nullable: false),
                    EntityId = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.IncidentResponsePlanTaskId, x.EntityId })
                        .Annotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                    table.ForeignKey(
                        name: "fk_irt_entity_irt",
                        column: x => x.EntityId,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_irt_irt_entity",
                        column: x => x.IncidentResponsePlanTaskId,
                        principalTable: "IncidentResponsePlanTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb3")
                .Annotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_risks_IncidentResponsePlanId",
                table: "risks",
                column: "IncidentResponsePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_nr_files_IncidentResponsePlanExecutionId",
                table: "nr_files",
                column: "IncidentResponsePlanExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_nr_files_IncidentResponsePlanId",
                table: "nr_files",
                column: "IncidentResponsePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_nr_files_IncidentResponsePlanTaskId",
                table: "nr_files",
                column: "IncidentResponsePlanTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanExecutions_ExecutedById",
                table: "IncidentResponsePlanExecutions",
                column: "ExecutedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanExecutions_PlanId",
                table: "IncidentResponsePlanExecutions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanExecutions_TaskId",
                table: "IncidentResponsePlanExecutions",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "idx_irp_name",
                table: "IncidentResponsePlans",
                column: "Name")
                .Annotation("MySql:FullTextIndex", true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlans_CreatedById",
                table: "IncidentResponsePlans",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlans_LastExercisedById",
                table: "IncidentResponsePlans",
                column: "LastExercisedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlans_LastReviewedById",
                table: "IncidentResponsePlans",
                column: "LastReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlans_LastTestedById",
                table: "IncidentResponsePlans",
                column: "LastTestedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlans_UpdatedById",
                table: "IncidentResponsePlans",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTasks_CreatedByValue",
                table: "IncidentResponsePlanTasks",
                column: "CreatedByValue");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTasks_LastTestedById",
                table: "IncidentResponsePlanTasks",
                column: "LastTestedById");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTasks_PlanId",
                table: "IncidentResponsePlanTasks",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTasks_UpdatedByValue",
                table: "IncidentResponsePlanTasks",
                column: "UpdatedByValue");

            migrationBuilder.CreateIndex(
                name: "irt_id",
                table: "IncidentResponsePlanTaskToEntity",
                columns: new[] { "EntityId", "IncidentResponsePlanTaskId" });

            migrationBuilder.AddForeignKey(
                name: "fk_irp_attachments",
                table: "nr_files",
                column: "IncidentResponsePlanId",
                principalTable: "IncidentResponsePlans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_executions_attachments",
                table: "nr_files",
                column: "IncidentResponsePlanExecutionId",
                principalTable: "IncidentResponsePlanExecutions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "fk_irp_task_attachments",
                table: "nr_files",
                column: "IncidentResponsePlanTaskId",
                principalTable: "IncidentResponsePlanTasks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_risks_IncidentResponsePlans_IncidentResponsePlanId",
                table: "risks",
                column: "IncidentResponsePlanId",
                principalTable: "IncidentResponsePlans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irp_attachments",
                table: "nr_files");

            migrationBuilder.DropForeignKey(
                name: "fk_irp_executions_attachments",
                table: "nr_files");

            migrationBuilder.DropForeignKey(
                name: "fk_irp_task_attachments",
                table: "nr_files");

            migrationBuilder.DropForeignKey(
                name: "FK_risks_IncidentResponsePlans_IncidentResponsePlanId",
                table: "risks");

            migrationBuilder.DropTable(
                name: "IncidentResponsePlanExecutions");

            migrationBuilder.DropTable(
                name: "IncidentResponsePlanTaskToEntity");

            migrationBuilder.DropTable(
                name: "IncidentResponsePlanTasks");

            migrationBuilder.DropTable(
                name: "IncidentResponsePlans");

            migrationBuilder.DropIndex(
                name: "IX_risks_IncidentResponsePlanId",
                table: "risks");

            migrationBuilder.DropIndex(
                name: "IX_nr_files_IncidentResponsePlanExecutionId",
                table: "nr_files");

            migrationBuilder.DropIndex(
                name: "IX_nr_files_IncidentResponsePlanId",
                table: "nr_files");

            migrationBuilder.DropIndex(
                name: "IX_nr_files_IncidentResponsePlanTaskId",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "IncidentResponsePlanId",
                table: "risks");

            migrationBuilder.DropColumn(
                name: "IncidentResponsePlanExecutionId",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "IncidentResponsePlanId",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "IncidentResponsePlanTaskId",
                table: "nr_files");
        }
    }
}
