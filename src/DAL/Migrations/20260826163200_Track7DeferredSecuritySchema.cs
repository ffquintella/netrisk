using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track7DeferredSecuritySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "entity_id",
                table: "nr_files",
                type: "int(11)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "login_attempts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    identity = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure_count = table.Column<int>(type: "int(11)", nullable: false),
                    first_failure_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    last_failure_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    locked_until = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "revoked_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "int(11)", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    jti = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<int>(type: "int(11)", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    reason = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                    table.ForeignKey(
                        name: "fk_revoked_tokens_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_nr_files_entity_id",
                table: "nr_files",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "idx_login_attempts_last_failure_at",
                table: "login_attempts",
                column: "last_failure_at");

            migrationBuilder.CreateIndex(
                name: "uq_login_attempts_identity_source",
                table: "login_attempts",
                columns: new[] { "identity", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_revoked_tokens_expires_at",
                table: "revoked_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_revoked_tokens_user_id",
                table: "revoked_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_revoked_tokens_jti",
                table: "revoked_tokens",
                column: "jti",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_nr_files_entity_id",
                table: "nr_files",
                column: "entity_id",
                principalTable: "entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nr_files_entity_id",
                table: "nr_files");

            migrationBuilder.DropTable(
                name: "login_attempts");

            migrationBuilder.DropTable(
                name: "revoked_tokens");

            migrationBuilder.DropIndex(
                name: "idx_nr_files_entity_id",
                table: "nr_files");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "nr_files");
        }
    }
}
