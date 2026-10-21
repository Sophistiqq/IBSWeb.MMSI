using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    public partial class AddSeparateBillingLink : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "basic_billing_id",
                schema: "msap",
                table: "mmsi_billings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_basic_billing_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "basic_billing_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_mmsi_billings_basic_billing_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "basic_billing_id",
                principalSchema: "msap",
                principalTable: "mmsi_billings",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_mmsi_billings_basic_billing_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_billings_basic_billing_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "basic_billing_id",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
