using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTugboatWorkspaceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_service_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_sales_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_receiving_reports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_purchase_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_provisional_receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_monthly_nibits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_journal_voucher_headers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_gl_sub_account_balances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_gl_period_balances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_general_ledger_books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_delivery_receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_debit_memos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_customer_order_slips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_credit_memos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_collection_receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workspace_tugboat_id",
                table: "filpride_check_voucher_headers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_filpride_service_invoices_workspace_tugboat_id",
                table: "filpride_service_invoices",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_sales_invoices_workspace_tugboat_id",
                table: "filpride_sales_invoices",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_receiving_reports_workspace_tugboat_id",
                table: "filpride_receiving_reports",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_purchase_orders_workspace_tugboat_id",
                table: "filpride_purchase_orders",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_provisional_receipts_workspace_tugboat_id",
                table: "filpride_provisional_receipts",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_journal_voucher_headers_workspace_tugboat_id",
                table: "filpride_journal_voucher_headers",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_general_ledger_books_workspace_tugboat_id_date",
                table: "filpride_general_ledger_books",
                columns: new[] { "workspace_tugboat_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_filpride_delivery_receipts_workspace_tugboat_id",
                table: "filpride_delivery_receipts",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_debit_memos_workspace_tugboat_id",
                table: "filpride_debit_memos",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_customer_order_slips_workspace_tugboat_id",
                table: "filpride_customer_order_slips",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_credit_memos_workspace_tugboat_id",
                table: "filpride_credit_memos",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_collection_receipts_workspace_tugboat_id",
                table: "filpride_collection_receipts",
                column: "workspace_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_filpride_check_voucher_headers_workspace_tugboat_id",
                table: "filpride_check_voucher_headers",
                column: "workspace_tugboat_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_filpride_service_invoices_workspace_tugboat_id",
                table: "filpride_service_invoices");

            migrationBuilder.DropIndex(
                name: "ix_filpride_sales_invoices_workspace_tugboat_id",
                table: "filpride_sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_filpride_receiving_reports_workspace_tugboat_id",
                table: "filpride_receiving_reports");

            migrationBuilder.DropIndex(
                name: "ix_filpride_purchase_orders_workspace_tugboat_id",
                table: "filpride_purchase_orders");

            migrationBuilder.DropIndex(
                name: "ix_filpride_provisional_receipts_workspace_tugboat_id",
                table: "filpride_provisional_receipts");

            migrationBuilder.DropIndex(
                name: "ix_filpride_journal_voucher_headers_workspace_tugboat_id",
                table: "filpride_journal_voucher_headers");

            migrationBuilder.DropIndex(
                name: "ix_filpride_general_ledger_books_workspace_tugboat_id_date",
                table: "filpride_general_ledger_books");

            migrationBuilder.DropIndex(
                name: "ix_filpride_delivery_receipts_workspace_tugboat_id",
                table: "filpride_delivery_receipts");

            migrationBuilder.DropIndex(
                name: "ix_filpride_debit_memos_workspace_tugboat_id",
                table: "filpride_debit_memos");

            migrationBuilder.DropIndex(
                name: "ix_filpride_customer_order_slips_workspace_tugboat_id",
                table: "filpride_customer_order_slips");

            migrationBuilder.DropIndex(
                name: "ix_filpride_credit_memos_workspace_tugboat_id",
                table: "filpride_credit_memos");

            migrationBuilder.DropIndex(
                name: "ix_filpride_collection_receipts_workspace_tugboat_id",
                table: "filpride_collection_receipts");

            migrationBuilder.DropIndex(
                name: "ix_filpride_check_voucher_headers_workspace_tugboat_id",
                table: "filpride_check_voucher_headers");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_service_invoices");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_sales_invoices");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_receiving_reports");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_purchase_orders");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_provisional_receipts");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_monthly_nibits");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_journal_voucher_headers");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_gl_sub_account_balances");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_gl_period_balances");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_general_ledger_books");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_delivery_receipts");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_debit_memos");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_customer_order_slips");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_credit_memos");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_collection_receipts");

            migrationBuilder.DropColumn(
                name: "workspace_tugboat_id",
                table: "filpride_check_voucher_headers");
        }
    }
}
