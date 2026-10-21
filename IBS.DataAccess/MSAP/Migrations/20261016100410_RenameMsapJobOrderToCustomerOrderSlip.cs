using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class RenameMsapJobOrderToCustomerOrderSlip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropTable(
                name: "mmsi_job_orders",
                schema: "msap");

            migrationBuilder.RenameColumn(
                name: "job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "customer_order_slip_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_customer_order_slip_id");

            migrationBuilder.RenameColumn(
                name: "can_edit_job_order",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_edit_customer_order_slip");

            migrationBuilder.RenameColumn(
                name: "can_delete_job_order",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_delete_customer_order_slip");

            migrationBuilder.RenameColumn(
                name: "can_create_job_order",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_create_customer_order_slip");

            migrationBuilder.RenameColumn(
                name: "can_close_job_order",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_close_customer_order_slip");

            migrationBuilder.RenameColumn(
                name: "job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "customer_order_slip_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_customer_order_slip_id");

            migrationBuilder.RenameColumn(
                name: "job_order_id",
                schema: "msap",
                table: "mmsi_billings",
                newName: "customer_order_slip_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_job_order_id",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_customer_order_slip_id");

            migrationBuilder.CreateTable(
                name: "mmsi_customer_order_slips",
                schema: "msap",
                columns: table => new
                {
                    customer_order_slip_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_order_slip_number = table.Column<string>(type: "varchar(20)", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    cos_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    voyage_number = table.Column<string>(type: "varchar(100)", nullable: true),
                    remarks = table.Column<string>(type: "text", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    vessel_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    planned_start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    planned_end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    required_tug_count = table.Column<int>(type: "integer", nullable: false),
                    preferred_tugboat_id = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_remarks = table.Column<string>(type: "varchar(255)", nullable: true),
                    canceled_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    canceled_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    voided_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    voided_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    posted_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    posted_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_customer_order_slips", x => x.customer_order_slip_id);
                    table.ForeignKey(
                        name: "fk_mmsi_customer_order_slips_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_customer_order_slips_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_customer_order_slips_msap_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_customer_order_slips_msap_tugboats_preferred_tugboat_id",
                        column: x => x.preferred_tugboat_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mmsi_customer_order_slips_msap_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_customer_id",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_customer_order_slip_number",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "customer_order_slip_number");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_port_id",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_preferred_tugboat_id",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "preferred_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_status",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_terminal_id",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_customer_order_slips_vessel_id",
                schema: "msap",
                table: "mmsi_customer_order_slips",
                column: "vessel_id");

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_billings_mmsi_customer_order_slips_customer_order_slip",
                schema: "msap",
                table: "mmsi_billings",
                column: "customer_order_slip_id",
                principalSchema: "msap",
                principalTable: "mmsi_customer_order_slips",
                principalColumn: "customer_order_slip_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_dispatch_tickets_mmsi_customer_order_slips_customer_or",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "customer_order_slip_id",
                principalSchema: "msap",
                principalTable: "mmsi_customer_order_slips",
                principalColumn: "customer_order_slip_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_customer_order_slips_customer_or",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "customer_order_slip_id",
                principalSchema: "msap",
                principalTable: "mmsi_customer_order_slips",
                principalColumn: "customer_order_slip_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_billings_mmsi_customer_order_slips_customer_order_slip",
                schema: "msap",
                table: "mmsi_billings");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_dispatch_tickets_mmsi_customer_order_slips_customer_or",
                schema: "msap",
                table: "mmsi_dispatch_tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_customer_order_slips_customer_or",
                schema: "msap",
                table: "mmsi_vessel_schedules");

            migrationBuilder.DropTable(
                name: "mmsi_customer_order_slips",
                schema: "msap");

            migrationBuilder.RenameColumn(
                name: "customer_order_slip_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_vessel_schedules_customer_order_slip_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                newName: "ix_mmsi_vessel_schedules_job_order_id");

            migrationBuilder.RenameColumn(
                name: "can_edit_customer_order_slip",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_edit_job_order");

            migrationBuilder.RenameColumn(
                name: "can_delete_customer_order_slip",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_delete_job_order");

            migrationBuilder.RenameColumn(
                name: "can_create_customer_order_slip",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_create_job_order");

            migrationBuilder.RenameColumn(
                name: "can_close_customer_order_slip",
                schema: "msap",
                table: "mmsi_user_accesses",
                newName: "can_close_job_order");

            migrationBuilder.RenameColumn(
                name: "customer_order_slip_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_dispatch_tickets_customer_order_slip_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                newName: "ix_mmsi_dispatch_tickets_job_order_id");

            migrationBuilder.RenameColumn(
                name: "customer_order_slip_id",
                schema: "msap",
                table: "mmsi_billings",
                newName: "job_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_mmsi_billings_customer_order_slip_id",
                schema: "msap",
                table: "mmsi_billings",
                newName: "ix_mmsi_billings_job_order_id");

            migrationBuilder.CreateTable(
                name: "mmsi_job_orders",
                schema: "msap",
                columns: table => new
                {
                    job_order_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    preferred_tugboat_id = table.Column<int>(type: "integer", nullable: true),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    vessel_id = table.Column<int>(type: "integer", nullable: false),
                    cos_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    canceled_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    canceled_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_remarks = table.Column<string>(type: "varchar(255)", nullable: true),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    edited_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    job_order_number = table.Column<string>(type: "varchar(20)", nullable: false),
                    planned_end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    planned_start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    posted_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    posted_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    remarks = table.Column<string>(type: "text", nullable: true),
                    required_tug_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    voided_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    voided_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    voyage_number = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_job_orders", x => x.job_order_id);
                    table.ForeignKey(
                        name: "fk_mmsi_job_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_job_orders_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_job_orders_msap_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_job_orders_msap_tugboats_preferred_tugboat_id",
                        column: x => x.preferred_tugboat_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mmsi_job_orders_msap_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_customer_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_job_order_number",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "job_order_number");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_port_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_preferred_tugboat_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "preferred_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_status",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_terminal_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_job_orders_vessel_id",
                schema: "msap",
                table: "mmsi_job_orders",
                column: "vessel_id");

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
                name: "fk_mmsi_dispatch_tickets_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "mmsi_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_mmsi_vessel_schedules_mmsi_job_orders_job_order_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "job_order_id",
                principalSchema: "msap",
                principalTable: "mmsi_job_orders",
                principalColumn: "job_order_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
