using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track6Phase4IndexingBlobText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "id",
                table: "framework_control_tests");

            migrationBuilder.RenameIndex(
                name: "id1",
                table: "risk_scoring",
                newName: "id");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "user",
                type: "varchar(255)",
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "permissions",
                type: "text",
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "frameworks",
                type: "text",
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "frameworks",
                type: "text",
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "supplemental_guidance",
                table: "framework_controls",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "long_name",
                table: "framework_controls",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "framework_controls",
                type: "text",
                nullable: true,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(byte[]),
                oldType: "blob",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_vulnerabilities_first_detection",
                table: "vulnerabilities",
                column: "FirstDetection");

            migrationBuilder.CreateIndex(
                name: "idx_vulnerabilities_last_detection",
                table: "vulnerabilities",
                column: "LastDetection");

            migrationBuilder.CreateIndex(
                name: "idx_user_email",
                table: "user",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "idx_risks_status_submission_date",
                table: "risks",
                columns: new[] { "status", "submission_date" });

            migrationBuilder.CreateIndex(
                name: "idx_hosts_registration_date",
                table: "hosts",
                column: "RegistrationDate");

            migrationBuilder.CreateIndex(
                name: "idx_hosts_status",
                table: "hosts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_vulnerabilities_first_detection",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "idx_vulnerabilities_last_detection",
                table: "vulnerabilities");

            migrationBuilder.DropIndex(
                name: "idx_user_email",
                table: "user");

            migrationBuilder.DropIndex(
                name: "idx_risks_status_submission_date",
                table: "risks");

            migrationBuilder.DropIndex(
                name: "idx_hosts_registration_date",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "idx_hosts_status",
                table: "hosts");

            migrationBuilder.RenameIndex(
                name: "id",
                table: "risk_scoring",
                newName: "id1");

            migrationBuilder.AlterColumn<byte[]>(
                name: "email",
                table: "user",
                type: "blob",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AlterColumn<byte[]>(
                name: "description",
                table: "permissions",
                type: "blob",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AlterColumn<byte[]>(
                name: "name",
                table: "frameworks",
                type: "blob",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AlterColumn<byte[]>(
                name: "description",
                table: "frameworks",
                type: "blob",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text")
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AlterColumn<byte[]>(
                name: "supplemental_guidance",
                table: "framework_controls",
                type: "blob",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true)
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AlterColumn<byte[]>(
                name: "long_name",
                table: "framework_controls",
                type: "blob",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true)
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.AlterColumn<byte[]>(
                name: "description",
                table: "framework_controls",
                type: "blob",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true)
                .OldAnnotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "id",
                table: "framework_control_tests",
                column: "id",
                unique: true);
        }
    }
}
