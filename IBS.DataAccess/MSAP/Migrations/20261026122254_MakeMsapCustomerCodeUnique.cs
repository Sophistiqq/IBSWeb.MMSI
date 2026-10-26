using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class MakeMsapCustomerCodeUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_customer_code",
                schema: "msap",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_customer_code",
                schema: "msap",
                table: "customers",
                column: "customer_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_customer_code",
                schema: "msap",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_customer_code",
                schema: "msap",
                table: "customers",
                column: "customer_code");
        }
    }
}
