using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AllowDirectTugboatPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "liability_journal_voucher_id",
                table: "filpride_tugboat_payments",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM filpride_tugboat_payments WHERE liability_journal_voucher_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot require an AP journal while direct tugboat payments exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<int>(
                name: "liability_journal_voucher_id",
                table: "filpride_tugboat_payments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
