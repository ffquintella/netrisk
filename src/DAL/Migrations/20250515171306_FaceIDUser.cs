using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class FaceIDUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "ReportedByEntity",
                table: "Incidents",
                type: "tinyint(1)",
                nullable: true,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValueSql: "0");

            migrationBuilder.CreateTable(
                name: "FaceIDUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int(11)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SignatureSeed = table.Column<string>(type: "varchar(255)", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceIDUsers_user_UserId",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "value",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "idx_signature_seed",
                table: "FaceIDUsers",
                column: "SignatureSeed",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceIDUsers_UserId",
                table: "FaceIDUsers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceIDUsers");

            migrationBuilder.AlterColumn<bool>(
                name: "ReportedByEntity",
                table: "Incidents",
                type: "tinyint(1)",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true,
                oldDefaultValueSql: "0");
        }
    }
}
