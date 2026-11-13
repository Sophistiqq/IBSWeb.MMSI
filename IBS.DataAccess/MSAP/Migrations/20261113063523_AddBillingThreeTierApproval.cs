using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingThreeTierApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_review_billing",
                schema: "msap",
                table: "mmsi_user_accesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "adjustment_attachment_url",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                schema: "msap",
                table: "mmsi_billings",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejected_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "msap",
                table: "mmsi_billings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                schema: "msap",
                table: "mmsi_billings",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reviewed_revision",
                schema: "msap",
                table: "mmsi_billings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_rejected_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "rejected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_reviewed_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "reviewed_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_billings_review_evidence",
                schema: "msap",
                table: "mmsi_billings",
                sql: "((reviewed_revision IS NULL AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL) OR (reviewed_revision IS NOT NULL AND reviewed_revision BETWEEN 1 AND revision AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_application_user_rejected_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "rejected_by_user_id",
                principalSchema: "public",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_application_user_reviewed_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "reviewed_by_user_id",
                principalSchema: "public",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_application_user_rejected_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_application_user_reviewed_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_billings_rejected_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropIndex(
                name: "ix_mmsi_billings_reviewed_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_billings_review_evidence",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "can_review_billing",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropColumn(
                name: "adjustment_attachment_url",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "rejected_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropColumn(
                name: "reviewed_revision",
                schema: "msap",
                table: "mmsi_billings");
        }
    }
}
