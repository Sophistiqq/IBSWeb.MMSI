# Current MSAP status

Checkpoint: 19 October 2026. Latest application milestone: 3257f348b. Read [BLOCKERS.md](BLOCKERS.md) for outstanding work; this file records delivered scope and evidence.

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
- Schedule rendering verification now passes through `dotnet <isolated-output>/Check.dll --schedule-render-only`. The original reapproval failure was a stale fixture: COS existence does not establish approval history. Checks now cover initial approval with/without an urgent COS, reapproval, matching confirmation/status, read-only states and COS pending-ticket links with the correct route context. No application approval behavior changed. The check project build succeeded with the existing EF1002 warning. The broad command still stops at its stale 32-controller inventory assertion; the full suite has not passed.
- The user reports using placeholders to proceed with master data and handing off exported Excel workbooks with notes. Returned corrections remain pending; this is no longer an immediate implementation/testing blocker and does not confirm the placeholder business values.
- The agent has not applied migrations or run live database imports. Verify the target environment rather than assuming whether the owner has applied them.
- Latest supplied cleanup migrations: 20261019044840_RemovePortSbmaFlag and 20261019045214_RemoveUnusedMsapCustomerFields. They drop retired column values; Down restores columns/defaults, not those old values.
- Source CSVs and supporting workbooks remain unchanged. No Chrome Recorder artifacts have been produced.

## Resume point

The user is reviewing removal of inherited customer numbering logic. All 92 source customer codes are numeric (maximum 0093); none uses RET/IND/RES/GOV. Existing codes/source IDs remain necessary for import identity and customer references. The proposed single numeric sequence is not implemented. User manuals are deferred, correction workbooks have been handed off, and full workflow testing is owner-led. The focused schedule rendering failure is resolved; the broad check's controller inventory remains open.
