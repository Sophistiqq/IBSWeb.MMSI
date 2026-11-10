# IBS-45 financial verification — 9 November 2026

Baseline: `40589a562`, updated through `a5f6bacff`. Verification used a fresh dump/restore of the configured
local `ibs_dev` database into `ibs_workspace_check_9bb6f8c25e52` and `ibs_workspace_check_09c3f109d80c`.

## Updates in `f1e016812` and `a5f6bacff`

1. **Removed first-download Sales Summary freeze (`a5f6bacff`):**
   - Removed `FinalSalesReport` (`msap.mmsi_final_sales_reports`) and the unapplied `20261027075022_AddFinalSalesReports` migration per user decision so `MaritimeReportController.SalesSummary` always generates live from current billings, collections, and confirmation timestamps without locking out subsequent collection/billing updates.
2. **Direct AP Other Tugs contra-revenue recognition (IBS-8, Filpride pattern):**
   - Registered provisional contra-revenue account `401020400` (*Services by Other Tugs*, Level 4 under `401020000 Sales`) alongside `201020500` (*AP - Trade - Other Tugs*) and `101060200` (*Vat - Input*) in `ibs_dev`.
   - `OtherTugChargeService.ConfirmAsync` posts direct General Ledger entries (`Dr 401020400` net contra-revenue, `Dr 101060200` Input VAT, `Cr 201020500` gross payable) with `Reference = charge.ProviderReference` dated to the customer billing month (or current open period as an adjustment if closed), mirroring the Filpride Delivery Receipt pattern without creating a synthetic Journal Voucher header.
   - `TugboatPaymentService.CreateAsync` disburses payments directly against the confirmed charge and liquidates `201020500` without requiring an intermediate `LiabilityJournalVoucherId`.
3. **Management workbook inspection (`MMSI PNL 202605 by Mgt.xlsx` and `PNL SEPT 2026`):**
   - Confirmed both May and September management P&Ls treat AP Other Tugs as contra-revenue (`Less: Services by Other Tugs`), pools external tug margin into `INCOME FROM OTHER TUGS` without attributing cost to owned tugs, keys rows by customer billing statement date, and displays Gross Sales and Services by Other Tugs on distinct lines to preserve top-line gross revenue visibility.

## Verification results

- Solution build: passed, zero warnings/errors.
- `--ar-monitoring-only`: passed existing AR/Dispatch XLSX discount, VAT,
  withholding, split/combined receipts, filtered COS and rounding cases, plus
  mocked recognition boundaries.
- `--sales-report-database`: passed actual PostgreSQL model check (no pending changes).
  Actual repository/controller exports reconcile mixed discounts, split Basic/BAF bills,
  a combined receipt across two COS and a service-date-filtered-out COS. For-billing
  status filtering and amounts also pass.
- `--other-tugs-only`: passed VAT-inclusive default, COS-scoped allocation choices,
  existing allocation preservation, provider allocation validation, recognition VAT split
  (VAT-inclusive, VAT-exclusive, VAT-exempt), pending/confirmed zero, document boundaries,
  company-scoped query translation and unique ticket allocation.
- `--billing-integrity-database`: passed persisted ticket/SBMA rounding, closed-period
  guards, approval integrity, single/split discard/reprepare, actual Sales Book
  snapshot posting and repeated approval protection.
- `--collection-only`: passed service checks for full settlement, withholding,
  deposit/return/redeposit/clearing, date/bank guards and audit behavior.
- `--other-tug-progress-database`: passed missing/draft/confirmed-zero provider
  progress, exclusions and dashboard behavior; fixture transaction rolled back.
- `--tugboat-payment-database`: passed posting/reversal, reservations/concurrency,
  selected-ticket/provider-bill operations, mixed VAT, cumulative rounding,
  immutable snapshots, storage rollback, workspace isolation, and end-to-end
  `OtherTugChargeService.ConfirmAsync` contra-revenue/Input VAT/AP JV recognition
  plus `LiabilityJournalVoucherId` linking on payment.

The diagnostic project emits its existing EF1002 warning in `SchedulingCheck.cs`.
No unrelated warning or diagnostic implementation was changed.

## Concrete reconciliation cases

| Case | Gross after discount | Sales excluding VAT | Cash | EWT |
| --- | ---: | ---: | ---: | ---: |
| COS A: 10% discount, separate Basic/BAF bills | 1,209.60 | 1,080.00 | 1,188.00 | 21.60 |
| COS B: no discount, same receipt | 1,344.00 | 1,200.00 | 1,320.00 | 24.00 |
| Total | 2,553.60 | 2,280.00 | 2,508.00 | 45.60 |

COS B has a February service date and January billing date. It remains in January
Sales Summary and in the receipt denominator when January Dispatch Summary
excludes it. COS A therefore retains cash 1,188.00 and EWT 21.60 in both exports.

The external-tug case uses foreign-vessel sales of 150,000 with no VAT included,
service in January, and provider cost of 100,000 confirmed on February's last
second. January records the service with `"Deferred to confirmation month"` and no
financial amounts; February records sales 150,000, cost 100,000 and margin 50,000
without counting service hours again.

## Remaining acceptance

- Finance/management meeting sign-off on the confirmation-month vs. billing-month
  report cutoff and in-cell labels (IBS-64), permanent account numbers `201020500`
  and `401020400` (IBS-57 / IBS-8), and whether any of the other seven non-trade AP
  accounts from the diagnostic baseline are needed.
- No new browser/accessibility, physical-print, live-cloud or deployment acceptance
  was performed.

## Repeat the database checks

Use a disposable local database named `ibs_workspace_check_*`, restored from the
configured source. The existing connection guard rejects the configured source
database and non-local hosts.

```powershell
$env:IBS_REBUILD_TEST_DATABASE = 'ibs_workspace_check_<fresh-clone>'
dotnet run --project Checks/MSAP/Check.csproj -- --sales-report-database
dotnet run --project Checks/MSAP/Check.csproj -- --tugboat-payment-database
```
