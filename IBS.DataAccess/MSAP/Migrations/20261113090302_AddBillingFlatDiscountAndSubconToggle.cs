using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingFlatDiscountAndSubconToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discount_type",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "flat_discount_baf",
                schema: "msap",
                table: "mmsi_billings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "flat_discount_tariff",
                schema: "msap",
                table: "mmsi_billings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "include_subcon_in_discount",
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
                name: "discount_type",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "flat_discount_baf",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "flat_discount_tariff",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "include_subcon_in_discount",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
