# Changelog
All notable changes to this project will be documented in this file.

The format of this file follows **Keep a Changelog**  
and this project adheres to **Semantic Versioning (SemVer)**.

---

## [v1.1.0] - 2026-10-06

### Added
- Added company-owned tugboat tagging to non-trade check voucher invoice accounting entries in the create and edit workflows.
- Added EF Core registration and a database migration for MSAP tugboat and tugboat owner master data.

---

## [v1.0.0] - 2025-11-28
### Added
- Initial implementation of **IBSWeb – Integrated Business System**.
- Added **N-Tier architecture** structure:
    - `IBS.DataAccess` for repositories and Unit of Work
    - `IBS.Models` for entity models
    - `IBS.DTOs` for data transfer objects
    - `IBS.Utility` for enums, constants, helpers
    - `IBS.Services` for business logic modules
    - `IBSWeb` for UI controllers and views
- Implemented **Chart of Accounts** module with hierarchical level support.
- Added **General Ledger**, **Journal Entry**, and posting logic.
- Implemented **role-based access control** (Admin, Accountant, User).
- Added **session-based authentication** support.
- Added reusable **JavaScript utilities** and global `site.js`.
- Implemented partials and modular views for accounting pages.
- Added database context configuration and initial EF Core integrations.
- Added basic **audit logging** for tracking user actions.
- Added initial documentation structure (README, repository organization).

### Changed
- Refactored repository methods to use **async/await** and cleaner LINQ.
- Improved data validation and error handling across the project.
- Updated folder naming and namespace conventions for consistency.

### Fixed
- Fixed issues in Chart of Accounts sorting and retrieval.
- Fixed session retrieval inconsistencies on user login.
- Fixed bugs in DataTables initialization and hidden column searching.
- Fixed authentication redirect issues in restricted pages.

---

## Upstream IBSWeb updates

## [v5.1.0] - 2026-10-20

### Added
- Stored CWT and CWVAT amounts on debit and credit memos, calculated during create/edit and reused for printing, posting, and re-journal processing.
- Added check amount display and search to trade and non-trade payment check voucher lists, with a check amount footer total on the non-trade payment list.

### Changed
- Standardized sales invoice, service invoice, debit memo, and credit memo printouts with bordered tables and explicit sales, discount, VAT, withholding tax, withholding VAT, and amount-due rows.
- Updated sales and service invoice printouts to display stored withholding totals and deduct discounts from the total amount due.

### Fixed
- Aligned service-invoice debit and credit memo re-journal entries with normal posting so the original invoice discount is not deducted again.

### Deployment
- Apply the `AddDebitMemoWithholdingAmounts` and `AddCreditMemoWithholdingAmounts` database migrations before using the updated memo workflows.

---

## [v5.0.0] - 2026-10-15

### Changed
- **Breaking:** Replaced `POST /jobs/daily-service` and `POST /jobs/start-of-the-month-service` with `POST /api/daily-service` and `POST /api/start-of-the-month-service`. External scheduler targets must use the new URLs.
- Moved daily and start-of-month endpoint definitions into separate API files, with independent registration in `Program.cs` and processing retained in their existing services.
- Removed the Quartz job interfaces, execution attributes, and package dependency from the externally triggered processing services.
- Moved journal voucher amortization and automatic reversals into Admin-only monthly closure. Closing a month prepares entries for the first day of the following month; the anonymous start-of-month endpoint now only creates reminders.
- Updated NuGet dependencies, including ASP.NET Core and Entity Framework Core, PostgreSQL integration, Google Drive, spreadsheet and PDF generation, and logging configuration packages.

---

## [v4.1.2] - 2026-10-15

### Fixed
- Prevented non-trade check voucher payment edits from selecting invoices with a different document type from the saved payment. The validation message directs users to cancel the payment and create a new one when a different document type is needed.

---

## [v4.1.1] - 2026-10-08

### Fixed
- Updated index and export tables across administration, master-file, and transaction pages to use full-width styling and horizontal scrolling, with automatic column-width calculation disabled to reduce layout shifts during searches.

---

## [v4.1.0] - 2026-10-08

### 2026-10-08
- Fixed automatic journal voucher reversals to process only posted vouchers marked for reversal and date the reversal entries to the first day of the month following the voucher transaction date.

### 2026-10-07
- Added document type and documenting company name to trade and non-trade check voucher lists, including invoice, payment, and payroll invoice pages.
- Improved table layouts across administration, master-file, and transaction pages by allowing cell text to wrap while keeping column headings on one line, and hiding duplicate totals footers in scrollable tables.
- Updated the trade check voucher list to display the retained supplier name.

### 2026-10-05
- Added MMSI to the company portal links.

---
