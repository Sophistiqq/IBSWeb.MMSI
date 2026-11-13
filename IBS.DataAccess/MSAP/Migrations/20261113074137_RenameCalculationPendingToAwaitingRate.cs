using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class RenameCalculationPendingToAwaitingRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE msap.mmsi_dispatch_tickets SET status = 'Awaiting Rate' WHERE status = 'Calculation pending';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE msap.mmsi_dispatch_tickets SET status = 'Calculation pending' WHERE status = 'Awaiting Rate';");
        }
    }
}
