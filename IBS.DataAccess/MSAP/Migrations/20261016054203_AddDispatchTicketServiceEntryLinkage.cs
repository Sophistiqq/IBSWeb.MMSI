using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchTicketServiceEntryLinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_manual_baf_rate",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_manual_tariff_rate",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "raw_duration_hours",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "raw_duration_minutes",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vessel_schedule_service_entry_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_vessel_schedule_service_entry_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "vessel_schedule_service_entry_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dispatch_tickets_raw_duration",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                sql: "(raw_duration_hours IS NULL OR raw_duration_hours >= 0) AND (raw_duration_minutes IS NULL OR raw_duration_minutes >= 0)");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_vessel_schedule_service_entries_",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "vessel_schedule_service_entry_id",
                principalSchema: "msap",
                principalTable: "mmsi_vessel_schedule_service_entries",
                principalColumn: "vessel_schedule_service_entry_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_vessel_schedule_service_entries_",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_dispatch_tickets_vessel_schedule_service_entry_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dispatch_tickets_raw_duration",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "is_manual_baf_rate",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "is_manual_tariff_rate",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "raw_duration_hours",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "raw_duration_minutes",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "vessel_schedule_service_entry_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");
        }
    }
}
