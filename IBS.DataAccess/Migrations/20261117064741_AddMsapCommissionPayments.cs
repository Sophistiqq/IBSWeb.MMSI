using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMsapCommissionPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "filpride_msap_commission_payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    check_voucher_header_id = table.Column<int>(type: "integer", nullable: false),
                    msap_billing_id = table.Column<int>(type: "integer", nullable: false),
                    representative_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    billing_revision = table.Column<int>(type: "integer", nullable: false),
                    billing_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_snapshot = table.Column<string>(type: "text", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    withholding_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filpride_msap_commission_payments", x => x.id);
                    table.CheckConstraint("ck_msap_commission_payment_amounts", "gross_amount > 0 AND withholding_amount >= 0 AND withholding_amount < gross_amount");
                    table.CheckConstraint("ck_msap_commission_payment_category", "category IN ('AgencyIncentive', 'AgentCommission')");
                    table.ForeignKey(
                        name: "fk_filpride_msap_commission_payments_filpride_check_voucher_he",
                        column: x => x.check_voucher_header_id,
                        principalTable: "filpride_check_voucher_headers",
                        principalColumn: "check_voucher_header_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_filpride_msap_commission_payments_check_voucher_header_id",
                table: "filpride_msap_commission_payments",
                column: "check_voucher_header_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_msap_commission_payments_msap_billing_id_category",
                table: "filpride_msap_commission_payments",
                columns: new[] { "msap_billing_id", "category" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.AddForeignKey(
                "fk_msap_commission_payment_billing",
                "filpride_msap_commission_payments",
                "msap_billing_id",
                principalSchema: "msap",
                principalTable: "mmsi_billings",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                "fk_msap_commission_payment_representative",
                "filpride_msap_commission_payments",
                "representative_id",
                principalSchema: "msap",
                principalTable: "mmsi_representatives",
                principalColumn: "representative_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION public.guard_msap_commission_payment_billing() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE source_id integer;
                BEGIN
                    IF TG_OP = 'UPDATE'
                       AND OLD."NUMBER" IS NOT DISTINCT FROM NEW."NUMBER"
                       AND OLD.revision IS NOT DISTINCT FROM NEW.revision
                       AND OLD.approved_revision IS NOT DISTINCT FROM NEW.approved_revision
                       AND OLD.agency_incentive_representative_id IS NOT DISTINCT FROM NEW.agency_incentive_representative_id
                       AND OLD.agency_incentive_amount IS NOT DISTINCT FROM NEW.agency_incentive_amount
                       AND OLD.agent_commission_representative_id IS NOT DISTINCT FROM NEW.agent_commission_representative_id
                       AND OLD.agent_commission_amount IS NOT DISTINCT FROM NEW.agent_commission_amount
                       AND NEW.status IN ('For Collection', 'Collected') THEN
                        RETURN NULL;
                    END IF;
                    source_id := OLD."RECID";
                    PERFORM 1 FROM msap.mmsi_billings WHERE "RECID" = source_id FOR UPDATE;
                    IF EXISTS (SELECT 1 FROM public.filpride_msap_commission_payments WHERE msap_billing_id = source_id AND released_at IS NULL) THEN
                        RAISE EXCEPTION 'Cancel or void the MSAP Commission Check Voucher before changing or reopening its billing.';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER guard_msap_commission_payment_billing AFTER UPDATE OR DELETE ON msap.mmsi_billings
                    FOR EACH ROW EXECUTE FUNCTION public.guard_msap_commission_payment_billing();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER guard_msap_commission_payment_billing ON msap.mmsi_billings;
                DROP FUNCTION public.guard_msap_commission_payment_billing();
                """);
            migrationBuilder.DropTable(
                name: "filpride_msap_commission_payments");
        }
    }
}
