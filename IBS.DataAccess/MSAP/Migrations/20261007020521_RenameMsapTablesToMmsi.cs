using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class RenameMsapTablesToMmsi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_customers_custno_fk",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_msap_collections_crnum",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_msap_job_orders_job_order_id",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_msap_ports_portnum",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_msap_principals_principal_id",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_msap_terminals_terminal",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_billings_msap_vessels_vesselnum",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_collection_bills_customers_customer_id",
                schema: "msap",
                table: "msap_collection_bills");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_collection_bills_msap_billings_billing_id",
                schema: "msap",
                table: "msap_collection_bills");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_collection_bills_msap_collections_collection_id",
                schema: "msap",
                table: "msap_collection_bills");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_collections_bank_accounts_bankacctco",
                schema: "msap",
                table: "msap_collections");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_collections_customers_custno",
                schema: "msap",
                table: "msap_collections");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_customers_custno",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_billings_billnum",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_job_orders_job_order_id",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_ports_portnum",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_services_service_id",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_terminals_terminal",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_tug_masters_masterno",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_tugboats_tugnum",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_dispatch_tickets_msap_vessels_vesselnum",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_job_orders_customers_customer_id",
                schema: "msap",
                table: "msap_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_job_orders_msap_ports_port_id",
                schema: "msap",
                table: "msap_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_job_orders_msap_terminals_terminal_id",
                schema: "msap",
                table: "msap_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_job_orders_msap_tugboats_preferred_tugboat_id",
                schema: "msap",
                table: "msap_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_job_orders_msap_vessels_vessel_id",
                schema: "msap",
                table: "msap_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_principals_customers_customer_id",
                schema: "msap",
                table: "msap_principals");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_tariff_rates_customers_customer_id",
                schema: "msap",
                table: "msap_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_tariff_rates_msap_ports_port_id",
                schema: "msap",
                table: "msap_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_tariff_rates_msap_services_service_id",
                schema: "msap",
                table: "msap_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_tariff_rates_msap_terminals_terminal_id",
                schema: "msap",
                table: "msap_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_terminals_msap_ports_port_id",
                schema: "msap",
                table: "msap_terminals");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_tugboats_msap_ports_port_id",
                schema: "msap",
                table: "msap_tugboats");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_tugboats_msap_tugboat_owners_tugboat_owner_id",
                schema: "msap",
                table: "msap_tugboats");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_vessel_schedules_msap_job_orders_job_order_id",
                schema: "msap",
                table: "msap_vessel_schedules");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_vessel_schedules_msap_ports_port_id",
                schema: "msap",
                table: "msap_vessel_schedules");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_vessel_schedules_msap_terminals_terminal_id",
                schema: "msap",
                table: "msap_vessel_schedules");

            migrationBuilder.DropForeignKey(
                name: "fk_msap_vessel_schedules_msap_vessels_vessel_id",
                schema: "msap",
                table: "msap_vessel_schedules");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_vessels",
                schema: "msap",
                table: "msap_vessels");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_vessel_schedules",
                schema: "msap",
                table: "msap_vessel_schedules");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_user_accesses",
                schema: "msap",
                table: "msap_user_accesses");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_tugboats",
                schema: "msap",
                table: "msap_tugboats");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_tugboat_owners",
                schema: "msap",
                table: "msap_tugboat_owners");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_tug_masters",
                schema: "msap",
                table: "msap_tug_masters");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_terminals",
                schema: "msap",
                table: "msap_terminals");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_tariff_rates",
                schema: "msap",
                table: "msap_tariff_rates");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_services",
                schema: "msap",
                table: "msap_services");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_principals",
                schema: "msap",
                table: "msap_principals");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_posted_periods",
                schema: "msap",
                table: "msap_posted_periods");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_ports",
                schema: "msap",
                table: "msap_ports");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_job_orders",
                schema: "msap",
                table: "msap_job_orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_dispatch_tickets",
                schema: "msap",
                table: "msap_dispatch_tickets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_collections",
                schema: "msap",
                table: "msap_collections");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_collection_bills",
                schema: "msap",
                table: "msap_collection_bills");

            migrationBuilder.DropPrimaryKey(
                name: "pk_msap_billings",
                schema: "msap",
                table: "msap_billings");

            migrationBuilder.RenameTable(
                name: "msap_vessels",
                schema: "msap",
                newName: "mmsi_vessels",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_vessel_schedules",
                schema: "msap",
                newName: "mmsi_vessel_schedules",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_user_accesses",
                schema: "msap",
                newName: "mmsi_user_accesses",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_tugboats",
                schema: "msap",
                newName: "mmsi_tugboats",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_tugboat_owners",
                schema: "msap",
                newName: "mmsi_tugboat_owners",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_tug_masters",
                schema: "msap",
                newName: "mmsi_tug_masters",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_terminals",
                schema: "msap",
                newName: "mmsi_terminals",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_tariff_rates",
                schema: "msap",
                newName: "mmsi_tariff_rates",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_services",
                schema: "msap",
                newName: "mmsi_services",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_principals",
                schema: "msap",
                newName: "mmsi_principals",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_posted_periods",
                schema: "msap",
                newName: "mmsi_posted_periods",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_ports",
                schema: "msap",
                newName: "mmsi_ports",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_job_orders",
                schema: "msap",
                newName: "mmsi_job_orders",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_dispatch_tickets",
                schema: "msap",
                newName: "mmsi_dispatch_tickets",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_collections",
                schema: "msap",
                newName: "mmsi_collections",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_collection_bills",
                schema: "msap",
                newName: "mmsi_collection_bills",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "msap_billings",
                schema: "msap",
                newName: "mmsi_billings",
                newSchema: "msap");

            migrationBuilder.RenameIndex(
                name: "ix_msap_vessel_schedules_vessel_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_vessel_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_vessel_schedules_terminal_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_vessel_schedules_status",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_status");

            migrationBuilder.RenameIndex(
                name: "ix_msap_vessel_schedules_port_id_terminal_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_port_id_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_vessel_schedules_planned_start",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_planned_start");

            migrationBuilder.RenameIndex(
                name: "ix_msap_vessel_schedules_job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tugboats_tugboat_owner_id",
                schema: "msap",
                table: "mmsi_tugboats",
                newName: "ix_mmsi_tugboats_tugboat_owner_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tugboats_port_id",
                schema: "msap",
                table: "mmsi_tugboats",
                newName: "ix_mmsi_tugboats_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_terminals_port_id",
                schema: "msap",
                table: "mmsi_terminals",
                newName: "ix_mmsi_terminals_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tariff_rates_terminal_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                newName: "ix_mmsi_tariff_rates_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tariff_rates_service_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                newName: "ix_mmsi_tariff_rates_service_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tariff_rates_port_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                newName: "ix_mmsi_tariff_rates_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_tariff_rates_customer_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                newName: "ix_mmsi_tariff_rates_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_principals_customer_id",
                schema: "msap",
                table: "mmsi_principals",
                newName: "ix_mmsi_principals_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_posted_periods_year_month",
                schema: "msap",
                table: "mmsi_posted_periods",
                newName: "ix_mmsi_posted_periods_year_month");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_vessel_id",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_vessel_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_terminal_id",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_status",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_status");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_preferred_tugboat_id",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_preferred_tugboat_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_port_id",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_job_order_number",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_job_order_number");

            migrationBuilder.RenameIndex(
                name: "ix_msap_job_orders_customer_id",
                schema: "msap",
                table: "mmsi_job_orders",
                newName: "ix_mmsi_job_orders_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_vesselnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_vesselnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_tugnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_tugnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_terminal",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_terminal");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_status",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_status");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_service_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_service_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_portnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_portnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_masterno",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_masterno");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_custno",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_custno");

            migrationBuilder.RenameIndex(
                name: "ix_msap_dispatch_tickets_billnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_billnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collections_custno",
                schema: "msap",
                table: "mmsi_collections",
                newName: "ix_mmsi_collections_custno");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collections_crnum_company",
                schema: "msap",
                table: "mmsi_collections",
                newName: "ix_mmsi_collections_crnum_company");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collections_crdate",
                schema: "msap",
                table: "mmsi_collections",
                newName: "ix_mmsi_collections_crdate");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collections_bankacctco",
                schema: "msap",
                table: "mmsi_collections",
                newName: "ix_mmsi_collections_bankacctco");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collection_bills_customer_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                newName: "ix_mmsi_collection_bills_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collection_bills_collection_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                newName: "ix_mmsi_collection_bills_collection_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_collection_bills_billing_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                newName: "ix_mmsi_collection_bills_billing_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_vesselnum",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_vesselnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_terminal",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_terminal");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_status",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_status");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_principal_id",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_principal_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_portnum",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_portnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_job_order_id",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_date",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_date");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_custno_fk",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_custno_fk");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_crnum",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_crnum");

            migrationBuilder.RenameIndex(
                name: "ix_msap_billings_billing_year_number_company",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_billing_year_number_company");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_vessels",
                schema: "msap",
                table: "mmsi_vessels",
                column: "vessel_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_vessel_schedules",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "vessel_schedule_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_user_accesses",
                schema: "msap",
                table: "mmsi_user_accesses",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_tugboats",
                schema: "msap",
                table: "mmsi_tugboats",
                column: "tugboat_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_tugboat_owners",
                schema: "msap",
                table: "mmsi_tugboat_owners",
                column: "tugboat_owner_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_tug_masters",
                schema: "msap",
                table: "mmsi_tug_masters",
                column: "tug_master_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_terminals",
                schema: "msap",
                table: "mmsi_terminals",
                column: "terminal_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_tariff_rates",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "tariff_rate_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_services",
                schema: "msap",
                table: "mmsi_services",
                column: "service_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_principals",
                schema: "msap",
                table: "mmsi_principals",
                column: "principal_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_posted_periods",
                schema: "msap",
                table: "mmsi_posted_periods",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_ports",
                schema: "msap",
                table: "mmsi_ports",
                column: "port_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_job_orders",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "job_order_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_dispatch_tickets",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "RECID");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_collections",
                schema: "msap",
                table: "mmsi_collections",
                column: "RECID");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_collection_bills",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "collection_bill_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_mmsi_billings",
                schema: "msap",
                table: "mmsi_billings",
                column: "RECID");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_customers_custno_fk",
                schema: "msap",
                table: "mmsi_billings",
                column: "CUSTNO_FK",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "mmsi_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_collections_crnum",
                schema: "msap",
                table: "mmsi_billings",
                column: "CRNUM",
                principalSchema: "msap",
                principalTable: "mmsi_collections",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_ports_portnum",
                schema: "msap",
                table: "mmsi_billings",
                column: "PORTNUM",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_principals_principal_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "principal_id",
                principalSchema: "msap",
                principalTable: "mmsi_principals",
                principalColumn: "principal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_terminals_terminal",
                schema: "msap",
                table: "mmsi_billings",
                column: "TERMINAL",
                principalSchema: "msap",
                principalTable: "mmsi_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_msap_vessels_vesselnum",
                schema: "msap",
                table: "mmsi_billings",
                column: "VESSELNUM",
                principalSchema: "msap",
                principalTable: "mmsi_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_collection_bills_customers_customer_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_collection_bills_mmsi_billings_billing_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "billing_id",
                principalSchema: "msap",
                principalTable: "mmsi_billings",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_collection_bills_mmsi_collections_collection_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "collection_id",
                principalSchema: "msap",
                principalTable: "mmsi_collections",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_collections_bank_accounts_bankacctco",
                schema: "msap",
                table: "mmsi_collections",
                column: "BANKACCTCO",
                principalSchema: "msap",
                principalTable: "bank_accounts",
                principalColumn: "bank_account_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_collections_customers_custno",
                schema: "msap",
                table: "mmsi_collections",
                column: "CUSTNO",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_customers_custno",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "CUSTNO",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "mmsi_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_billings_billnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "BILLNUM",
                principalSchema: "msap",
                principalTable: "mmsi_billings",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_ports_portnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "PORTNUM",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_services_service_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "service_id",
                principalSchema: "msap",
                principalTable: "mmsi_services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_terminals_terminal",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "TERMINAL",
                principalSchema: "msap",
                principalTable: "mmsi_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_tug_masters_masterno",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "MASTERNO",
                principalSchema: "msap",
                principalTable: "mmsi_tug_masters",
                principalColumn: "tug_master_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_tugboats_tugnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "TUGNUM",
                principalSchema: "msap",
                principalTable: "mmsi_tugboats",
                principalColumn: "tugboat_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_vessels_vesselnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "VESSELNUM",
                principalSchema: "msap",
                principalTable: "mmsi_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_job_orders_customers_customer_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_job_orders_msap_ports_port_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_job_orders_msap_terminals_terminal_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "terminal_id",
                principalSchema: "msap",
                principalTable: "mmsi_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_job_orders_msap_tugboats_preferred_tugboat_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "preferred_tugboat_id",
                principalSchema: "msap",
                principalTable: "mmsi_tugboats",
                principalColumn: "tugboat_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_job_orders_msap_vessels_vessel_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "vessel_id",
                principalSchema: "msap",
                principalTable: "mmsi_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_principals_customers_customer_id",
                schema: "msap",
                table: "mmsi_principals",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tariff_rates_customers_customer_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tariff_rates_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tariff_rates_mmsi_services_service_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "service_id",
                principalSchema: "msap",
                principalTable: "mmsi_services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tariff_rates_mmsi_terminals_terminal_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "terminal_id",
                principalSchema: "msap",
                principalTable: "mmsi_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_terminals_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_terminals",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tugboats_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_tugboats",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_tugboats_msap_tugboat_owners_tugboat_owner_id",
                schema: "msap",
                table: "mmsi_tugboats",
                column: "tugboat_owner_id",
                principalSchema: "msap",
                principalTable: "mmsi_tugboat_owners",
                principalColumn: "tugboat_owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "mmsi_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "mmsi_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_terminals_terminal_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "terminal_id",
                principalSchema: "msap",
                principalTable: "mmsi_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_vessels_vessel_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "vessel_id",
                principalSchema: "msap",
                principalTable: "mmsi_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_customers_custno_fk",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_collections_crnum",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_ports_portnum",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_principals_principal_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_terminals_terminal",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_msap_vessels_vesselnum",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_collection_bills_customers_customer_id",
                schema: "msap",
                table: "mmsi_collection_bills");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_collection_bills_mmsi_billings_billing_id",
                schema: "msap",
                table: "mmsi_collection_bills");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_collection_bills_mmsi_collections_collection_id",
                schema: "msap",
                table: "mmsi_collection_bills");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_collections_bank_accounts_bankacctco",
                schema: "msap",
                table: "mmsi_collections");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_collections_customers_custno",
                schema: "msap",
                table: "mmsi_collections");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_customers_custno",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_billings_billnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_ports_portnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_services_service_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_terminals_terminal",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_tug_masters_masterno",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_tugboats_tugnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_msap_vessels_vesselnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_job_orders_customers_customer_id",
                schema: "msap",
                table: "mmsi_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_job_orders_msap_ports_port_id",
                schema: "msap",
                table: "mmsi_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_job_orders_msap_terminals_terminal_id",
                schema: "msap",
                table: "mmsi_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_job_orders_msap_tugboats_preferred_tugboat_id",
                schema: "msap",
                table: "mmsi_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_job_orders_msap_vessels_vessel_id",
                schema: "msap",
                table: "mmsi_job_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_principals_customers_customer_id",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tariff_rates_customers_customer_id",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tariff_rates_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tariff_rates_mmsi_services_service_id",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tariff_rates_mmsi_terminals_terminal_id",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_terminals_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_terminals");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tugboats_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_tugboats");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_tugboats_msap_tugboat_owners_tugboat_owner_id",
                schema: "msap",
                table: "mmsi_tugboats");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_ports_port_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_terminals_terminal_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_vessels_vessel_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_vessels",
                schema: "msap",
                table: "mmsi_vessels");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_vessel_schedules",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_user_accesses",
                schema: "msap",
                table: "mmsi_user_accesses");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_tugboats",
                schema: "msap",
                table: "mmsi_tugboats");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_tugboat_owners",
                schema: "msap",
                table: "mmsi_tugboat_owners");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_tug_masters",
                schema: "msap",
                table: "mmsi_tug_masters");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_terminals",
                schema: "msap",
                table: "mmsi_terminals");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_tariff_rates",
                schema: "msap",
                table: "mmsi_tariff_rates");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_services",
                schema: "msap",
                table: "mmsi_services");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_principals",
                schema: "msap",
                table: "mmsi_principals");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_posted_periods",
                schema: "msap",
                table: "mmsi_posted_periods");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_ports",
                schema: "msap",
                table: "mmsi_ports");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_job_orders",
                schema: "msap",
                table: "mmsi_job_orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_dispatch_tickets",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_collections",
                schema: "msap",
                table: "mmsi_collections");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_collection_bills",
                schema: "msap",
                table: "mmsi_collection_bills");

            migrationBuilder.DropPrimaryKey(
                name: "pk_mmsi_billings",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.RenameTable(
                name: "mmsi_vessels",
                schema: "msap",
                newName: "msap_vessels",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_vessel_schedules",
                schema: "msap",
                newName: "msap_vessel_schedules",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_user_accesses",
                schema: "msap",
                newName: "msap_user_accesses",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_tugboats",
                schema: "msap",
                newName: "msap_tugboats",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_tugboat_owners",
                schema: "msap",
                newName: "msap_tugboat_owners",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_tug_masters",
                schema: "msap",
                newName: "msap_tug_masters",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_terminals",
                schema: "msap",
                newName: "msap_terminals",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_tariff_rates",
                schema: "msap",
                newName: "msap_tariff_rates",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_services",
                schema: "msap",
                newName: "msap_services",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_principals",
                schema: "msap",
                newName: "msap_principals",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_posted_periods",
                schema: "msap",
                newName: "msap_posted_periods",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_ports",
                schema: "msap",
                newName: "msap_ports",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_job_orders",
                schema: "msap",
                newName: "msap_job_orders",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_dispatch_tickets",
                schema: "msap",
                newName: "msap_dispatch_tickets",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_collections",
                schema: "msap",
                newName: "msap_collections",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_collection_bills",
                schema: "msap",
                newName: "msap_collection_bills",
                newSchema: "msap");

            migrationBuilder.RenameTable(
                name: "mmsi_billings",
                schema: "msap",
                newName: "msap_billings",
                newSchema: "msap");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_vessel_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                newName: "ix_msap_vessel_schedules_vessel_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_terminal_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                newName: "ix_msap_vessel_schedules_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_status",
                schema: "msap",
                table: "msap_vessel_schedules",
                newName: "ix_msap_vessel_schedules_status");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_port_id_terminal_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                newName: "ix_msap_vessel_schedules_port_id_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_planned_start",
                schema: "msap",
                table: "msap_vessel_schedules",
                newName: "ix_msap_vessel_schedules_planned_start");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_job_order_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                newName: "ix_msap_vessel_schedules_job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tugboats_tugboat_owner_id",
                schema: "msap",
                table: "msap_tugboats",
                newName: "ix_msap_tugboats_tugboat_owner_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tugboats_port_id",
                schema: "msap",
                table: "msap_tugboats",
                newName: "ix_msap_tugboats_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_terminals_port_id",
                schema: "msap",
                table: "msap_terminals",
                newName: "ix_msap_terminals_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tariff_rates_terminal_id",
                schema: "msap",
                table: "msap_tariff_rates",
                newName: "ix_msap_tariff_rates_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tariff_rates_service_id",
                schema: "msap",
                table: "msap_tariff_rates",
                newName: "ix_msap_tariff_rates_service_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tariff_rates_port_id",
                schema: "msap",
                table: "msap_tariff_rates",
                newName: "ix_msap_tariff_rates_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_tariff_rates_customer_id",
                schema: "msap",
                table: "msap_tariff_rates",
                newName: "ix_msap_tariff_rates_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_principals_customer_id",
                schema: "msap",
                table: "msap_principals",
                newName: "ix_msap_principals_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_posted_periods_year_month",
                schema: "msap",
                table: "msap_posted_periods",
                newName: "ix_msap_posted_periods_year_month");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_vessel_id",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_vessel_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_terminal_id",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_terminal_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_status",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_status");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_preferred_tugboat_id",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_preferred_tugboat_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_port_id",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_port_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_job_order_number",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_job_order_number");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_job_orders_customer_id",
                schema: "msap",
                table: "msap_job_orders",
                newName: "ix_msap_job_orders_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_vesselnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_vesselnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_tugnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_tugnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_terminal",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_terminal");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_status",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_status");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_service_id",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_service_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_portnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_portnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_masterno",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_masterno");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_job_order_id",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_custno",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_custno");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_billnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                newName: "ix_msap_dispatch_tickets_billnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collections_custno",
                schema: "msap",
                table: "msap_collections",
                newName: "ix_msap_collections_custno");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collections_crnum_company",
                schema: "msap",
                table: "msap_collections",
                newName: "ix_msap_collections_crnum_company");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collections_crdate",
                schema: "msap",
                table: "msap_collections",
                newName: "ix_msap_collections_crdate");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collections_bankacctco",
                schema: "msap",
                table: "msap_collections",
                newName: "ix_msap_collections_bankacctco");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collection_bills_customer_id",
                schema: "msap",
                table: "msap_collection_bills",
                newName: "ix_msap_collection_bills_customer_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collection_bills_collection_id",
                schema: "msap",
                table: "msap_collection_bills",
                newName: "ix_msap_collection_bills_collection_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_collection_bills_billing_id",
                schema: "msap",
                table: "msap_collection_bills",
                newName: "ix_msap_collection_bills_billing_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_vesselnum",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_vesselnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_terminal",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_terminal");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_status",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_status");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_principal_id",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_principal_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_portnum",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_portnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_job_order_id",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_date",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_date");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_custno_fk",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_custno_fk");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_crnum",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_crnum");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_billing_year_number_company",
                schema: "msap",
                table: "msap_billings",
                newName: "ix_msap_billings_billing_year_number_company");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_vessels",
                schema: "msap",
                table: "msap_vessels",
                column: "vessel_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_vessel_schedules",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "vessel_schedule_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_user_accesses",
                schema: "msap",
                table: "msap_user_accesses",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_tugboats",
                schema: "msap",
                table: "msap_tugboats",
                column: "tugboat_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_tugboat_owners",
                schema: "msap",
                table: "msap_tugboat_owners",
                column: "tugboat_owner_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_tug_masters",
                schema: "msap",
                table: "msap_tug_masters",
                column: "tug_master_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_terminals",
                schema: "msap",
                table: "msap_terminals",
                column: "terminal_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_tariff_rates",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "tariff_rate_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_services",
                schema: "msap",
                table: "msap_services",
                column: "service_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_principals",
                schema: "msap",
                table: "msap_principals",
                column: "principal_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_posted_periods",
                schema: "msap",
                table: "msap_posted_periods",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_ports",
                schema: "msap",
                table: "msap_ports",
                column: "port_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_job_orders",
                schema: "msap",
                table: "msap_job_orders",
                column: "job_order_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_dispatch_tickets",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "RECID");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_collections",
                schema: "msap",
                table: "msap_collections",
                column: "RECID");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_collection_bills",
                schema: "msap",
                table: "msap_collection_bills",
                column: "collection_bill_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_msap_billings",
                schema: "msap",
                table: "msap_billings",
                column: "RECID");

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_customers_custno_fk",
                schema: "msap",
                table: "msap_billings",
                column: "CUSTNO_FK",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_msap_collections_crnum",
                schema: "msap",
                table: "msap_billings",
                column: "CRNUM",
                principalSchema: "msap",
                principalTable: "msap_collections",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_msap_job_orders_job_order_id",
                schema: "msap",
                table: "msap_billings",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "msap_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_msap_ports_portnum",
                schema: "msap",
                table: "msap_billings",
                column: "PORTNUM",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_msap_principals_principal_id",
                schema: "msap",
                table: "msap_billings",
                column: "principal_id",
                principalSchema: "msap",
                principalTable: "msap_principals",
                principalColumn: "principal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_msap_terminals_terminal",
                schema: "msap",
                table: "msap_billings",
                column: "TERMINAL",
                principalSchema: "msap",
                principalTable: "msap_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_billings_msap_vessels_vesselnum",
                schema: "msap",
                table: "msap_billings",
                column: "VESSELNUM",
                principalSchema: "msap",
                principalTable: "msap_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_collection_bills_customers_customer_id",
                schema: "msap",
                table: "msap_collection_bills",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_collection_bills_msap_billings_billing_id",
                schema: "msap",
                table: "msap_collection_bills",
                column: "billing_id",
                principalSchema: "msap",
                principalTable: "msap_billings",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_collection_bills_msap_collections_collection_id",
                schema: "msap",
                table: "msap_collection_bills",
                column: "collection_id",
                principalSchema: "msap",
                principalTable: "msap_collections",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_collections_bank_accounts_bankacctco",
                schema: "msap",
                table: "msap_collections",
                column: "BANKACCTCO",
                principalSchema: "msap",
                principalTable: "bank_accounts",
                principalColumn: "bank_account_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_collections_customers_custno",
                schema: "msap",
                table: "msap_collections",
                column: "CUSTNO",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_customers_custno",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "CUSTNO",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_billings_billnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "BILLNUM",
                principalSchema: "msap",
                principalTable: "msap_billings",
                principalColumn: "RECID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_job_orders_job_order_id",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "msap_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_ports_portnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "PORTNUM",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_services_service_id",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "service_id",
                principalSchema: "msap",
                principalTable: "msap_services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_terminals_terminal",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "TERMINAL",
                principalSchema: "msap",
                principalTable: "msap_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_tug_masters_masterno",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "MASTERNO",
                principalSchema: "msap",
                principalTable: "msap_tug_masters",
                principalColumn: "tug_master_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_tugboats_tugnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "TUGNUM",
                principalSchema: "msap",
                principalTable: "msap_tugboats",
                principalColumn: "tugboat_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_dispatch_tickets_msap_vessels_vesselnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "VESSELNUM",
                principalSchema: "msap",
                principalTable: "msap_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_job_orders_customers_customer_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_job_orders_msap_ports_port_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_job_orders_msap_terminals_terminal_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "terminal_id",
                principalSchema: "msap",
                principalTable: "msap_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_job_orders_msap_tugboats_preferred_tugboat_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "preferred_tugboat_id",
                principalSchema: "msap",
                principalTable: "msap_tugboats",
                principalColumn: "tugboat_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_job_orders_msap_vessels_vessel_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "vessel_id",
                principalSchema: "msap",
                principalTable: "msap_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_principals_customers_customer_id",
                schema: "msap",
                table: "msap_principals",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tariff_rates_customers_customer_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "customer_id",
                principalSchema: "msap",
                principalTable: "customers",
                principalColumn: "customer_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tariff_rates_msap_ports_port_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tariff_rates_msap_services_service_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "service_id",
                principalSchema: "msap",
                principalTable: "msap_services",
                principalColumn: "service_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tariff_rates_msap_terminals_terminal_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "terminal_id",
                principalSchema: "msap",
                principalTable: "msap_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_terminals_msap_ports_port_id",
                schema: "msap",
                table: "msap_terminals",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tugboats_msap_ports_port_id",
                schema: "msap",
                table: "msap_tugboats",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_tugboats_msap_tugboat_owners_tugboat_owner_id",
                schema: "msap",
                table: "msap_tugboats",
                column: "tugboat_owner_id",
                principalSchema: "msap",
                principalTable: "msap_tugboat_owners",
                principalColumn: "tugboat_owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_msap_vessel_schedules_msap_job_orders_job_order_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "msap_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_vessel_schedules_msap_ports_port_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "port_id",
                principalSchema: "msap",
                principalTable: "msap_ports",
                principalColumn: "port_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_vessel_schedules_msap_terminals_terminal_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "terminal_id",
                principalSchema: "msap",
                principalTable: "msap_terminals",
                principalColumn: "terminal_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_msap_vessel_schedules_msap_vessels_vessel_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "vessel_id",
                principalSchema: "msap",
                principalTable: "msap_vessels",
                principalColumn: "vessel_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
