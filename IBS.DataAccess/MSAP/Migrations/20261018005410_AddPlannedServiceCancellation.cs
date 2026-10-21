using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedServiceCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancelled_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_service_entries_cancellation",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                sql: "(cancelled_at IS NULL AND cancelled_by_user_id IS NULL AND cancellation_reason IS NULL) OR (cancelled_at IS NOT NULL AND cancelled_by_user_id IS NOT NULL AND length(trim(cancellation_reason)) > 0 AND cancellation_reason IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_service_entries_cancellation",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");
        }
    }
}
