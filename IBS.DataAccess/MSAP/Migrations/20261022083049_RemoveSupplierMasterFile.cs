using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSupplierMasterFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "msap");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "msap",
                columns: table => new
                {
                    supplier_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    proof_of_exemption_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proof_of_exemption_file_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    proof_of_registration_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proof_of_registration_file_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    reason_of_exemption = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requires_price_adjustment = table.Column<bool>(type: "boolean", nullable: false),
                    supplier_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supplier_terms = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    supplier_tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    validity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    validity_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    vat_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    withholding_tax_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.supplier_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_supplier_code",
                schema: "msap",
                table: "suppliers",
                column: "supplier_code");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_supplier_name",
                schema: "msap",
                table: "suppliers",
                column: "supplier_name");
        }
    }
}
