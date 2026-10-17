using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddRateMaintenancePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_manage_rates",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_view_rates",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_manage_rates",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_view_rates",
                schema: "msap",
                table: "mmsi_user_accesses");
        }
    }
}
