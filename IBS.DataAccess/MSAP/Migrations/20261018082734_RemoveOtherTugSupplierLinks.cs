using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOtherTugSupplierLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "ibs_supplier_code",
                schema: "msap",
                table: "mmsi_other_tug_charges");

            migrationBuilder.DropColumn(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_other_tug_charges");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ibs_supplier_code",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "integer",
                nullable: true);
        }
    }
}
