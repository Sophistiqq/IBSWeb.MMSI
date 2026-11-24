using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IBS.DataAccess.MSAP.Migrations
{
    /// <inheritdoc />
    public partial class InitialMsapCleanUp : Migration
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
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    actor_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
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
                    print_discount = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    default_cos_discount_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.customer_id);
                    table.CheckConstraint("ck_customers_default_cos_discount_percent", "default_cos_discount_percent BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "mmsi_adjustment_logs",
                schema: "msap",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    record_id = table.Column<int>(type: "integer", nullable: false),
                    reference_number = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    attachment_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    edited_by = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_adjustment_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_ports",
                schema: "msap",
                columns: table => new
                {
                    port_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    port_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    port_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_ports", x => x.port_id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_posted_periods",
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
                    table.PrimaryKey("pk_mmsi_posted_periods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_rate_uploads",
                schema: "msap",
                columns: table => new
                {
                    rate_upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uploaded_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rate_kind = table.Column<int>(type: "integer", nullable: false),
                    approved_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    changed_rows = table.Column<int>(type: "integer", nullable: false),
                    rows_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_rate_uploads", x => x.rate_upload_id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_services",
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
                    table.PrimaryKey("pk_mmsi_services", x => x.service_id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_tug_masters",
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
                    table.PrimaryKey("pk_mmsi_tug_masters", x => x.tug_master_id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_tugboat_owners",
                schema: "msap",
                columns: table => new
                {
                    tugboat_owner_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tugboat_owner_number = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    tugboat_owner_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    tin = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    vat_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    tax_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    withholding_tax_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_tugboat_owners", x => x.tugboat_owner_id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_user_accesses",
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
                    can_create_customer_order_slip = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit_customer_order_slip = table.Column<bool>(type: "boolean", nullable: false),
                    can_delete_customer_order_slip = table.Column<bool>(type: "boolean", nullable: false),
                    can_close_customer_order_slip = table.Column<bool>(type: "boolean", nullable: false),
                    can_approve_schedule = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_rates = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_rates = table.Column<bool>(type: "boolean", nullable: false),
                    can_approve_rate_upload = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_other_tug_charges = table.Column<bool>(type: "boolean", nullable: false),
                    can_confirm_other_tug_charges = table.Column<bool>(type: "boolean", nullable: false),
                    can_review_billing = table.Column<bool>(type: "boolean", nullable: false),
                    can_approve_final_billing = table.Column<bool>(type: "boolean", nullable: false),
                    can_access_treasury = table.Column<bool>(type: "boolean", nullable: false),
                    can_revert_collection_banking = table.Column<bool>(type: "boolean", nullable: false),
                    can_create_disbursement = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_msap_import = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_customer_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_service_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_port_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_terminal_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_vessel_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_tugboat_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_tugboat_owner_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_tug_master_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_principal_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_bank_account_master_file = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_inventory_report = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_maritime_report = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_user_accesses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_vessels",
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
                    table.PrimaryKey("pk_mmsi_vessels", x => x.vessel_id);
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
                name: "mmsi_collections",
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
                    returned_date = table.Column<DateOnly>(type: "date", nullable: true),
                    period_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("pk_mmsi_collections", x => x.RECID);
                    table.ForeignKey(
                        name: "fk_mmsi_collections_bank_accounts_bankacctco",
                        column: x => x.BANKACCTCO,
                        principalSchema: "msap",
                        principalTable: "bank_accounts",
                        principalColumn: "bank_account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_collections_customers_custno",
                        column: x => x.CUSTNO,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_principals",
                schema: "msap",
                columns: table => new
                {
                    principal_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    principal_number = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false),
                    principal_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    business_type = table.Column<string>(type: "text", nullable: true),
                    terms = table.Column<string>(type: "text", nullable: true),
                    tin = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_vatable = table.Column<bool>(type: "boolean", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    msap_recid = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_principals", x => x.principal_id);
                    table.ForeignKey(
                        name: "fk_mmsi_principals_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_terminals",
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
                    table.PrimaryKey("pk_mmsi_terminals", x => x.terminal_id);
                    table.ForeignKey(
                        name: "fk_mmsi_terminals_mmsi_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_other_tug_charges",
                schema: "msap",
                columns: table => new
                {
                    other_tug_charge_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tugboat_owner_id = table.Column<int>(type: "integer", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    includes_vat = table.Column<bool>(type: "boolean", nullable: false),
                    document_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    prepared_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    prepared_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    confirmed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    confirmed_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    period_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_other_tug_charges", x => x.other_tug_charge_id);
                    table.CheckConstraint("ck_other_tug_charge_confirmation", "revision >= 1 AND length(trim(provider_reference)) > 0 AND ((confirmed_at IS NULL AND confirmed_by_user_id IS NULL AND confirmed_by_name IS NULL) OR (confirmed_at IS NOT NULL AND confirmed_by_user_id IS NOT NULL AND confirmed_by_name IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_mmsi_other_tug_charges_msap_tugboat_owners_tugboat_owner_id",
                        column: x => x.tugboat_owner_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboat_owners",
                        principalColumn: "tugboat_owner_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_tugboats",
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
                    table.PrimaryKey("pk_mmsi_tugboats", x => x.tugboat_id);
                    table.ForeignKey(
                        name: "fk_mmsi_tugboats_mmsi_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_tugboats_msap_tugboat_owners_tugboat_owner_id",
                        column: x => x.tugboat_owner_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboat_owners",
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
                name: "mmsi_representatives",
                schema: "msap",
                columns: table => new
                {
                    representative_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    representative_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    principal_id = table.Column<int>(type: "integer", nullable: true),
                    rate_mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    rate_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    calculation_base = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    tin = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    vat_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    tax_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    withholding_tax_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_representatives", x => x.representative_id);
                    table.ForeignKey(
                        name: "fk_mmsi_representatives_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_representatives_mmsi_principals_principal_id",
                        column: x => x.principal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_principals",
                        principalColumn: "principal_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_baf_rates",
                schema: "msap",
                columns: table => new
                {
                    baf_rate_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    update_by = table.Column<string>(type: "text", nullable: true),
                    update_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_baf_rates", x => x.baf_rate_id);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_mmsi_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_mmsi_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_baf_rates_mmsi_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_tariff_rates",
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
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    update_by = table.Column<string>(type: "text", nullable: true),
                    update_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    dispatch_discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_tariff_rates", x => x.tariff_rate_id);
                    table.ForeignKey(
                        name: "fk_mmsi_tariff_rates_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_tariff_rates_mmsi_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_tariff_rates_mmsi_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mmsi_tariff_rates_mmsi_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "mmsi_billings",
                schema: "msap",
                columns: table => new
                {
                    RECID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NUMBER = table.Column<string>(type: "varchar(10)", nullable: false),
                    DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    reviewed_revision = table.Column<int>(type: "integer", nullable: true),
                    reviewed_by_user_id = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejected_by_user_id = table.Column<string>(type: "text", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    adjustment_attachment_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    discount_on_tariff = table.Column<bool>(type: "boolean", nullable: false),
                    discount_on_baf = table.Column<bool>(type: "boolean", nullable: false),
                    discount_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    flat_discount_tariff = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    flat_discount_baf = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    include_subcon_in_discount = table.Column<bool>(type: "boolean", nullable: false),
                    agency_incentive_representative_id = table.Column<int>(type: "integer", nullable: true),
                    agency_incentive_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    agency_incentive_mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    agency_incentive_base = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    agency_incentive_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    agency_incentive_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    agent_commission_representative_id = table.Column<int>(type: "integer", nullable: true),
                    agent_commission_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    agent_commission_mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    agent_commission_base = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    agent_commission_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    agent_commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    approved_revision = table.Column<int>(type: "integer", nullable: true),
                    approved_by_user_id = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    basic_billing_id = table.Column<int>(type: "integer", nullable: true),
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
                    print_discount = table.Column<bool>(type: "boolean", nullable: false),
                    is_printed = table.Column<bool>(type: "boolean", nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    terms = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    billing_year = table.Column<int>(type: "integer", nullable: false),
                    company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_order_slip_id = table.Column<int>(type: "integer", nullable: true),
                    CRNUM = table.Column<int>(type: "integer", nullable: true),
                    collection_number = table.Column<string>(type: "text", nullable: true),
                    unposted_by = table.Column<string>(type: "text", nullable: true),
                    unposted_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unpost_remarks = table.Column<string>(type: "text", nullable: true),
                    period_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("pk_mmsi_billings", x => x.RECID);
                    table.CheckConstraint("ck_billings_approval_evidence", "revision >= 1 AND ((approved_revision IS NULL AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (approved_revision IS NOT NULL AND approved_revision BETWEEN 1 AND revision AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");
                    table.CheckConstraint("ck_billings_review_evidence", "((reviewed_revision IS NULL AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL) OR (reviewed_revision IS NOT NULL AND reviewed_revision BETWEEN 1 AND revision AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_mmsi_billings_application_user_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_application_user_rejected_by_user_id",
                        column: x => x.rejected_by_user_id,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_application_user_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_customers_custno_fk",
                        column: x => x.CUSTNO_FK,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_mmsi_billings_basic_billing_id",
                        column: x => x.basic_billing_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_mmsi_customer_order_slips_customer_order_slip",
                        column: x => x.customer_order_slip_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_customer_order_slips",
                        principalColumn: "customer_order_slip_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_collections_crnum",
                        column: x => x.CRNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_collections",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_ports_portnum",
                        column: x => x.PORTNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_principals_principal_id",
                        column: x => x.principal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_principals",
                        principalColumn: "principal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_representatives_agency_incentive_represe",
                        column: x => x.agency_incentive_representative_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_representatives",
                        principalColumn: "representative_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_representatives_agent_commission_represe",
                        column: x => x.agent_commission_representative_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_representatives",
                        principalColumn: "representative_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_terminals_terminal",
                        column: x => x.TERMINAL,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_billings_msap_vessels_vesselnum",
                        column: x => x.VESSELNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_vessel_schedules",
                schema: "msap",
                columns: table => new
                {
                    vessel_schedule_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    vessel_id = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false),
                    terminal_id = table.Column<int>(type: "integer", nullable: false),
                    planned_start = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    planned_end = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    required_tug_count = table.Column<int>(type: "integer", nullable: false),
                    assigned_tugboat_ids = table.Column<string>(type: "text", nullable: true),
                    is_ioc = table.Column<bool>(type: "boolean", nullable: true),
                    voyage_number = table.Column<string>(type: "varchar(50)", nullable: true),
                    vessel_type = table.Column<string>(type: "varchar(20)", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    customer_order_slip_id = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    edited_by = table.Column<string>(type: "varchar(50)", nullable: true),
                    edited_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cos_discount_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    skip_cos_discount = table.Column<bool>(type: "boolean", nullable: false),
                    sbma_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    approved_revision = table.Column<int>(type: "integer", nullable: true),
                    approved_by_user_id = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_vessel_schedules", x => x.vessel_schedule_id);
                    table.CheckConstraint("ck_vessel_schedules_approval_evidence", "revision >= 1 AND ((approved_revision IS NULL AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (approved_revision IS NOT NULL AND approved_revision BETWEEN 1 AND revision AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");
                    table.CheckConstraint("ck_vessel_schedules_cos_discount_percent", "cos_discount_percent BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedules_application_user_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedules_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedules_mmsi_customer_order_slips_customer_or",
                        column: x => x.customer_order_slip_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_customer_order_slips",
                        principalColumn: "customer_order_slip_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedules_mmsi_ports_port_id",
                        column: x => x.port_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedules_mmsi_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedules_mmsi_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_collection_bills",
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
                    table.PrimaryKey("pk_mmsi_collection_bills", x => x.collection_bill_id);
                    table.ForeignKey(
                        name: "fk_mmsi_collection_bills_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_collection_bills_mmsi_billings_billing_id",
                        column: x => x.billing_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_collection_bills_mmsi_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_collections",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_sales_books",
                schema: "msap",
                columns: table => new
                {
                    sales_book_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    serial_no = table.Column<string>(type: "text", nullable: false),
                    sold_to = table.Column<string>(type: "text", nullable: false),
                    tin_no = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vatable_sales = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_exempt_sales = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    zero_rated = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_sales = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: true),
                    company = table.Column<string>(type: "text", nullable: false),
                    approved_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_undocumented = table.Column<bool>(type: "boolean", nullable: false),
                    billing_revision = table.Column<int>(type: "integer", nullable: false),
                    billing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    approved_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    voided_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    void_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_sales_books", x => x.sales_book_id);
                    table.ForeignKey(
                        name: "fk_mmsi_sales_books_msap_billings_document_id",
                        column: x => x.document_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_vessel_schedule_service_entries",
                schema: "msap",
                columns: table => new
                {
                    vessel_schedule_service_entry_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vessel_schedule_id = table.Column<int>(type: "integer", nullable: false),
                    tugboat_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    tariff_basis = table.Column<short>(type: "smallint", nullable: true),
                    baf_method = table.Column<short>(type: "smallint", nullable: true),
                    manual_tariff_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    is_manual_tariff_override = table.Column<bool>(type: "boolean", nullable: true),
                    manual_baf_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_vessel_schedule_service_entries", x => x.vessel_schedule_service_entry_id);
                    table.CheckConstraint("ck_schedule_service_entries_baf_method", "baf_method IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_schedule_service_entries_cancellation", "(cancelled_at IS NULL AND cancelled_by_user_id IS NULL AND cancellation_reason IS NULL) OR (cancelled_at IS NOT NULL AND cancelled_by_user_id IS NOT NULL AND length(trim(cancellation_reason)) > 0 AND cancellation_reason IS NOT NULL)");
                    table.CheckConstraint("ck_schedule_service_entries_manual_baf_rate", "manual_baf_rate >= 0");
                    table.CheckConstraint("ck_schedule_service_entries_manual_tariff_rate", "manual_tariff_rate >= 0");
                    table.CheckConstraint("ck_schedule_service_entries_tariff_basis", "tariff_basis IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedule_service_entries_mmsi_services_service_",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedule_service_entries_mmsi_tugboats_tugboat_",
                        column: x => x.tugboat_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_vessel_schedule_service_entries_mmsi_vessel_schedules_",
                        column: x => x.vessel_schedule_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessel_schedules",
                        principalColumn: "vessel_schedule_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_dispatch_tickets",
                schema: "msap",
                columns: table => new
                {
                    RECID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    accounting_source_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
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
                    tons = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    raw_duration_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    raw_duration_minutes = table.Column<int>(type: "integer", nullable: true),
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
                    sbma_share_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    agency_incentive_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    agent_commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 4, nullable: false),
                    ap_other_tugs_includes_vat = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_manual_tariff_rate = table.Column<bool>(type: "boolean", nullable: false),
                    is_manual_baf_rate = table.Column<bool>(type: "boolean", nullable: false),
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
                    customer_order_slip_id = table.Column<int>(type: "integer", nullable: true),
                    BILLNUM = table.Column<int>(type: "integer", nullable: true),
                    vessel_schedule_service_entry_id = table.Column<int>(type: "integer", nullable: true),
                    CUSTNO = table.Column<int>(type: "integer", nullable: false),
                    TUGNUM = table.Column<int>(type: "integer", nullable: false),
                    MASTERNO = table.Column<int>(type: "integer", nullable: true),
                    VESSELNUM = table.Column<int>(type: "integer", nullable: false),
                    PORTNUM = table.Column<int>(type: "integer", nullable: false),
                    TERMINAL = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    period_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_dispatch_tickets", x => x.RECID);
                    table.CheckConstraint("ck_dispatch_tickets_actual_times", "(date_left IS NULL AND time_left IS NULL AND date_arrived IS NULL AND time_arrived IS NULL) OR (date_left IS NOT NULL AND time_left IS NOT NULL AND date_arrived IS NOT NULL AND time_arrived IS NOT NULL AND date_arrived + time_arrived > date_left + time_left)");
                    table.CheckConstraint("ck_dispatch_tickets_raw_duration", "(raw_duration_hours IS NULL OR raw_duration_hours >= 0) AND (raw_duration_minutes IS NULL OR raw_duration_minutes >= 0)");
                    table.CheckConstraint("ck_dispatch_tickets_sbma_share", "sbma_share_amount >= 0");
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_customers_custno",
                        column: x => x.CUSTNO,
                        principalSchema: "msap",
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_mmsi_customer_order_slips_customer_or",
                        column: x => x.customer_order_slip_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_customer_order_slips",
                        principalColumn: "customer_order_slip_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_billings_billnum",
                        column: x => x.BILLNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_billings",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_ports_portnum",
                        column: x => x.PORTNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_ports",
                        principalColumn: "port_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_terminals_terminal",
                        column: x => x.TERMINAL,
                        principalSchema: "msap",
                        principalTable: "mmsi_terminals",
                        principalColumn: "terminal_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_tug_masters_masterno",
                        column: x => x.MASTERNO,
                        principalSchema: "msap",
                        principalTable: "mmsi_tug_masters",
                        principalColumn: "tug_master_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_tugboats_tugnum",
                        column: x => x.TUGNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_tugboats",
                        principalColumn: "tugboat_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_vessel_schedule_service_entries_",
                        column: x => x.vessel_schedule_service_entry_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessel_schedule_service_entries",
                        principalColumn: "vessel_schedule_service_entry_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_dispatch_tickets_msap_vessels_vesselnum",
                        column: x => x.VESSELNUM,
                        principalSchema: "msap",
                        principalTable: "mmsi_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mmsi_other_tug_charge_lines",
                schema: "msap",
                columns: table => new
                {
                    other_tug_charge_line_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    other_tug_charge_id = table.Column<int>(type: "integer", nullable: false),
                    dispatch_ticket_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mmsi_other_tug_charge_lines", x => x.other_tug_charge_line_id);
                    table.CheckConstraint("ck_other_tug_charge_line_amount", "amount >= 0 AND amount <= 99999999999999.99");
                    table.ForeignKey(
                        name: "fk_mmsi_other_tug_charge_lines_mmsi_dispatch_tickets_dispatch_",
                        column: x => x.dispatch_ticket_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_dispatch_tickets",
                        principalColumn: "RECID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mmsi_other_tug_charge_lines_mmsi_other_tug_charges_other_tu",
                        column: x => x.other_tug_charge_id,
                        principalSchema: "msap",
                        principalTable: "mmsi_other_tug_charges",
                        principalColumn: "other_tug_charge_id",
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
                name: "ix_customers_customer_code",
                schema: "msap",
                table: "customers",
                column: "customer_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_customer_name",
                schema: "msap",
                table: "customers",
                column: "customer_name");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_adjustment_logs_document_type_record_id",
                schema: "msap",
                table: "mmsi_adjustment_logs",
                columns: new[] { "document_type", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_adjustment_logs_edited_date",
                schema: "msap",
                table: "mmsi_adjustment_logs",
                column: "edited_date");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_customer_id_terminal_id_service_id_as_of_date",
                schema: "msap",
                table: "mmsi_baf_rates",
                columns: new[] { "customer_id", "terminal_id", "service_id", "as_of_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_port_id",
                schema: "msap",
                table: "mmsi_baf_rates",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_service_id",
                schema: "msap",
                table: "mmsi_baf_rates",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_baf_rates_terminal_id",
                schema: "msap",
                table: "mmsi_baf_rates",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_agency_incentive_representative_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "agency_incentive_representative_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_agent_commission_representative_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "agent_commission_representative_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_approved_by_user_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_basic_billing_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "basic_billing_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_billing_year_number_company",
                schema: "msap",
                table: "mmsi_billings",
                columns: new[] { "billing_year", "NUMBER", "company" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_crnum",
                schema: "msap",
                table: "mmsi_billings",
                column: "CRNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_custno_fk",
                schema: "msap",
                table: "mmsi_billings",
                column: "CUSTNO_FK");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_customer_order_slip_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "customer_order_slip_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_date",
                schema: "msap",
                table: "mmsi_billings",
                column: "DATE");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_portnum",
                schema: "msap",
                table: "mmsi_billings",
                column: "PORTNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_principal_id",
                schema: "msap",
                table: "mmsi_billings",
                column: "principal_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_status",
                schema: "msap",
                table: "mmsi_billings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_terminal",
                schema: "msap",
                table: "mmsi_billings",
                column: "TERMINAL");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_billings_vesselnum",
                schema: "msap",
                table: "mmsi_billings",
                column: "VESSELNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collection_bills_billing_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "billing_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collection_bills_collection_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collection_bills_customer_id",
                schema: "msap",
                table: "mmsi_collection_bills",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collections_bankacctco",
                schema: "msap",
                table: "mmsi_collections",
                column: "BANKACCTCO");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collections_crdate",
                schema: "msap",
                table: "mmsi_collections",
                column: "CRDATE");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collections_crnum_company",
                schema: "msap",
                table: "mmsi_collections",
                columns: new[] { "CRNUM", "company" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_collections_custno",
                schema: "msap",
                table: "mmsi_collections",
                column: "CUSTNO");

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

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_accounting_source_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "accounting_source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_billnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "BILLNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_custno",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "CUSTNO");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_customer_order_slip_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "customer_order_slip_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_masterno",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "MASTERNO");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_portnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "PORTNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_service_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_status",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_terminal",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "TERMINAL");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_tugnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "TUGNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_vessel_schedule_service_entry_id",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "vessel_schedule_service_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_dispatch_tickets_vesselnum",
                schema: "msap",
                table: "mmsi_dispatch_tickets",
                column: "VESSELNUM");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_other_tug_charge_lines_dispatch_ticket_id",
                schema: "msap",
                table: "mmsi_other_tug_charge_lines",
                column: "dispatch_ticket_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_other_tug_charge_lines_other_tug_charge_id",
                schema: "msap",
                table: "mmsi_other_tug_charge_lines",
                column: "other_tug_charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_other_tug_charges_tugboat_owner_id_provider_reference",
                schema: "msap",
                table: "mmsi_other_tug_charges",
                columns: new[] { "tugboat_owner_id", "provider_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_posted_periods_year_month",
                schema: "msap",
                table: "mmsi_posted_periods",
                columns: new[] { "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_principals_customer_id",
                schema: "msap",
                table: "mmsi_principals",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_rate_uploads_company_uploaded_at",
                schema: "msap",
                table: "mmsi_rate_uploads",
                columns: new[] { "company", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_representatives_customer_id",
                schema: "msap",
                table: "mmsi_representatives",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_representatives_principal_id",
                schema: "msap",
                table: "mmsi_representatives",
                column: "principal_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_sales_books_company_transaction_date",
                schema: "msap",
                table: "mmsi_sales_books",
                columns: new[] { "company", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_sales_books_document_id_billing_revision",
                schema: "msap",
                table: "mmsi_sales_books",
                columns: new[] { "document_id", "billing_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_tariff_rates_customer_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_tariff_rates_port_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_tariff_rates_service_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_tariff_rates_terminal_id",
                schema: "msap",
                table: "mmsi_tariff_rates",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_terminals_port_id",
                schema: "msap",
                table: "mmsi_terminals",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_tugboats_port_id",
                schema: "msap",
                table: "mmsi_tugboats",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_tugboats_tugboat_owner_id",
                schema: "msap",
                table: "mmsi_tugboats",
                column: "tugboat_owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedule_service_entries_service_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedule_service_entries_tugboat_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                column: "tugboat_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedule_service_entries_vessel_schedule_id",
                schema: "msap",
                table: "mmsi_vessel_schedule_service_entries",
                column: "vessel_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_approved_by_user_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_customer_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_customer_order_slip_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "customer_order_slip_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_planned_start",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "planned_start");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_port_id_terminal_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                columns: new[] { "port_id", "terminal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_status",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_terminal_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "ix_mmsi_vessel_schedules_vessel_id",
                schema: "msap",
                table: "mmsi_vessel_schedules",
                column: "vessel_id");

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
                name: "mmsi_adjustment_logs",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_baf_rates",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_collection_bills",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_other_tug_charge_lines",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_posted_periods",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_rate_uploads",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_sales_books",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_tariff_rates",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_user_accesses",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "terms",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "user_notification",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_dispatch_tickets",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_other_tug_charges",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_billings",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_tug_masters",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_vessel_schedule_service_entries",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_collections",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_representatives",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_services",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_vessel_schedules",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_principals",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_customer_order_slips",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_terminals",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_tugboats",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_vessels",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_ports",
                schema: "msap");

            migrationBuilder.DropTable(
                name: "mmsi_tugboat_owners",
                schema: "msap");
        }
    }
}
