using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class LinkOtherTugPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "filpride_tugboat_payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    check_voucher_header_id = table.Column<int>(type: "integer", nullable: false),
                    other_tug_charge_id = table.Column<int>(type: "integer", nullable: false),
                    tugboat_owner_id = table.Column<int>(type: "integer", nullable: false),
                    liability_journal_voucher_id = table.Column<int>(type: "integer", nullable: false),
                    charge_revision = table.Column<int>(type: "integer", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_snapshot = table.Column<string>(type: "text", nullable: false),
                    liability_snapshot = table.Column<string>(type: "text", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    withholding_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filpride_tugboat_payments", x => x.id);
                    table.CheckConstraint("ck_tugboat_payment_amounts", "gross_amount > 0 AND withholding_amount >= 0 AND withholding_amount < gross_amount");
                    table.ForeignKey(
                        name: "fk_filpride_tugboat_payments_filpride_check_voucher_headers_ch",
                        column: x => x.check_voucher_header_id,
                        principalTable: "filpride_check_voucher_headers",
                        principalColumn: "check_voucher_header_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_filpride_tugboat_payments_filpride_journal_voucher_headers_",
                        column: x => x.liability_journal_voucher_id,
                        principalTable: "filpride_journal_voucher_headers",
                        principalColumn: "journal_voucher_header_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_check_voucher_header_id",
                table: "filpride_tugboat_payments",
                column: "check_voucher_header_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_liability_journal_voucher_id",
                table: "filpride_tugboat_payments",
                column: "liability_journal_voucher_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_other_tug_charge_id",
                table: "filpride_tugboat_payments",
                column: "other_tug_charge_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.AddForeignKey("fk_tugboat_payment_provider_bill", "filpride_tugboat_payments", "other_tug_charge_id",
                principalSchema: "msap", principalTable: "mmsi_other_tug_charges", principalColumn: "other_tug_charge_id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("fk_tugboat_payment_owner", "filpride_tugboat_payments", "tugboat_owner_id",
                principalSchema: "msap", principalTable: "mmsi_tugboat_owners", principalColumn: "tugboat_owner_id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.guard_tugboat_payment_bill() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE old_id integer; new_id integer; source_id integer;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN old_id := OLD.other_tug_charge_id; END IF;
                    IF TG_OP <> 'DELETE' THEN new_id := NEW.other_tug_charge_id; END IF;
                    FOR source_id IN SELECT DISTINCT x FROM unnest(ARRAY[old_id, new_id]) x WHERE x IS NOT NULL ORDER BY x LOOP
                        PERFORM 1 FROM msap.mmsi_other_tug_charges WHERE other_tug_charge_id = source_id FOR UPDATE;
                        IF EXISTS (SELECT 1 FROM public.filpride_tugboat_payments WHERE other_tug_charge_id = source_id AND released_at IS NULL) THEN
                            RAISE EXCEPTION 'Cancel or void the AP Other Tugs payment before changing its provider bill.';
                        END IF;
                    END LOOP;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER guard_tugboat_payment_bill AFTER UPDATE OR DELETE ON msap.mmsi_other_tug_charges
                    FOR EACH ROW EXECUTE FUNCTION public.guard_tugboat_payment_bill();
                CREATE TRIGGER guard_tugboat_payment_bill_lines AFTER INSERT OR UPDATE OR DELETE ON msap.mmsi_other_tug_charge_lines
                    FOR EACH ROW EXECUTE FUNCTION public.guard_tugboat_payment_bill();

                CREATE FUNCTION public.guard_tugboat_payment_liability() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE old_id integer; new_id integer; source_id integer;
                BEGIN
                    IF TG_TABLE_NAME = 'filpride_journal_voucher_headers' AND TG_OP = 'UPDATE'
                       AND (to_jsonb(OLD) - 'is_printed') = (to_jsonb(NEW) - 'is_printed') THEN RETURN NULL; END IF;
                    IF TG_OP <> 'INSERT' THEN old_id := OLD.journal_voucher_header_id; END IF;
                    IF TG_OP <> 'DELETE' THEN new_id := NEW.journal_voucher_header_id; END IF;
                    FOR source_id IN SELECT DISTINCT x FROM unnest(ARRAY[old_id, new_id]) x WHERE x IS NOT NULL ORDER BY x LOOP
                        PERFORM 1 FROM public.filpride_journal_voucher_headers WHERE journal_voucher_header_id = source_id FOR UPDATE;
                        IF EXISTS (SELECT 1 FROM public.filpride_tugboat_payments WHERE liability_journal_voucher_id = source_id AND released_at IS NULL) THEN
                            RAISE EXCEPTION 'Cancel or void the AP Other Tugs payment before changing its liability journal.';
                        END IF;
                    END LOOP;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER guard_tugboat_payment_liability AFTER UPDATE OR DELETE ON public.filpride_journal_voucher_headers
                    FOR EACH ROW EXECUTE FUNCTION public.guard_tugboat_payment_liability();
                CREATE TRIGGER guard_tugboat_payment_liability_lines AFTER INSERT OR UPDATE OR DELETE ON public.filpride_journal_voucher_details
                    FOR EACH ROW EXECUTE FUNCTION public.guard_tugboat_payment_liability();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER guard_tugboat_payment_bill ON msap.mmsi_other_tug_charges;
                DROP TRIGGER guard_tugboat_payment_bill_lines ON msap.mmsi_other_tug_charge_lines;
                DROP FUNCTION public.guard_tugboat_payment_bill();
                DROP TRIGGER guard_tugboat_payment_liability ON public.filpride_journal_voucher_headers;
                DROP TRIGGER guard_tugboat_payment_liability_lines ON public.filpride_journal_voucher_details;
                DROP FUNCTION public.guard_tugboat_payment_liability();
                """);
            migrationBuilder.DropTable(
                name: "filpride_tugboat_payments");
        }
    }
}
