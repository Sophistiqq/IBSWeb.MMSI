# Current MSAP status

Audit checkpoint: 26 October 2026. The delivered milestone table below records the 19 October baseline (3257f348b); audit fixes are local changes on `chore/audits-and-reviews`. Read [BLOCKERS.md](BLOCKERS.md) for outstanding work and the Linear project for individual findings and decisions.

## Delivered milestones

| Milestone | Current behavior / evidence |
|---|---|
| Scheduling and COS | Calendar, grouped tug/service planning, revision-aware COS approval, urgent factual ticket entry before approval and cancellation of unperformed services are implemented. COS replaced Job Order in the current flow. |
| Actual services and rates | Shared ticket record/view/edit form, attachments, automatic tariff/BAF resolution, completeness gates and explicit recalculation. Independent Tariff/BAF spreadsheet editors, comparisons, selected approvals and exports. Tuesday–Monday freshness applies only to table-derived BAF. |
| Customer and principal billing | General split Basic/BAF preparation, distinct billing numbers/balances, final approval and approval-gated print actions. Review includes ticket images; printing supports the agreed options. |
| Collection and clearing | Multi-bill full settlement, receiving-bank selection, deposit, return/redeposit and clearing. |
| Provider costs | Other Tug Charges capture/allocation/review/confirmation, tied to actual tugboats/tickets. No IBS Supplier prerequisite. Milestone b4e187d4c. |
| Accounting evidence | Stable ticket AccountingSourceId and persisted per-billing-number/per-approved-revision Sales Book snapshots. Weekly BAF enforcement milestone 313305905. Actual IBS GL/payment integration remains open. |
| Portable UI | Compact grayscale system with restrained attention colors, dark primary actions, shared clickable Job Progress and browser-history Back behavior. Styling milestone cd11c80cd. |
| Access and dashboard | Effective module-entry denial, granular action grants, Admin/SuperAdmin handling, retired-route cleanup and permission-scoped dashboard sections/queries. Milestone 0fb631b8c. |
| Master imports and cleanup | Admin-only master migration, editable comparisons, explicit updates, named audit, Excel headers/clean exports and annotated correction workbooks that can return as XLSX. Legacy tariff.csv stages separate rate drafts. Removed port SBMA and unused inherited IBS customer fields. Milestone 3257f348b. |

## Decisions to preserve

- Schedule SbmaEnabled controls SBMA; the port flag is retired. The calculation uses actual service time.
- Supplied final BAF values are sufficient for current supported methods. Fuel inputs, consumption/inventory and formulas are a future scope.
- Saving factual tickets can succeed while charges need applicable approved rates/recalculation; unresolved required charges/tickets still block billing.
- An April service billed/final-approved in May keeps its service date and enters the May Sales Book. Management GL recognition is separate. MSAP month closing is deferred.
- Collections settle each selected billing in full; one receipt/check may cover multiple bills.
- MSAP captures provider costs; IBS will pay tugboat payables. Stable identity is not implemented GL posting, synchronization or duplicate-payment prevention.
- VAT type, WHT and withholding VAT require independent confirmation. Imports preserve codes/source IDs and never invent identity, tax policy or home-port assignments.
- Master imports exclude legacy financial/user/reset operations. Retired fields/routes are not alternative ways around current workflow gates.

## Verification and deployment evidence

- The latest C# cleanup build succeeded through Checks/MSAP/Check.csproj (which references the web application). The pre-existing EF1002 warning in SchedulingCheck.cs:72 remains.
- Database-free master-import checks passed for workbook round trips/comments/notes, code preservation, formula rejection, correction boundaries, removed fields, migration/model alignment and forward/rollback SQL generation. Source inspection covered eleven master/rate CSVs and 2,083 rows.
- Targeted workflow-access, rate-access and Job Progress checks passed in the preceding access/import work. They have not been represented as the complete automated suite.
- The user has reported successful testing and will start the full workflow test plan. Complete recorded case-by-case browser/database acceptance remains outstanding; see the test plan.
- Schedule rendering checks cover initial approval with/without an urgent COS, reapproval, matching confirmation/status, read-only states and COS pending-ticket links. The default runner now passes after IBS-46 corrected the stale controller inventory. The check project still reports the existing EF1002 warning in SchedulingCheck.cs:72.
- The user reports using placeholders to proceed with master data and handing off exported Excel workbooks with notes. Returned corrections remain pending; this is no longer an immediate implementation/testing blocker and does not confirm the placeholder business values.
- Audit migrations and mutating workflow checks use designated local disposable databases. No source database reset, master import or source schema migration was performed. Target-environment deployment remains unverified.
- Latest supplied cleanup migrations: 20261019044840_RemovePortSbmaFlag and 20261019045214_RemoveUnusedMsapCustomerFields. They drop retired column values; Down restores columns/defaults, not those old values.
- Source CSVs and supporting workbooks remain unchanged. No Chrome Recorder artifacts have been produced.

## Resume point

The user confirmed type-prefixed codes for new customers while preserving numeric/imported identifiers. IBS-47 is implemented locally with a unique-index migration and a passing disposable PostgreSQL concurrency check. Audit history is restricted to administrators, including COS timeline controls; the role lookup uses the existing current-role service rather than login-cookie claims. All three User-role demo accounts were denied on the three audit endpoints, and the administrator received nonempty history. Actual COS pages matched those visibility rules.

The corrected original billing and dependent provider-progress diagnostics pass on `ibs_workspace_check_59c8d6163aae`. Persisted receipt-rename and actual XLSX checks on the earlier financial clone confirm the linked reference/audit fix and the discounted gross, income, withholding and BAF receipt totals. Report regression checks also cover Basic-only/separate/combined receipts and multi-COS receipts with different discounts, including filtered exports.

Clone HTTP checks now reject GET and missing/invalid antiforgery POST requests on all eight master delete endpoints. Invalid Port, Terminal and Vessel create/edit submissions preserve record counts and existing values. Chrome rendered the stored HTML probe literally, with no image or event attribute injected; normal confirmed Port deletion removed that clone fixture.

The first HTTP launch used the configured source connection because of PowerShell dictionary adaptation; those requests covered login and audit reads, with no business edits. The app was stopped and the full audit role/visibility check was repeated successfully with an explicit verified clone connection. No temporary audit app remains running.

At the user's explicit request, normal chart administration registered account 358 in ibs_dev: 201020500, AP - Trade - Other Tugs, Level 4, Credit, under account 51 / Accounts Payable 201010000. The mapping is provisional and must be verified at the next meeting (IBS-57). The seven other synthetic AP diagnostic accounts were not copied into the source chart. No payment or source posting was performed.

Continue final review of the local fixes and the remaining full mutation/role coverage in Linear. User manuals and full owner workflow acceptance remain deferred. The user authorized dead-code cleanup on 27 October.

The [cleanup record](DEAD_CODE_CANDIDATES.md) documents removal of the retired raw-data controller/view, its service/DI registration, six retired tariff methods and four unused dispatch helpers. The authorized SuperAdmin route remains, with a workspace entry replacing broken editor links. Existing deletion/restoration diagnostic consumers are retained. Builds, default/access/urgent checks and fresh-clone workspace/ticket/billing checks pass; the code-only graph is refreshed. These changes are included in the cleanup checkpoint.

IBS-59 records a separate verification gap: the full scheduling database runner expects a zero manual tariff to save, but the unchanged schedule validator rejects it. The failure occurs before the cleanup-related fixture changes. A passing full scheduling database run is not claimed; no rate-policy change or source database mutation was made for cleanup.
