using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class PersistTicketSbmaAndValidateActualTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "sbma_share_amount",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_dispatch_tickets_actual_times",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                sql: "(date_left IS NULL AND time_left IS NULL AND date_arrived IS NULL AND time_arrived IS NULL) OR (date_left IS NOT NULL AND time_left IS NOT NULL AND date_arrived IS NOT NULL AND time_arrived IS NOT NULL AND date_arrived + time_arrived > date_left + time_left)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dispatch_tickets_sbma_share",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                sql: "sbma_share_amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_dispatch_tickets_actual_times",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dispatch_tickets_sbma_share",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "sbma_share_amount",
                schema: "msap",
                table: "mmsi_dispatch_tickets");
        }
    }
}
