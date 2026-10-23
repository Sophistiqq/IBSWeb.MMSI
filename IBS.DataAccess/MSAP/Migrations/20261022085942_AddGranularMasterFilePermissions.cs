using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddGranularMasterFilePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_manage_maritime_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_bank_account_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_customer_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_port_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_principal_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_service_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_terminal_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_tug_master_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_tugboat_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_tugboat_owner_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_vessel_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_manage_bank_account_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_customer_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_port_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_principal_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_service_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_terminal_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_tug_master_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_tugboat_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_tugboat_owner_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "can_manage_vessel_master_file",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_maritime_master_file",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
