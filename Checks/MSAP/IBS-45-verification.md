# IBS-45 financial verification — 6 November 2026

Baseline: `40589a562`. Verification used a fresh dump/restore of the configured
local `ibs_dev` database into `ibs_workspace_check_9bb6f8c25e52`. Source access
was read-only; migrations, fixtures and payment operations ran only in the clone.
The clone is retained for inspection. No deployment or source-chart repair occurred.

## Confirmed defect and fix

An ended-period Sales Summary export failed when saving its finalized XLSX:
Npgsql inferred a timezone-aware parameter for the Philippine local `generatedAt`
value, but the value has `DateTimeKind.Unspecified` and the column is
`timestamp without time zone`. The mocked finalization test did not exercise this.

`ReportRepository.SaveFinalSalesReportAsync` now binds that parameter explicitly
as `NpgsqlDbType.Timestamp`. No schema, recognition policy or timezone conversion changed.
The PostgreSQL regression failed before the fix and passes afterward.

## Verification results

- Solution build: passed, zero warnings/errors.
- `--ar-monitoring-only`: passed existing AR/Dispatch XLSX discount, VAT,
  withholding, split/combined receipts, filtered COS and rounding cases, plus
  mocked recognition boundaries and final-file replay.
- `--sales-report-database`: passed actual PostgreSQL migration upgrade and
  rollback/reapply; model has no pending changes. Actual repository/controller
  exports reconcile mixed discounts, split Basic/BAF bills, a combined receipt
  across two COS and a service-date-filtered-out COS. For-billing status filtering
  and amounts also pass. This invokes controllers directly, not authenticated HTTP.
- Actual PostgreSQL finalization: January file replays byte-for-byte after a
  February provider confirmation; February contains amounts without duplicate
  service hours. Separate date ranges persist separately. Competing saves return
  one stored winner and preserve the local generation timestamp.
- `--billing-integrity-database`: passed persisted ticket/SBMA rounding, closed-period
  guards, approval integrity, single/split discard/reprepare, actual Sales Book
  snapshot posting and repeated approval protection.
- `--collection-only`: passed service checks for full settlement, withholding,
  deposit/return/redeposit/clearing, date/bank guards and audit behavior. These are
  mocked service checks, not an additional persisted collection lifecycle.
- `--other-tug-progress-database`: passed missing/draft/confirmed-zero provider
  progress, exclusions and dashboard behavior; fixture transaction rolled back.
- `--tugboat-payment-database`: initially stopped at the eight-account chart
  prerequisite. After inserting eight diagnostic AP liability fixtures ONLY into
  the disposable clone, the unchanged diagnostic passed posting/reversal,
  reservations/concurrency, selected-ticket/provider-bill operations, mixed VAT,
  cumulative rounding, immutable snapshots, storage rollback and workspace isolation.

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
second. January records the service without financial amounts; February records
sales 150,000, cost 100,000 and margin 50,000 without counting service hours again.
The original January finalized export stays unchanged.

## Remaining acceptance

- Finance must approve expected accounting results and the Other Tugs recognition,
  cutoff, late correction and finalization rules (IBS-8 / IBS-64). These checks
  verify implemented behavior, not policy approval or the management workbook.
- The fresh source clone did not contain the eight diagnostic AP account numbers,
  including `201020500`. Clone fixtures establish payment behavior, not operational
  chart readiness. Reconcile the earlier provisional-registration record with the
  intended target database and confirm account mapping (IBS-57).
- No finance reference workbook was present in the repository. No independent
  historical-period reconciliation against an approved workbook was performed.
- No new browser/accessibility, physical-print, live-cloud or deployment acceptance
  was performed. Historical populated migration paths were not exhaustively replayed.
- Sales Book snapshot posting passed its existing database diagnostic; the Sales
  Book XLSX layout and every report/filter combination were not independently audited.

## Repeat the new regression

Use a disposable local database named `ibs_workspace_check_*`, restored from the
configured source. The existing connection guard rejects the configured source
database and non-local hosts. The check rolls back/reapplies the final-report
migration, which deletes existing finalized report rows in that clone. Use a fresh
clone; do not use a database whose report snapshots need to be preserved.

```powershell
$env:IBS_REBUILD_TEST_DATABASE = 'ibs_workspace_check_<fresh-clone>'
dotnet run --project Checks/MSAP/Check.csproj -- --sales-report-database
```

Report transaction fixtures roll back and the concurrency fixture is removed on
success. The other existing database diagnostics retain their own clone fixtures.
