using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddMsapPeriodStatusAndAdjustmentLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "period_status",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period_status",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period_status",
                schema: "msap",
                table: "mmsi_collections",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period_status",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mmsi_adjustment_logs",
                schema: "msap",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    record_id = table.Column<int>(type: "integer", nullable: false),
                    reference_number = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    edited_by = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_adjustment_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_adjustment_logs_document_type_record_id",
                schema: "msap",
                table: "mmsi_adjustment_logs",
                columns: new[] { "document_type", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_adjustment_logs_edited_date",
                schema: "msap",
                table: "mmsi_adjustment_logs",
                column: "edited_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mmsi_adjustment_logs",
                schema: "msap");

            migrationBuilder.DropColumn(
                name: "period_status",
                schema: "msap",
                table: "mmsi_other_tug_charges");

            migrationBuilder.DropColumn(
                name: "period_status",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "period_status",
                schema: "msap",
                table: "mmsi_collections");

            migrationBuilder.DropColumn(
                name: "period_status",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
