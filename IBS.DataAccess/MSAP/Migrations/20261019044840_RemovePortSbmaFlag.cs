using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    public partial class RemovePortSbmaFlag : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_sbma",
                schema: "msap",
                table: "mmsi_ports");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_sbma",
                schema: "msap",
                table: "mmsi_ports",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
