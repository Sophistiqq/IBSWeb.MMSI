using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddOtherTugChargeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_confirm_other_tug_charges",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_other_tug_charges",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "mmsi_other_tug_charges",
                schema: "msap",
                columns: table => new
                {
                    other_tug_charge_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tugboat_owner_id = table.Column<int>(type: "integer", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    includes_vat = table.Column<bool>(type: "boolean", nullable: false),
                    document_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    prepared_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    prepared_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    confirmed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    confirmed_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_other_tug_charges", x => x.other_tug_charge_id);
                    table.CheckConstraint("ck_other_tug_charge_confirmation", "revision >= 1 AND length(trim(provider_reference)) > 0 AND ((confirmed_at IS NULL AND confirmed_by_user_id IS NULL AND confirmed_by_name IS NULL) OR (confirmed_at IS NOT NULL AND confirmed_by_user_id IS NOT NULL AND confirmed_by_name IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_mmsi_other_tug_charges_msap_tugboat_owners_tugboat_owner_id",
                        column: x => x.tugboat_owner_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboat_owners",
                        principalColumn: "tugboat_owner_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_other_tug_charge_lines",
                schema: "msap",
                columns: table => new
                {
                    other_tug_charge_line_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    other_tug_charge_id = table.Column<int>(type: "integer", nullable: false),
                    dispatch_ticket_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_other_tug_charge_lines", x => x.other_tug_charge_line_id);
                    table.CheckConstraint("ck_other_tug_charge_line_amount", "amount >= 0 AND amount <= 99999999999999.99");
                    table.ForeignKey(
                        name: "fk_mmsi_other_tug_charge_lines_mmsi_dispatch_tickets_dispatch_",
                        column: x => x.dispatch_ticket_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_dispatch_tickets",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_other_tug_charge_lines_mmsi_other_tug_charges_other_tu",
                        column: x => x.other_tug_charge_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_other_tug_charges",
                        principalColumn: "other_tug_charge_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_other_tug_charge_lines_dispatch_ticket_id",
                schema: "msap",
                table: "mmsi_other_tug_charge_lines",
                column: "dispatch_ticket_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_other_tug_charge_lines_other_tug_charge_id",
                schema: "msap",
                table: "mmsi_other_tug_charge_lines",
                column: "other_tug_charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_other_tug_charges_tugboat_owner_id_provider_reference",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                columns: new[] { "tugboat_owner_id", "provider_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mmsi_other_tug_charge_lines",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_other_tug_charges",
                schema: "msap");

            migrationBuilder.DropColumn(
                name: "can_confirm_other_tug_charges",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_other_tug_charges",
                schema: "msap",
                table: "mmsi_user_accesses");
        }
    }
}
