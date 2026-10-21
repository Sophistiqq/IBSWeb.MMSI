using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDefaultCosDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "default_cos_discount_percent",
                schema: "msap",
                table: "customers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_default_cos_discount_percent",
                schema: "msap",
                table: "customers",
                sql: "default_cos_discount_percent BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_default_cos_discount_percent",
                schema: "msap",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "default_cos_discount_percent",
                schema: "msap",
                table: "customers");
        }
    }
}
