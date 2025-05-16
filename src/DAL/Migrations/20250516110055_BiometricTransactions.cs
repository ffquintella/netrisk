using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class BiometricTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaceIdentification",
                table: "FaceIDUsers",
                type: "text",
                nullable: false,
                collation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdate",
                table: "FaceIDUsers",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "LastUpdateUserId",
                table: "FaceIDUsers",
                type: "int(11)",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BiometricTransaction",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int(11)", nullable: false),
                    FaceIdUserId = table.Column<int>(type: "int", nullable: false),
                    BiometricType = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TransactionObjectId = table.Column<int>(type: "int", nullable: true),
                    TransactionObjectType = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransactionDetails = table.Column<string>(type: "text", nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransactionResult = table.Column<int>(type: "int", nullable: false),
                    BiometricLivenessAnchor = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "fk_btrans_faceiduser",
                        column: x => x.FaceIdUserId,
                        principalTable: "FaceIDUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "fk_btrans_user",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "value");
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_FaceIDUsers_LastUpdateUserId",
                table: "FaceIDUsers",
                column: "LastUpdateUserId");

            migrationBuilder.CreateIndex(
                name: "idx_biometic_anchor",
                table: "BiometricTransaction",
                column: "BiometricLivenessAnchor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BiometricTransaction_FaceIdUserId",
                table: "BiometricTransaction",
                column: "FaceIdUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BiometricTransaction_UserId",
                table: "BiometricTransaction",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "fk_faceid_last_update",
                table: "FaceIDUsers",
                column: "LastUpdateUserId",
                principalTable: "user",
                principalColumn: "value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_faceid_last_update",
                table: "FaceIDUsers");

            migrationBuilder.DropTable(
                name: "BiometricTransaction");

            migrationBuilder.DropIndex(
                name: "IX_FaceIDUsers_LastUpdateUserId",
                table: "FaceIDUsers");

            migrationBuilder.DropColumn(
                name: "FaceIdentification",
                table: "FaceIDUsers");

            migrationBuilder.DropColumn(
                name: "LastUpdate",
                table: "FaceIDUsers");

            migrationBuilder.DropColumn(
                name: "LastUpdateUserId",
                table: "FaceIDUsers");
        }
    }
}
