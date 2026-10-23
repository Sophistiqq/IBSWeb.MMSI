using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedMasterFileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fixed_rate",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropColumn(
                name: "address2",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropColumn(
                name: "address3",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropColumn(
                name: "agent",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropColumn(
                name: "landline1",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropColumn(
                name: "landline2",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropColumn(
                name: "mobile1",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropColumn(
                name: "mobile2",
                schema: "msap",
                table: "mmsi_principals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "fixed_rate",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "address2",
                schema: "msap",
                table: "mmsi_principals",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address3",
                schema: "msap",
                table: "mmsi_principals",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent",
                schema: "msap",
                table: "mmsi_principals",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "landline1",
                schema: "msap",
                table: "mmsi_principals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "landline2",
                schema: "msap",
                table: "mmsi_principals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile1",
                schema: "msap",
                table: "mmsi_principals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile2",
                schema: "msap",
                table: "mmsi_principals",
                type: "text",
                nullable: true);
        }
    }
}
