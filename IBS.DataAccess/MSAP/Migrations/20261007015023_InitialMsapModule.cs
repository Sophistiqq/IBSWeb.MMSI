using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class InitialMsapModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "msap");

            migrationBuilder.CreateTable(
                name: "app_settings",
                schema: "msap",
                columns: table => new
                {
                    setting_key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.setting_key);
                });

            migrationBuilder.CreateTable(
                name: "audit_trails",
                schema: "msap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    machine_name = table.Column<string>(type: "text", nullable: false),
                    activity = table.Column<string>(type: "text", nullable: false),
                    document_type = table.Column<string>(type: "text", nullable: false),
                    record_id = table.Column<int>(type: "integer", nullable: true),
                    reference_number = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_trails", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "msap",
                columns: table => new
                {
                    bank_account_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bank_account_code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    bank = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.bank_account_id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "msap",
                columns: table => new
                {
                    company_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_code = table.Column<string>(type: "varchar(3)", nullable: true),
                    company_name = table.Column<string>(type: "varchar(50)", nullable: false),
                    company_address = table.Column<string>(type: "varchar(200)", nullable: false),
                    company_tin = table.Column<string>(type: "varchar(20)", nullable: false),
                    business_style = table.Column<string>(type: "varchar(20)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "msap",
                columns: table => new
                {
                    employee_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    initial = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    suffix = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tel_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sss_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tin_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    philhealth_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pagibig_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    department = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    date_hired = table.Column<DateOnly>(type: "date", nullable: false),
                    date_resigned = table.Column<DateOnly>(type: "date", nullable: true),
                    position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_managerial = table.Column<bool>(type: "boolean", nullable: false),
                    supervisor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    paygrade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "msap_ports",
                schema: "msap",
                columns: table => new
                {
                    port_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    port_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    port_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    has_sbma = table.Column<bool>(type: "boolean", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_ports", x => x.port_id);
                });

            migrationBuilder.CreateTable(
                name: "msap_posted_periods",
                schema: "msap",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    closed_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    closed_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    opened_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    opened_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_posted_periods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msap_services",
                schema: "msap",
                columns: table => new
                {
                    service_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    service_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    service_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_services", x => x.service_id);
                });

            migrationBuilder.CreateTable(
                name: "msap_tug_masters",
                schema: "msap",
                columns: table => new
                {
                    tug_master_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tug_master_number = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false),
                    tug_master_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_tug_masters", x => x.tug_master_id);
                });

            migrationBuilder.CreateTable(
                name: "msap_tugboat_owners",
                schema: "msap",
                columns: table => new
                {
                    tugboat_owner_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tugboat_owner_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    tugboat_owner_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    fixed_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_tugboat_owners", x => x.tugboat_owner_id);
                });

            migrationBuilder.CreateTable(
                name: "msap_user_accesses",
                schema: "msap",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    user_name = table.Column<string>(type: "varchar(100)", nullable: true),
                    can_create_dispatch_ticket = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit_dispatch_ticket = table.Column<bool>(type: "boolean", nullable: false),
                    can_cancel_dispatch_ticket = table.Column<bool>(type: "boolean", nullable: false),
                    can_set_tariff = table.Column<bool>(type: "boolean", nullable: false),
                    can_approve_tariff = table.Column<bool>(type: "boolean", nullable: false),
                    can_create_billing = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit_billing = table.Column<bool>(type: "boolean", nullable: false),
                    can_delete_billing = table.Column<bool>(type: "boolean", nullable: false),
                    can_reverse_billing = table.Column<bool>(type: "boolean", nullable: false),
                    can_create_collection = table.Column<bool>(type: "boolean", nullable: false),
                    can_create_job_order = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit_job_order = table.Column<bool>(type: "boolean", nullable: false),
                    can_delete_job_order = table.Column<bool>(type: "boolean", nullable: false),
                    can_close_job_order = table.Column<bool>(type: "boolean", nullable: false),
                    can_access_treasury = table.Column<bool>(type: "boolean", nullable: false),
                    can_create_disbursement = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_msap_import = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_maritime_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_inventory_report = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_maritime_report = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_user_accesses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "msap_vessels",
                schema: "msap",
                columns: table => new
                {
                    vessel_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vessel_number = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false),
                    vessel_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    vessel_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_vessels", x => x.vessel_id);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "msap",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.notification_id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "msap",
                columns: table => new
                {
                    supplier_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supplier_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supplier_tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_terms = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    vat_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tax_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proof_of_registration_file_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    proof_of_registration_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proof_of_exemption_file_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    proof_of_exemption_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    branch = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    withholding_tax_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    reason_of_exemption = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    validity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    validity_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requires_price_adjustment = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.supplier_id);
                });

            migrationBuilder.CreateTable(
                name: "terms",
                schema: "msap",
                columns: table => new
                {
                    terms_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    number_of_days = table.Column<int>(type: "integer", nullable: false),
                    number_of_months = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terms", x => x.terms_code);
                });

            migrationBuilder.CreateTable(
                name: "msap_terminals",
                schema: "msap",
                columns: table => new
                {
                    terminal_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    terminal_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    terminal_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    is_ioc = table.Column<bool>(type: "boolean", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_terminals", x => x.terminal_id);
                    table.ForeignKey(
                        name: "fk_msap_terminals_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "msap_tugboats",
                schema: "msap",
                columns: table => new
                {
                    tugboat_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tugboat_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    tugboat_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    is_company_owned = table.Column<bool>(type: "boolean", nullable: false),
                    tugboat_owner_id = table.Column<int>(type: "integer", nullable: true),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_tugboats", x => x.tugboat_id);
                    table.ForeignKey(
                        name: "fk_msap_tugboats_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_tugboats_msap_tugboat_owners_tugboat_owner_id",
                        column: x => x.tugboat_owner_id,
                        principalSchema: "msap",
                        principalTable: "msap_tugboat_owners",
                        principalColumn: "tugboat_owner_id");
                });

            migrationBuilder.CreateTable(
                name: "user_notification",
                schema: "msap",
                columns: table => new
                {
                    user_notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    requires_response = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notification", x => x.user_notification_id);
                    table.ForeignKey(
                        name: "fk_user_notification_application_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_notification_notification_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "msap",
                        principalTable: "notification",
                        principalColumn: "notification_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "msap",
                columns: table => new
                {
                    customer_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    customer_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address1 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address2 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address3 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    customer_tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_style = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    customer_terms = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vat_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    with_holding_vat = table.Column<bool>(type: "boolean", nullable: false),
                    with_holding_tax = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    station_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit_limit_as_of_today = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    retention_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    has_multiple_terms = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    requires_price_adjustment = table.Column<bool>(type: "boolean", nullable: false),
                    commissionee_id = table.Column<int>(type: "integer", nullable: true),
                    commission_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.customer_id);
                    table.ForeignKey(
                        name: "fk_customers_suppliers_commissionee_id",
                        column: x => x.commissionee_id,
                        principalSchema: "msap",
                        principalTable: "suppliers",
                        principalColumn: "supplier_id");
                });

            migrationBuilder.CreateTable(
                name: "msap_collections",
                schema: "msap",
                columns: table => new
                {
                    RECID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CRNUM = table.Column<string>(type: "text", nullable: false),
                    reference_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CRDATE = table.Column<DateOnly>(type: "date", nullable: false),
                    remarks = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cash_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CHECKDATE = table.Column<DateOnly>(type: "date", nullable: true),
                    CHECKNO = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    check_bank = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    check_branch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    check_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BANKACCTCO = table.Column<int>(type: "integer", nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AMOUNT = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ewt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    wvat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CUSTNO = table.Column<int>(type: "integer", nullable: false),
                    is_undocumented = table.Column<bool>(type: "boolean", nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deposit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_printed = table.Column<bool>(type: "boolean", nullable: false),
                    cleared_date = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_msap_collections", x => x.RECID);
                    table.ForeignKey(
                        name: "fk_msap_collections_bank_accounts_bankacctco",
                        column: x => x.BANKACCTCO,
                        principalSchema: "msap",
                        principalTable: "bank_accounts",
                        principalColumn: "bank_account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_collections_customers_custno",
                        column: x => x.CUSTNO,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msap_job_orders",
                schema: "msap",
                columns: table => new
                {
                    job_order_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_order_number = table.Column<string>(type: "varchar(20)", nullable: false),
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
                    table.PrimaryKey("pk_msap_job_orders", x => x.job_order_id);
                    table.ForeignKey(
                        name: "fk_msap_job_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_job_orders_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_job_orders_msap_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "msap_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_job_orders_msap_tugboats_preferred_tugboat_id",
                        column: x => x.preferred_tugboat_id,
                        principalSchema: "msap",
                        principalTable: "msap_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_msap_job_orders_msap_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalSchema: "msap",
                        principalTable: "msap_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msap_principals",
                schema: "msap",
                columns: table => new
                {
                    principal_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    principal_number = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false),
                    principal_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    agent = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    address1 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    address2 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    address3 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    business_type = table.Column<string>(type: "text", nullable: true),
                    terms = table.Column<string>(type: "text", nullable: true),
                    tin = table.Column<string>(type: "text", nullable: true),
                    landline1 = table.Column<string>(type: "text", nullable: true),
                    landline2 = table.Column<string>(type: "text", nullable: true),
                    mobile1 = table.Column<string>(type: "text", nullable: true),
                    mobile2 = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_vatable = table.Column<bool>(type: "boolean", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_principals", x => x.principal_id);
                    table.ForeignKey(
                        name: "fk_msap_principals_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "msap_tariff_rates",
                schema: "msap",
                columns: table => new
                {
                    tariff_rate_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    dispatch = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    baf = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    update_by = table.Column<string>(type: "text", nullable: true),
                    update_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    dispatch_discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    baf_discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_tariff_rates", x => x.tariff_rate_id);
                    table.ForeignKey(
                        name: "fk_msap_tariff_rates_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_msap_tariff_rates_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_msap_tariff_rates_msap_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "msap_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_msap_tariff_rates_msap_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "msap_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "msap_vessel_schedules",
                schema: "msap",
                columns: table => new
                {
                    vessel_schedule_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vessel_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    planned_start = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    planned_end = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    required_tug_count = table.Column<int>(type: "integer", nullable: false),
                    assigned_tugboat_ids = table.Column<string>(type: "text", nullable: true),
                    voyage_number = table.Column<string>(type: "varchar(50)", nullable: true),
                    vessel_type = table.Column<string>(type: "varchar(20)", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    job_order_id = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_vessel_schedules", x => x.vessel_schedule_id);
                    table.ForeignKey(
                        name: "fk_msap_vessel_schedules_msap_job_orders_job_order_id",
                        column: x => x.job_order_id,
                        principalSchema: "msap",
                        principalTable: "msap_job_orders",
                        principalColumn: "job_order_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_msap_vessel_schedules_msap_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_vessel_schedules_msap_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "msap_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_vessel_schedules_msap_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalSchema: "msap",
                        principalTable: "msap_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msap_billings",
                schema: "msap",
                columns: table => new
                {
                    RECID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NUMBER = table.Column<string>(type: "varchar(10)", nullable: false),
                    DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_undocumented = table.Column<bool>(type: "boolean", nullable: false),
                    CUSTNO = table.Column<string>(type: "varchar(10)", nullable: false),
                    voyage_number = table.Column<string>(type: "text", nullable: true),
                    cos_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    AMOUNT = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    dispatch_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    baf_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_principal = table.Column<bool>(type: "boolean", nullable: false),
                    CUSTNO_FK = table.Column<int>(type: "integer", nullable: false),
                    principal_id = table.Column<int>(type: "integer", nullable: true),
                    VESSELNUM = table.Column<int>(type: "integer", nullable: false),
                    PORTNUM = table.Column<int>(type: "integer", nullable: false),
                    TERMINAL = table.Column<int>(type: "integer", nullable: false),
                    ap_other_tug = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VAT = table.Column<bool>(type: "boolean", nullable: false),
                    is_vat_inclusive = table.Column<bool>(type: "boolean", nullable: false),
                    print_wht = table.Column<bool>(type: "boolean", nullable: false),
                    is_printed = table.Column<bool>(type: "boolean", nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    terms = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    billing_year = table.Column<int>(type: "integer", nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    job_order_id = table.Column<int>(type: "integer", nullable: true),
                    CRNUM = table.Column<int>(type: "integer", nullable: true),
                    collection_number = table.Column<string>(type: "text", nullable: true),
                    unposted_by = table.Column<string>(type: "text", nullable: true),
                    unposted_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unpost_remarks = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_msap_billings", x => x.RECID);
                    table.ForeignKey(
                        name: "fk_msap_billings_customers_custno_fk",
                        column: x => x.CUSTNO_FK,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_billings_msap_collections_crnum",
                        column: x => x.CRNUM,
                        principalSchema: "msap",
                        principalTable: "msap_collections",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_billings_msap_job_orders_job_order_id",
                        column: x => x.job_order_id,
                        principalSchema: "msap",
                        principalTable: "msap_job_orders",
                        principalColumn: "job_order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_billings_msap_ports_portnum",
                        column: x => x.PORTNUM,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_billings_msap_principals_principal_id",
                        column: x => x.principal_id,
                        principalSchema: "msap",
                        principalTable: "msap_principals",
                        principalColumn: "principal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_billings_msap_terminals_terminal",
                        column: x => x.TERMINAL,
                        principalSchema: "msap",
                        principalTable: "msap_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_billings_msap_vessels_vesselnum",
                        column: x => x.VESSELNUM,
                        principalSchema: "msap",
                        principalTable: "msap_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msap_collection_bills",
                schema: "msap",
                columns: table => new
                {
                    collection_bill_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    collection_number = table.Column<string>(type: "varchar(10)", nullable: false),
                    billing_number = table.Column<string>(type: "varchar(10)", nullable: false),
                    customer_number = table.Column<string>(type: "varchar(10)", nullable: false),
                    collection_id = table.Column<int>(type: "integer", nullable: false),
                    billing_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_collection_bills", x => x.collection_bill_id);
                    table.ForeignKey(
                        name: "fk_msap_collection_bills_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_collection_bills_msap_billings_billing_id",
                        column: x => x.billing_id,
                        principalSchema: "msap",
                        principalTable: "msap_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_collection_bills_msap_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "msap",
                        principalTable: "msap_collections",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "msap_dispatch_tickets",
                schema: "msap",
                columns: table => new
                {
                    RECID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    NUMBER = table.Column<string>(type: "varchar(20)", nullable: false),
                    cos_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    date_left = table.Column<DateOnly>(type: "date", nullable: true),
                    date_arrived = table.Column<DateOnly>(type: "date", nullable: true),
                    time_left = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    time_arrived = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    remarks = table.Column<string>(type: "varchar(100)", nullable: true),
                    base_or_station = table.Column<string>(type: "varchar(100)", nullable: true),
                    voyage_number = table.Column<string>(type: "varchar(100)", nullable: true),
                    dispatch_charge_type = table.Column<string>(type: "text", nullable: true),
                    baf_charge_type = table.Column<string>(type: "text", nullable: true),
                    total_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "text", nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    dispatch_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    dispatch_billing_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    dispatch_discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    dispatch_net_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    baf_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    baf_billing_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    baf_discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    baf_net_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    total_billing = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    total_net_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    ap_other_tugs = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ap_other_tugs_includes_vat = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tariff_by = table.Column<string>(type: "text", nullable: true),
                    tariff_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    tariff_edited_by = table.Column<string>(type: "text", nullable: true),
                    tariff_edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    image_name = table.Column<string>(type: "text", nullable: true),
                    image_saved_url = table.Column<string>(type: "text", nullable: true),
                    image_signed_url = table.Column<string>(type: "text", nullable: true),
                    video_name = table.Column<string>(type: "text", nullable: true),
                    video_saved_url = table.Column<string>(type: "text", nullable: true),
                    video_signed_url = table.Column<string>(type: "text", nullable: true),
                    job_order_id = table.Column<int>(type: "integer", nullable: true),
                    BILLNUM = table.Column<int>(type: "integer", nullable: true),
                    CUSTNO = table.Column<int>(type: "integer", nullable: false),
                    TUGNUM = table.Column<int>(type: "integer", nullable: false),
                    MASTERNO = table.Column<int>(type: "integer", nullable: true),
                    VESSELNUM = table.Column<int>(type: "integer", nullable: false),
                    PORTNUM = table.Column<int>(type: "integer", nullable: false),
                    TERMINAL = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_msap_dispatch_tickets", x => x.RECID);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_customers_custno",
                        column: x => x.CUSTNO,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_billings_billnum",
                        column: x => x.BILLNUM,
                        principalSchema: "msap",
                        principalTable: "msap_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_job_orders_job_order_id",
                        column: x => x.job_order_id,
                        principalSchema: "msap",
                        principalTable: "msap_job_orders",
                        principalColumn: "job_order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_ports_portnum",
                        column: x => x.PORTNUM,
                        principalSchema: "msap",
                        principalTable: "msap_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "msap_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_terminals_terminal",
                        column: x => x.TERMINAL,
                        principalSchema: "msap",
                        principalTable: "msap_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_tug_masters_masterno",
                        column: x => x.MASTERNO,
                        principalSchema: "msap",
                        principalTable: "msap_tug_masters",
                        principalColumn: "tug_master_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_tugboats_tugnum",
                        column: x => x.TUGNUM,
                        principalSchema: "msap",
                        principalTable: "msap_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_msap_dispatch_tickets_msap_vessels_vesselnum",
                        column: x => x.VESSELNUM,
                        principalSchema: "msap",
                        principalTable: "msap_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_settings_setting_key",
                schema: "msap",
                table: "app_settings",
                column: "setting_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_trails_date",
                schema: "msap",
                table: "audit_trails",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_audit_trails_document_type",
                schema: "msap",
                table: "audit_trails",
                column: "document_type");

            migrationBuilder.CreateIndex(
                name: "ix_audit_trails_record_id",
                schema: "msap",
                table: "audit_trails",
                column: "record_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_trails_reference_number",
                schema: "msap",
                table: "audit_trails",
                column: "reference_number");

            migrationBuilder.CreateIndex(
                name: "ix_companies_company_code",
                schema: "msap",
                table: "companies",
                column: "company_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_company_name",
                schema: "msap",
                table: "companies",
                column: "company_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_commissionee_id",
                schema: "msap",
                table: "customers",
                column: "commissionee_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_customer_code",
                schema: "msap",
                table: "customers",
                column: "customer_code");

            migrationBuilder.CreateIndex(
                name: "ix_customers_customer_name",
                schema: "msap",
                table: "customers",
                column: "customer_name");

            migrationBuilder.CreateIndex(
                name: "ix_employees_employee_number",
                schema: "msap",
                table: "employees",
                column: "employee_number");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_billing_year_number_company",
                schema: "msap",
                table: "msap_billings",
                columns: new[] { "billing_year", "NUMBER", "company" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_crnum",
                schema: "msap",
                table: "msap_billings",
                column: "CRNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_custno_fk",
                schema: "msap",
                table: "msap_billings",
                column: "CUSTNO_FK");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_date",
                schema: "msap",
                table: "msap_billings",
                column: "DATE");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_job_order_id",
                schema: "msap",
                table: "msap_billings",
                column: "job_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_portnum",
                schema: "msap",
                table: "msap_billings",
                column: "PORTNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_principal_id",
                schema: "msap",
                table: "msap_billings",
                column: "principal_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_status",
                schema: "msap",
                table: "msap_billings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_terminal",
                schema: "msap",
                table: "msap_billings",
                column: "TERMINAL");

            migrationBuilder.CreateIndex(
                name: "ix_msap_billings_vesselnum",
                schema: "msap",
                table: "msap_billings",
                column: "VESSELNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_collection_bills_billing_id",
                schema: "msap",
                table: "msap_collection_bills",
                column: "billing_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_collection_bills_collection_id",
                schema: "msap",
                table: "msap_collection_bills",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_collection_bills_customer_id",
                schema: "msap",
                table: "msap_collection_bills",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_collections_bankacctco",
                schema: "msap",
                table: "msap_collections",
                column: "BANKACCTCO");

            migrationBuilder.CreateIndex(
                name: "ix_msap_collections_crdate",
                schema: "msap",
                table: "msap_collections",
                column: "CRDATE");

            migrationBuilder.CreateIndex(
                name: "ix_msap_collections_crnum_company",
                schema: "msap",
                table: "msap_collections",
                columns: new[] { "CRNUM", "company" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_msap_collections_custno",
                schema: "msap",
                table: "msap_collections",
                column: "CUSTNO");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_billnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "BILLNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_custno",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "CUSTNO");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_job_order_id",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "job_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_masterno",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "MASTERNO");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_portnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "PORTNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_service_id",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_status",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_terminal",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "TERMINAL");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_tugnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "TUGNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_dispatch_tickets_vesselnum",
                schema: "msap",
                table: "msap_dispatch_tickets",
                column: "VESSELNUM");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_customer_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_job_order_number",
                schema: "msap",
                table: "msap_job_orders",
                column: "job_order_number");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_port_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_preferred_tugboat_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "preferred_tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_status",
                schema: "msap",
                table: "msap_job_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_terminal_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_job_orders_vessel_id",
                schema: "msap",
                table: "msap_job_orders",
                column: "vessel_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_posted_periods_year_month",
                schema: "msap",
                table: "msap_posted_periods",
                columns: new[] { "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_msap_principals_customer_id",
                schema: "msap",
                table: "msap_principals",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_tariff_rates_customer_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_tariff_rates_port_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_tariff_rates_service_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_tariff_rates_terminal_id",
                schema: "msap",
                table: "msap_tariff_rates",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_terminals_port_id",
                schema: "msap",
                table: "msap_terminals",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_tugboats_port_id",
                schema: "msap",
                table: "msap_tugboats",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_tugboats_tugboat_owner_id",
                schema: "msap",
                table: "msap_tugboats",
                column: "tugboat_owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_vessel_schedules_job_order_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "job_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_vessel_schedules_planned_start",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "planned_start");

            migrationBuilder.CreateIndex(
                name: "ix_msap_vessel_schedules_port_id_terminal_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                columns: new[] { "port_id", "terminal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_msap_vessel_schedules_status",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_msap_vessel_schedules_terminal_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_msap_vessel_schedules_vessel_id",
                schema: "msap",
                table: "msap_vessel_schedules",
                column: "vessel_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_supplier_code",
                schema: "msap",
                table: "suppliers",
                column: "supplier_code");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_supplier_name",
                schema: "msap",
                table: "suppliers",
                column: "supplier_name");

            migrationBuilder.CreateIndex(
                name: "ix_user_notification_notification_id",
                schema: "msap",
                table: "user_notification",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_notification_user_id",
                schema: "msap",
                table: "user_notification",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "audit_trails",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_collection_bills",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_dispatch_tickets",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_posted_periods",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_tariff_rates",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_user_accesses",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_vessel_schedules",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "terms",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "user_notification",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_billings",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_tug_masters",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_services",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_collections",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_job_orders",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_principals",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_terminals",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_tugboats",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_vessels",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_ports",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "msap_tugboat_owners",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "msap");
        }
    }
}
