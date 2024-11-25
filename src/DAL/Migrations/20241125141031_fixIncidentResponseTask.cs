using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class fixIncidentResponseTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentResponsePlanTasks_user_CreatedByValue",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_IncidentResponsePlanTasks_user_UpdatedByValue",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropTable(
                name: "IncidentResponsePlanTaskToEntity");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanTasks_CreatedByValue",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropColumn(
                name: "CreatedByValue",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.RenameColumn(
                name: "UpdatedByValue",
                table: "IncidentResponsePlanTasks",
                newName: "UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_UpdatedByValue",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_UpdatedById");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "1",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "PlanId",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "IncidentResponsePlanTasks",
                type: "text",
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastUpdate",
                table: "IncidentResponsePlanTasks",
                type: "datetime(6)",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastTestDate",
                table: "IncidentResponsePlanTasks",
                type: "datetime",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsSequential",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsParallel",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsOptional",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenTested",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<int>(
                name: "ExecutionOrder",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: false,
                defaultValueSql: "1",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "IncidentResponsePlanTasks",
                type: "text",
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationDate",
                table: "IncidentResponsePlanTasks",
                type: "datetime",
                nullable: false,
                defaultValueSql: "current_timestamp()",
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");

            migrationBuilder.AlterColumn<int>(
                name: "AssignedToId",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTasks_AssignedToId",
                table: "IncidentResponsePlanTasks",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResponsePlanTasks_CreatedById",
                table: "IncidentResponsePlanTasks",
                column: "CreatedById");

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_created_by",
                table: "IncidentResponsePlanTasks",
                column: "CreatedById",
                principalTable: "user",
                principalColumn: "value");

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_task_assigned_to",
                table: "IncidentResponsePlanTasks",
                column: "AssignedToId",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_irpt_updated_by",
                table: "IncidentResponsePlanTasks",
                column: "UpdatedById",
                principalTable: "user",
                principalColumn: "value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_irpt_created_by",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropForeignKey(
                name: "fk_irpt_task_assigned_to",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropForeignKey(
                name: "fk_irpt_updated_by",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanTasks_AssignedToId",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropIndex(
                name: "IX_IncidentResponsePlanTasks_CreatedById",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "IncidentResponsePlanTasks");

            migrationBuilder.RenameColumn(
                name: "UpdatedById",
                table: "IncidentResponsePlanTasks",
                newName: "UpdatedByValue");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_UpdatedById",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_UpdatedByValue");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "IncidentResponsePlanTasks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "IncidentResponsePlanTasks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldDefaultValueSql: "1");

            migrationBuilder.AlterColumn<int>(
                name: "PlanId",
                table: "IncidentResponsePlanTasks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "IncidentResponsePlanTasks",
                type: "longtext",
                nullable: true,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastUpdate",
                table: "IncidentResponsePlanTasks",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastTestDate",
                table: "IncidentResponsePlanTasks",
                type: "datetime(6)",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsSequential",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "IsParallel",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "IsOptional",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<bool>(
                name: "HasBeenTested",
                table: "IncidentResponsePlanTasks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");

            migrationBuilder.AlterColumn<int>(
                name: "ExecutionOrder",
                table: "IncidentResponsePlanTasks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)",
                oldDefaultValueSql: "1");

            migrationBuilder.UpdateData(
                table: "IncidentResponsePlanTasks",
                keyColumn: "Description",
                keyValue: null,
                column: "Description",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "IncidentResponsePlanTasks",
                type: "longtext",
                nullable: false,
                collation: "utf8mb3_general_ci",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("MySql:CharSet", "utf8mb3")
                .OldAnnotation("Relational:Collation", "utf8mb3_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationDate",
                table: "IncidentResponsePlanTasks",
                type: "datetime(6)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldDefaultValueSql: "current_timestamp()");

            migrationBuilder.AlterColumn<int>(
                name: "AssignedToId",
                table: "IncidentResponsePlanTasks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int(11)");

            migrationBuilder.AddColumn<int>(
                name: "CreatedByValue",
                table: "IncidentResponsePlanTasks",
                type: "int(11)",
                nullable: false,
                defaultValue: 0);

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
                name: "IX_IncidentResponsePlanTasks_CreatedByValue",
                table: "IncidentResponsePlanTasks",
                column: "CreatedByValue");

            migrationBuilder.CreateIndex(
                name: "irt_id",
                table: "IncidentResponsePlanTaskToEntity",
                columns: new[] { "EntityId", "IncidentResponsePlanTaskId" });

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentResponsePlanTasks_user_CreatedByValue",
                table: "IncidentResponsePlanTasks",
                column: "CreatedByValue",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentResponsePlanTasks_user_UpdatedByValue",
                table: "IncidentResponsePlanTasks",
                column: "UpdatedByValue",
                principalTable: "user",
                principalColumn: "value");
        }
    }
}
