# MSAP integration

IBSWeb.MMSI is the host. MSAP's Job Order, Dispatch Ticket, tariff, billing, collection, vessel scheduling, master files, imports, reports, permissions and audit workflows live in dedicated module paths.

| Boundary | Location |
| --- | --- |
| Operational controllers and views | `IBSWeb/Areas/MSAP` |
| MSAP administration | `IBSWeb/Areas/MSAPAdmin` |
| MSAP document maintenance | `IBSWeb/Areas/MSAPSuperAdmin` |
| Models and viewmodels | `IBS.Models/MSAP` |
| DTOs, repositories, services and utilities | Each existing project's `MSAP` folder |
| Startup, scoped JSON and dependencies | `IBSWeb/MSAP` and `IBS.Utility/MSAP/MSAP.props` |
| Static assets | `IBSWeb/wwwroot/msap` |
| Database and migration history | PostgreSQL schema `msap` |
| Audit trail | `msap.audit_trails`, `IBS.Models.MSAP.AuditTrail`, `/MSAP/AuditTrail` |
| Development attachments | `IBSWeb/App_Data/MSAP/LocalStorage`, `/msap-storage` |
| Cloud attachments | `msap/` object prefix, separately configured MSAP storage options |

MSAP reuses the host's Identity users, login and cookie. MSAP supports exactly `Admin`, `User` and `SuperAdmin`, with explicit assignments stored as a single `MSAP.Role` claim in the existing `public."AspNetUserClaims"` table. User creation, account details, activation and password resets are managed only in IBSWeb/Filpride. MSAP has no account-management endpoints. MSAP role claims and procedure permissions are separate from shared IBSWeb role memberships. Every MSAP endpoint requires an active IBSWeb account, except the anonymous error page. Without an explicit MSAP role, a shared Filpride Admin defaults to MSAP Admin; other accounts default to User. An explicit MSAP role takes precedence. SuperAdmin always requires an explicit MSAP role claim. Invalid or duplicate MSAP role claims deny access. Admin and SuperAdmin have all procedure permissions; User permissions come from MSAP User Access. SuperAdmin can also use the MSAP Admin area. Role changes are checked from storage each request, including existing login sessions. MSAP permissions, company/reference data, posted periods and audits remain separate. The host's company selection is not required to open the MMSI maritime dashboard.

The base context, models, audit trail, historical migrations and existing routes were not changed. The base has four integration files with additive edits: `IBSWeb/Program.cs`, `IBSWeb/Views/Shared/_Navbar.cshtml`, `IBSWeb/IBSWeb.csproj` and `IBS.Utility/IBS.Utility.csproj`. New dependencies and manual publication rules are in module-owned `.props` files. The local development connection now targets `ibs_dev`.

## Initial MSAP role assignment

Active IBSWeb accounts can open MSAP without a separate role assignment: Filpride Admin defaults to MSAP Admin, and other accounts default to User. To manage MSAP users and permissions, explicitly assign the first MSAP SuperAdmin to an existing IBSWeb account. Run the following on the target database with the chosen username; it aborts if the account does not exist. It changes only that account's MSAP role claim:

```sql
DO $$
DECLARE
    target_user_id text;
BEGIN
    SELECT id INTO STRICT target_user_id
    FROM public."AspNetUsers"
    WHERE normalized_user_name = upper('REPLACE_WITH_IBSWEB_USERNAME');

    DELETE FROM public."AspNetUserClaims"
    WHERE user_id = target_user_id AND claim_type = 'MSAP.Role';

    INSERT INTO public."AspNetUserClaims" (user_id, claim_type, claim_value)
    VALUES (target_user_id, 'MSAP.Role', 'SuperAdmin');
END $$;
```

MSAP does not provide user creation or account editing. Manage shared accounts in IBSWeb/Filpride and assign explicit MSAP role claims through an administrative database operation when needed. Unassigned accounts use their default Admin or User role; existing procedure permissions are retained. No schema migration is needed.

## Database setup

Local MSAP setup was rebuilt on 2026-10-07 against `localhost:5432/ibs_dev` after merging `origin/master` at `64db5a9a`. The old module migration and provisional MSAP records were removed, and the regenerated migration `20261007015023_InitialMsapModule` was applied with one company and 21 payment terms seeded. Public schema and records, including Identity and base audits, were verified unchanged. MSAP starts with empty operational records; existing accounting data remains in `public`. The integrated host's `IBSWeb/appsettings.Development.json` uses this database. Normal startup does not require `MSAP__ApplyMigrations=true` after this initialization.

Migration `20261007020521_RenameMsapTablesToMmsi` then renamed the module's 17 `msap_` tables to `mmsi_`, including their keys, foreign keys and indexes, within the `msap` schema. Import/reset SQL uses the new table names. The legacy `msap_recid` columns retain origin's spelling. Matching prefixes do not share data with accounting's separate `public.mmsi_*` tables.

The local public migration history still ends at `20260926022634_AddMultipleServiceInvoiceCollections`; origin's three later base migrations remain pending and must be applied separately with `ApplicationDbContext`. The MSAP reset and table rename did not apply or roll back base migrations.

The host database and Identity tables must already be initialized using the base's normal deployment process. MSAP uses the same `ConnectionStrings:MMSIConnection`. Its fresh migration creates only module-owned tables; it does not replay the separate source repository's historical migrations or copy existing source data.

For an explicit schema deployment, from the repository root, run:

```powershell
dotnet ef database update --project IBS.DataAccess --startup-project IBSWeb --context IBS.DataAccess.MSAP.Data.MsapDbContext
```

For startup migration and reference-data initialization, enable `MSAP:ApplyMigrations` (environment variable `MSAP__ApplyMigrations=true`). It defaults to false. Initialization seeds MSAP's company, never users, passwords, roles or payment terms. MSAP reads Filpride payment terms for term selection and billing due dates; maintain those records in Filpride. Running it again preserves existing module records.

EF tools, including Rider, resolve the module context from the IBSWeb startup project. They use the host's configuration: appsettings, environment-specific appsettings, development user secrets and environment variables. Set `ConnectionStrings__MMSIConnection` to override the connection explicitly, or pass `-- --environment Development` to select development settings. The module has no separate design database. Create future MSAP migrations with:

```powershell
dotnet ef migrations add ChangeName --project IBS.DataAccess --startup-project IBSWeb --context IBS.DataAccess.MSAP.Data.MsapDbContext --output-dir MSAP/Migrations
```

Keep base migrations under their existing context and path. Do not use the MSAP context to change base tables.

### Table layout and imports

`ibs_dev` contains the existing host tables in `public` and 28 MSAP tables (including migration history) in `msap`:

| Purpose | Tables in `msap` |
| --- | --- |
| Workflow | `mmsi_job_orders`, `mmsi_dispatch_tickets`, `mmsi_billings`, `mmsi_collections`, `mmsi_collection_bills` |
| Maritime references | `mmsi_ports`, `mmsi_terminals`, `mmsi_vessels`, `mmsi_services`, `mmsi_principals`, `mmsi_tariff_rates`, `mmsi_tugboats`, `mmsi_tug_masters`, `mmsi_tugboat_owners` |
| Master files | `customers`, `suppliers`, `employees`, `bank_accounts`, `companies`, `terms` |
| Scheduling and access | `mmsi_vessel_schedules`, `mmsi_user_accesses`, `mmsi_posted_periods` |
| Audit and settings | `audit_trails`, `app_settings`, `notification`, `user_notification`, `__EFMigrationsHistory` |

Use fully qualified names in SQL, for example `msap.mmsi_billings` and `msap.customers`. EF queries already apply this schema. Import/reset statements and legacy ID sequence updates also explicitly use `msap`; PostgreSQL's default search path does not include it. A missing-table error from an unqualified import statement is a code issue, not a reason to replay migrations.

Billing imports retain the source behavior of clearing existing MSAP billings and dispatch tickets before loading their CSVs. Reset clears the module's imported maritime data. Both operations are transactional; reset records an MSAP audit entry.

## Storage and audit identifiers

Development storage can be configured through `MSAP:LocalStoragePath`. Production storage uses `MSAP:GoogleCloudStorageBucketName` and the existing cloud credential mechanism. The `msap/` object prefix separates attachments even when a bucket is shared. Browser quick-access state and theme keys are also separate.

An audit record's identity is its module, document type and record ID. `RecordId` may overlap between documents or with the base application. MSAP timelines query only MSAP records and audits. Job Order, Dispatch Ticket and Billing creation save the generated ID before recording the audit, within a transaction. BAF adjustments reference the dispatch ticket; collection edits retain their collection ID and reference.

## Upstream synchronization

Sync upstream normally and retain the four small integration edits. Keep future MSAP changes in the module paths above. Shared integration lines can still require a merge resolution; folder isolation cannot promise zero conflicts. Build and run the checks after each sync:

```powershell
dotnet build "Integrated Business System.sln"
./Checks/MSAP/Check-EfConnection.ps1
dotnet run --project Checks/MSAP/Check.csproj
```

The check verifies all imported controllers, routing, DI, view isolation, database ownership, audit ownership, scoped JSON and attachment path confinement without starting a server.

For the PostgreSQL integration check, set `MSAP_CHECK_SERVER_CONNECTION` to a local PostgreSQL server connection with permission to create a temporary database, then run:

```powershell
dotnet run --project Checks/MSAP/Check.csproj -- --database
```

When the sibling MSAP copy is present, the check can read its development connection instead. It creates a uniquely named temporary database and drops only that database afterward. It verifies unchanged base schema/audits, shared users, idempotent module migration/seeding, Job Order/dispatch/billing/posting/collection, VAT totals and audit timeline isolation.

Browser layout, guided tours, file previews, manual links and report downloads still require manual review. Existing source data and attachments have not been migrated.
