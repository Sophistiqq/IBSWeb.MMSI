using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleCommercialSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cos_discount_percent",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "sbma_enabled",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "skip_cos_discount",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "baf_method",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_baf_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_tariff_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "tariff_basis",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_vessel_schedules_cos_discount_percent",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                sql: "cos_discount_percent BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_service_entries_baf_method",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                sql: "baf_method IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_service_entries_manual_baf_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                sql: "manual_baf_rate >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_service_entries_manual_tariff_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                sql: "manual_tariff_rate >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_service_entries_tariff_basis",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                sql: "tariff_basis IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vessel_schedules_cos_discount_percent",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_service_entries_baf_method",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_service_entries_manual_baf_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_service_entries_manual_tariff_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_service_entries_tariff_basis",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "cos_discount_percent",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "sbma_enabled",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "skip_cos_discount",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "baf_method",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "manual_baf_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "manual_tariff_rate",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");

            migrationBuilder.DropColumn(
                name: "tariff_basis",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");
        }
    }
}
