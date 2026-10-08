using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations;

public partial class ConcurrentRiskAcceptanceRenewal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Never silently choose a winner if an older deployment already recorded the race. Fail
        // before changing the index so an operator can reconcile the evidence explicitly.
        migrationBuilder.Sql("""
            SET @nr_ddl = IF(
                (SELECT COUNT(*) FROM (
                    SELECT `renewed_from_id`
                    FROM `risk_acceptances`
                    WHERE `renewed_from_id` IS NOT NULL
                    GROUP BY `renewed_from_id`
                    HAVING COUNT(*) > 1
                ) AS `duplicate_renewals`) > 0,
                'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''Cannot add uq_ra_renewed_from_id: duplicate renewal history exists; reconcile it explicitly without deleting acceptance evidence''',
                'DO 0');
            PREPARE nr_ddl FROM @nr_ddl;
            EXECUTE nr_ddl;
            DEALLOCATE PREPARE nr_ddl;
            """);

        // The existing self-referencing FK requires an index, so create its unique replacement first.
        migrationBuilder.CreateIndex(
            name: "uq_ra_renewed_from_id",
            table: "risk_acceptances",
            column: "renewed_from_id",
            unique: true);

        migrationBuilder.DropIndex(
            name: "idx_ra_renewed_from_id",
            table: "risk_acceptances");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the FK supported while replacing the unique index during rollback.
        migrationBuilder.CreateIndex(
            name: "idx_ra_renewed_from_id",
            table: "risk_acceptances",
            column: "renewed_from_id");

        migrationBuilder.DropIndex(
            name: "uq_ra_renewed_from_id",
            table: "risk_acceptances");
    }
}
