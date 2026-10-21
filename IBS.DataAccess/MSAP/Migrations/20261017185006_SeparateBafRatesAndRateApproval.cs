using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class SeparateBafRatesAndRateApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_tariff_rates",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by",
                schema: "msap",
                table: "mmsi_tariff_rates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_rate_uploads",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by",
                schema: "msap",
                table: "mmsi_rate_uploads",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rate_kind",
                schema: "msap",
                table: "mmsi_rate_uploads",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "mmsi_baf_rates",
                schema: "msap",
                columns: table => new
                {
                    baf_rate_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    update_by = table.Column<string>(type: "text", nullable: true),
                    update_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_baf_rates", x => x.baf_rate_id);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_mmsi_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_mmsi_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_mmsi_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_customer_id_terminal_id_service_id_as_of_date",
                schema: "msap",
                table: "mmsi_baf_rates",
                columns: new[] { "customer_id", "terminal_id", "service_id", "as_of_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_port_id",
                schema: "msap",
                table: "mmsi_baf_rates",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_service_id",
                schema: "msap",
                table: "mmsi_baf_rates",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_terminal_id",
                schema: "msap",
                table: "mmsi_baf_rates",
                column: "terminal_id");

            migrationBuilder.Sql("""
                INSERT INTO msap.mmsi_baf_rates
                    (as_of_date, customer_id, port_id, terminal_id, service_id, amount, created_by, created_date, update_by, update_date)
                SELECT as_of_date, customer_id, port_id, terminal_id, service_id, baf, created_by, created_date, update_by, update_date
                FROM msap.mmsi_tariff_rates;
                """);

            migrationBuilder.DropColumn(
                name: "baf",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropColumn(
                name: "baf_discount",
                schema: "msap",
                table: "mmsi_tariff_rates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_rate_uploads");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "msap",
                table: "mmsi_rate_uploads");

            migrationBuilder.DropColumn(
                name: "rate_kind",
                schema: "msap",
                table: "mmsi_rate_uploads");

            migrationBuilder.AddColumn<decimal>(
                name: "baf",
                schema: "msap",
                table: "mmsi_tariff_rates",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "baf_discount",
                schema: "msap",
                table: "mmsi_tariff_rates",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE msap.mmsi_tariff_rates AS tariff
                SET baf = source.amount
                FROM (
                    SELECT DISTINCT ON (customer_id, terminal_id, service_id, as_of_date)
                        customer_id, terminal_id, service_id, as_of_date, amount
                    FROM msap.mmsi_baf_rates
                    ORDER BY customer_id, terminal_id, service_id, as_of_date, baf_rate_id DESC
                ) AS source
                WHERE tariff.customer_id = source.customer_id AND tariff.terminal_id = source.terminal_id
                    AND tariff.service_id = source.service_id AND tariff.as_of_date = source.as_of_date;
                """);

            migrationBuilder.DropTable(
                name: "mmsi_baf_rates",
                schema: "msap");
        }
    }
}
