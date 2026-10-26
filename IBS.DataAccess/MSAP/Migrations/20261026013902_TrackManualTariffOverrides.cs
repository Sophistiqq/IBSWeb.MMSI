using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class TrackManualTariffOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_manual_tariff_override",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                type: "boolean",
                nullable: true);

            // Recover known fallback sources; ambiguous legacy agreements retain their existing manual protection.
            migrationBuilder.Sql("""
                UPDATE msap.mmsi_vessel_schedule_service_entries AS entry
                SET is_manual_tariff_override = false
                FROM msap.mmsi_vessel_schedules AS schedule
                WHERE entry.vessel_schedule_id = schedule.vessel_schedule_id
                  AND (entry.manual_tariff_rate IS NULL
                    OR EXISTS (
                        SELECT 1 FROM msap.mmsi_rate_uploads AS upload,
                            jsonb_array_elements(upload.rows_json) AS source
                        WHERE upload.rate_kind = 1 AND upload.approved_at IS NOT NULL AND upload.company = 'MMSI'
                          AND (source->>'Selected')::boolean = true
                          AND (source->>'SourceScheduleId')::integer = schedule.vessel_schedule_id
                          AND (source->>'SourceServiceEntryId')::integer = entry.vessel_schedule_service_entry_id
                          AND (source->>'SourceRevision')::integer = schedule.revision
                          AND (source->>'Amount')::numeric = entry.manual_tariff_rate
                          AND (source->>'CustomerId')::integer = schedule.customer_id
                          AND (source->>'PortId')::integer = schedule.port_id
                          AND (source->>'TerminalId')::integer = schedule.terminal_id
                          AND (source->>'ServiceId')::integer = entry.service_id)
                    OR (schedule.edited_date IS NULL AND schedule.created_date > '1900-01-01'::timestamp
                        AND NOT EXISTS (
                            SELECT 1 FROM msap.mmsi_tariff_rates AS rate
                            WHERE rate.customer_id = schedule.customer_id AND rate.port_id = schedule.port_id
                              AND rate.terminal_id = schedule.terminal_id AND rate.service_id = entry.service_id
                              AND rate.as_of_date <= schedule.planned_start::date
                              AND (rate.created_date IS NULL OR rate.created_date <= schedule.created_date))));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_manual_tariff_override",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries");
        }
    }
}
