using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleIocSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_ioc",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_ioc",
                schema: "msap",
                table: "mmsi_vessel_schedules");
        }
    }
}
