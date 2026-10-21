using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RenameAndAddNewMmsiTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_msap_tugboats_msap_tugboat_owners_tugboat_owner_id",
                table: "msap_tugboats");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_tugboats",
                table: "msap_tugboats");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_tugboat_owners",
                table: "msap_tugboat_owners");

            migrationBuilder.RenameTable(
                name: "msap_tugboats",
                newName: "mmsi_tugboats");

            migrationBuilder.RenameTable(
                name: "msap_tugboat_owners",
                newName: "mmsi_tugboat_owners");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tugboats_tugboat_owner_id",
                table: "mmsi_tugboats",
                newName: "ix_mmsi_tugboats_tugboat_owner_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_tugboats",
                table: "mmsi_tugboats",
                column: "tugboat_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_tugboat_owners",
                table: "mmsi_tugboat_owners",
                column: "tugboat_owner_id");

            migrationBuilder.CreateTable(
                name: "mmsi_tug_masters",
                columns: table => new
                {
                    tug_master_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tug_master_number = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false),
                    tug_master_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_tug_masters", x => x.tug_master_id);
                });

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tugboats_mmsi_tugboat_owners_tugboat_owner_id",
                table: "mmsi_tugboats",
                column: "tugboat_owner_id",
                principalTable: "mmsi_tugboat_owners",
                principalColumn: "tugboat_owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tugboats_mmsi_tugboat_owners_tugboat_owner_id",
                table: "mmsi_tugboats");

            migrationBuilder.DropTable(
                name: "mmsi_tug_masters");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_tugboats",
                table: "mmsi_tugboats");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_tugboat_owners",
                table: "mmsi_tugboat_owners");

            migrationBuilder.RenameTable(
                name: "mmsi_tugboats",
                newName: "msap_tugboats");

            migrationBuilder.RenameTable(
                name: "mmsi_tugboat_owners",
                newName: "msap_tugboat_owners");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tugboats_tugboat_owner_id",
                table: "msap_tugboats",
                newName: "ix_msap_tugboats_tugboat_owner_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_tugboats",
                table: "msap_tugboats",
                column: "tugboat_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_tugboat_owners",
                table: "msap_tugboat_owners",
                column: "tugboat_owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tugboats_msap_tugboat_owners_tugboat_owner_id",
                table: "msap_tugboats",
                column: "tugboat_owner_id",
                principalTable: "msap_tugboat_owners",
                principalColumn: "tugboat_owner_id");
        }
    }
}
