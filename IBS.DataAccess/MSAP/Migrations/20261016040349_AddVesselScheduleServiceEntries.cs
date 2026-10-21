using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselScheduleServiceEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mmsi_vessel_schedule_service_entries",
                schema: "msap",
                columns: table => new
                {
                    vessel_schedule_service_entry_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vessel_schedule_id = table.Column<int>(type: "integer", nullable: false),
                    tugboat_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_vessel_schedule_service_entries", x => x.vessel_schedule_service_entry_id);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedule_service_entries_mmsi_services_service_",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedule_service_entries_mmsi_tugboats_tugboat_",
                        column: x => x.tugboat_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedule_service_entries_mmsi_vessel_schedules_",
                        column: x => x.vessel_schedule_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessel_schedules",
                        principalColumn: "vessel_schedule_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedule_service_entries_service_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedule_service_entries_tugboat_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                column: "tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedule_service_entries_vessel_schedule_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                column: "vessel_schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mmsi_vessel_schedule_service_entries",
                schema: "msap");
        }
    }
}
