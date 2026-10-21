using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionBoundApprovalEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approved_revision",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_billings",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approved_revision",
                schema: "msap",
                table: "mmsi_billings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "msap",
                table: "mmsi_billings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "approved_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vessel_schedules_approval_evidence",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                sql: "revision >= 1 AND ((approved_revision IS NULL AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (approved_revision IS NOT NULL AND approved_revision BETWEEN 1 AND revision AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "approved_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_billings_approval_evidence",
                schema: "msap",
                table: "mmsi_billings",
                sql: "revision >= 1 AND ((approved_revision IS NULL AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (approved_revision IS NOT NULL AND approved_revision BETWEEN 1 AND revision AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_application_user_approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "approved_by_user_id",
                principalSchema: "public",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_application_user_approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "approved_by_user_id",
                principalSchema: "public",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_application_user_approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_application_user_approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_vessel_schedules_approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vessel_schedules_approval_evidence",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_billings_approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_billings_approval_evidence",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "approved_revision",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "approved_revision",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
