using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddTugboatOwnerPaymentProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_type",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tin",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vat_type",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "withholding_tax_percent",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "tax_type",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "tin",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "vat_type",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "withholding_tax_percent",
                schema: "msap",
                table: "mmsi_tugboat_owners");
        }
    }
}
