using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddMsapAccountingSourcesAndSalesBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_name",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "ibs_supplier_code",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "accounting_source_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateTable(
                name: "mmsi_sales_books",
                schema: "msap",
                columns: table => new
                {
                    sales_book_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    serial_no = table.Column<string>(type: "text", nullable: false),
                    sold_to = table.Column<string>(type: "text", nullable: false),
                    tin_no = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vatable_sales = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_exempt_sales = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    zero_rated = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_sales = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: true),
                    company = table.Column<string>(type: "text", nullable: false),
                    approved_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_undocumented = table.Column<bool>(type: "boolean", nullable: false),
                    billing_revision = table.Column<int>(type: "integer", nullable: false),
                    billing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    approved_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    voided_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    void_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_sales_books", x => x.sales_book_id);
                    table.ForeignKey(
                        name: "fk_mmsi_sales_books_msap_billings_document_id",
                        column: x => x.document_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_accounting_source_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "accounting_source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_sales_books_company_transaction_date",
                schema: "msap",
                table: "mmsi_sales_books",
                columns: new[] { "company", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_sales_books_document_id_billing_revision",
                schema: "msap",
                table: "mmsi_sales_books",
                columns: new[] { "document_id", "billing_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mmsi_sales_books",
                schema: "msap");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_dispatch_tickets_accounting_source_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropColumn(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "ibs_supplier_code",
                schema: "msap",
                table: "mmsi_other_tug_charges");

            migrationBuilder.DropColumn(
                name: "ibs_supplier_id",
                schema: "msap",
                table: "mmsi_other_tug_charges");

            migrationBuilder.DropColumn(
                name: "accounting_source_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.AlterColumn<string>(
                name: "provider_name",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
