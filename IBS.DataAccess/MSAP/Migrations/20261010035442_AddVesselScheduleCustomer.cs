using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselScheduleCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "customer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_customers_customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_customers_customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_vessel_schedules_customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");
        }
    }
}
