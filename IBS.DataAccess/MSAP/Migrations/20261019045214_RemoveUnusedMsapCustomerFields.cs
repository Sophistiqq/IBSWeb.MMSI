using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    public partial class RemoveUnusedMsapCustomerFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customers_suppliers_commissionee_id",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_commissionee_id",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "commissionee_id",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "commission_rate",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "station_code",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_limit_as_of_today",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "retention_rate",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "has_multiple_terms",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "requires_price_adjustment",
                schema: "msap",
                table: "customers");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "commissionee_id",
                schema: "msap",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "commission_rate",
                schema: "msap",
                table: "customers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "station_code",
                schema: "msap",
                table: "customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit",
                schema: "msap",
                table: "customers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit_as_of_today",
                schema: "msap",
                table: "customers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "retention_rate",
                schema: "msap",
                table: "customers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_multiple_terms",
                schema: "msap",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_price_adjustment",
                schema: "msap",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_customers_commissionee_id",
                schema: "msap",
                table: "customers",
                column: "commissionee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_customers_suppliers_commissionee_id",
                schema: "msap",
                table: "customers",
                column: "commissionee_id",
                principalSchema: "msap",
                principalTable: "suppliers",
                principalColumn: "supplier_id");
        }
    }
}
