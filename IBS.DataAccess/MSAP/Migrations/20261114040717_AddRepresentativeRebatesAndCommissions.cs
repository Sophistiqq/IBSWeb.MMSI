using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddRepresentativeRebatesAndCommissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "agency_incentive_amount",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "agent_commission_amount",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "agency_incentive_amount",
                schema: "msap",
                table: "mmsi_billings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "agency_incentive_base",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agency_incentive_mode",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agency_incentive_name",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "agency_incentive_rate",
                schema: "msap",
                table: "mmsi_billings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "agency_incentive_representative_id",
                schema: "msap",
                table: "mmsi_billings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "agent_commission_amount",
                schema: "msap",
                table: "mmsi_billings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "agent_commission_base",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_commission_mode",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_commission_name",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "agent_commission_rate",
                schema: "msap",
                table: "mmsi_billings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "agent_commission_representative_id",
                schema: "msap",
                table: "mmsi_billings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mmsi_representatives",
                schema: "msap",
                columns: table => new
                {
                    representative_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    representative_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    principal_id = table.Column<int>(type: "integer", nullable: true),
                    rate_mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    rate_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    calculation_base = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_representatives", x => x.representative_id);
                    table.ForeignKey(
                        name: "fk_mmsi_representatives_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_representatives_mmsi_principals_principal_id",
                        column: x => x.principal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_principals",
                        principalColumn: "principal_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_agency_incentive_representative_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "agency_incentive_representative_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_agent_commission_representative_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "agent_commission_representative_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_representatives_customer_id",
                schema: "msap",
                table: "mmsi_representatives",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_representatives_principal_id",
                schema: "msap",
                table: "mmsi_representatives",
                column: "principal_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_representatives_agency_incentive_represe",
                schema: "msap",
                table: "mmsi_billings",
                column: "agency_incentive_representative_id",
                principalSchema: "msap",
                principalTable: "mmsi_representatives",
                principalColumn: "representative_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_representatives_agent_commission_represe",
                schema: "msap",
                table: "mmsi_billings",
                column: "agent_commission_representative_id",
                principalSchema: "msap",
                principalTable: "mmsi_representatives",
                principalColumn: "representative_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_representatives_agency_incentive_represe",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_representatives_agent_commission_represe",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropTable(
                name: "mmsi_representatives",
                schema: "msap");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_billings_agency_incentive_representative_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_billings_agent_commission_representative_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agency_incentive_amount",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "agent_commission_amount",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "agency_incentive_amount",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agency_incentive_base",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agency_incentive_mode",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agency_incentive_name",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agency_incentive_rate",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agency_incentive_representative_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agent_commission_amount",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agent_commission_base",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agent_commission_mode",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agent_commission_name",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agent_commission_rate",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "agent_commission_representative_id",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
