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

## [v5.6.0] - 2026-11-23

### Added
- Allow Filpride users to export the Profit and Loss, Balance Sheet, and Statement of Retained Earnings reports to Excel before month-end close. The exports flag unavailable net income or retained earnings figures and indicate when year-to-date net income includes only closed months.

### Changed
- Generate the Filpride Trial Balance Excel export for a selected month using posted ledger activity. When a saved beginning balance is unavailable, the export leaves it blank and calculates the ending balance from that month's activity.

---

## [v5.5.0] - 2026-11-23

### Added
- Allow users to leave the account selection blank in the Filpride Subsidiary Ledger report to export all accounts with qualifying sub-account transactions for the selected date range. The Excel export includes per-account subtotals and a grand total.

---

## [v5.4.0] - 2026-11-10

### Added
- Extend Filpride Excel exports for IBS-RCD with transaction status, linked document IDs, customer and supplier details, withholding information, payment tracking, approval details, and sub-account fields across purchase orders, receiving reports, invoices, receipts, memos, and vouchers.

### Fixed
- Default supplier withholding percentages by category: 0.5% for Trade, 10% for Commissionee, and 2% for Hauler. Automatically derive the withholding tax title from the selected percentage, validate its matching account on save, and clear withholding fields for exempt suppliers.
- Default customer CWT to 0.5% and CWVAT to 5% when withholding is enabled without a selected percentage, while preserving saved percentages when opening the form.

---

## [v5.3.1] - 2026-10-30

### Fixed
- Prevent Transaction Master Control edits to check vouchers, journal vouchers, sales invoices, service invoices, and collection receipts in locked accounting periods. Also block linked payment voucher updates when their periods are locked, and display a message to reopen the affected period.

---

## [v5.3.0] - 2026-10-30

### Added
- Display check amounts in the Filpride disbursement list.
- Add confirmation actions to remove DCP or DCR dates from disbursement vouchers and connected invoices. Removing DCP also clears DCR; removing DCR preserves DCP. Record date removals in the audit trail.

### Fixed
- Align disbursement date filters with the new Check Amount column.
- Validate disbursement date requests and require antiforgery tokens for DCP/DCR updates and removals.

---

## [v5.2.1] - 2026-10-27

### Changed
- Display original and final purchase-order prices separately in PDF and Excel reports, with four-decimal price formatting in Excel.

### Fixed
- Use the approved final purchase-order price consistently for receiving reports, customer order slip product costs, and delivery receipt margin calculations.
- Apply approved price updates to all posted receiving reports for the purchase order, including direct-delivery freight, instead of limiting updates by triggered volume.
- Reject nonpositive or unchanged purchase-order price updates and additional updates while approval is pending; allow cost-adjustment entries for receiving reports without a delivery receipt.
- Replaced Times New Roman with Lato in Filpride PDF reports to address font compatibility after the QuestPDF update.

---

## [v5.2.0] - 2026-10-23

### Added
- Added net-of-VAT, VAT, withholding tax, and customer/supplier information to sales invoice and receiving report Excel exports for IBS-RCD.
- Included referenced check voucher invoice headers and details in journal voucher Excel exports.

### Changed
- Removed worksheet and workbook password protection from Filpride master-file and transaction Excel exports.

### Fixed
- Corrected liquidation report freight cost per liter to use delivery receipt quantities for receipts with freight, including per-hauler and combined cost summaries.

---

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
