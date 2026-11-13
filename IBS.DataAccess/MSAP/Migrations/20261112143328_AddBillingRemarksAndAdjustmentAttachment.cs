using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingRemarksAndAdjustmentAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "remarks",
                schema: "msap",
                table: "mmsi_billings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_url",
                schema: "msap",
                table: "mmsi_adjustment_logs",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "remarks",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "attachment_url",
                schema: "msap",
                table: "mmsi_adjustment_logs");
        }
    }
}
