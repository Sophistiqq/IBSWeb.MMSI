using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AllowTicketSelectionForTugboatPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_filpride_tugboat_payments_check_voucher_header_id",
                table: "filpride_tugboat_payments");

            migrationBuilder.DropIndex(
                name: "ix_filpride_tugboat_payments_other_tug_charge_id",
                table: "filpride_tugboat_payments");

            migrationBuilder.CreateTable(
                name: "filpride_tugboat_payment_allocations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tugboat_payment_id = table.Column<int>(type: "integer", nullable: false),
                    other_tug_charge_line_id = table.Column<int>(type: "integer", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filpride_tugboat_payment_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_filpride_tugboat_payment_allocations_filpride_tugboat_payme",
                        column: x => x.tugboat_payment_id,
                        principalTable: "filpride_tugboat_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_check_voucher_header_id",
                table: "filpride_tugboat_payments",
                column: "check_voucher_header_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_other_tug_charge_id",
                table: "filpride_tugboat_payments",
                column: "other_tug_charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payment_allocations_other_tug_charge_line_",
                table: "filpride_tugboat_payment_allocations",
                column: "other_tug_charge_line_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payment_allocations_tugboat_payment_id",
                table: "filpride_tugboat_payment_allocations",
                column: "tugboat_payment_id");

            migrationBuilder.AddForeignKey("fk_tugboat_payment_allocation_source", "filpride_tugboat_payment_allocations", "other_tug_charge_line_id",
                principalSchema: "msap", principalTable: "mmsi_other_tug_charge_lines", principalColumn: "other_tug_charge_line_id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                INSERT INTO public.filpride_tugboat_payment_allocations (tugboat_payment_id, other_tug_charge_line_id, released_at)
                SELECT p.id, (line->>'AllocationId')::integer, p.released_at
                FROM public.filpride_tugboat_payments p
                CROSS JOIN LATERAL jsonb_array_elements(p.source_snapshot::jsonb->'Lines') line;

                CREATE OR REPLACE FUNCTION public.guard_tugboat_payment_bill() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE old_id integer; new_id integer; source_id integer;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN old_id := OLD.other_tug_charge_id; END IF;
                    IF TG_OP <> 'DELETE' THEN new_id := NEW.other_tug_charge_id; END IF;
                    FOR source_id IN SELECT DISTINCT x FROM unnest(ARRAY[old_id, new_id]) x WHERE x IS NOT NULL ORDER BY x LOOP
                        PERFORM 1 FROM msap.mmsi_other_tug_charges WHERE other_tug_charge_id = source_id FOR UPDATE;
                        IF EXISTS (SELECT 1 FROM public.filpride_tugboat_payments p
                            JOIN public.filpride_tugboat_payment_allocations a ON a.tugboat_payment_id = p.id
                            WHERE p.other_tug_charge_id = source_id AND a.released_at IS NULL) THEN
                            RAISE EXCEPTION 'Cancel or void the AP Other Tugs payments before changing their provider bill.';
                        END IF;
                    END LOOP;
                    RETURN NULL;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM public.filpride_tugboat_payments GROUP BY check_voucher_header_id HAVING count(*) > 1)
                       OR EXISTS (SELECT 1 FROM public.filpride_tugboat_payments p
                           WHERE (SELECT count(*) FROM public.filpride_tugboat_payment_allocations a WHERE a.tugboat_payment_id = p.id)
                               <> jsonb_array_length(p.source_snapshot::jsonb->'Lines')) THEN
                        RAISE EXCEPTION 'Cannot restore whole-bill payments while multi-bill or selected-ticket vouchers exist.';
                    END IF;
                END $$;
                CREATE OR REPLACE FUNCTION public.guard_tugboat_payment_bill() RETURNS trigger LANGUAGE plpgsql AS $$
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
                """);
            migrationBuilder.DropTable(
                name: "filpride_tugboat_payment_allocations");

            migrationBuilder.DropIndex(
                name: "ix_filpride_tugboat_payments_check_voucher_header_id",
                table: "filpride_tugboat_payments");

            migrationBuilder.DropIndex(
                name: "ix_filpride_tugboat_payments_other_tug_charge_id",
                table: "filpride_tugboat_payments");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_check_voucher_header_id",
                table: "filpride_tugboat_payments",
                column: "check_voucher_header_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_filpride_tugboat_payments_other_tug_charge_id",
                table: "filpride_tugboat_payments",
                column: "other_tug_charge_id",
                unique: true,
                filter: "released_at IS NULL");
        }
    }
}
