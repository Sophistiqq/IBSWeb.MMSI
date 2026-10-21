using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    public partial class AddRateUploadStaging : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mmsi_rate_uploads",
                schema: "msap",
                columns: table => new
                {
                    rate_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uploaded_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    changed_rows = table.Column<int>(type: "integer", nullable: false),
                    rows_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table => table.PrimaryKey("pk_mmsi_rate_uploads", upload => upload.rate_upload_id));

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_rate_uploads_company_uploaded_at",
                schema: "msap",
                table: "mmsi_rate_uploads",
                columns: new[] { "company", "uploaded_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "mmsi_rate_uploads", schema: "msap");
        }
    }
}
