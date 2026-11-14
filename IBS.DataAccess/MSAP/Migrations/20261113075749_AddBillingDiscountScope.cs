using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingDiscountScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "discount_on_baf",
                schema: "msap",
                table: "mmsi_billings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "discount_on_tariff",
                schema: "msap",
                table: "mmsi_billings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discount_on_baf",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "discount_on_tariff",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
