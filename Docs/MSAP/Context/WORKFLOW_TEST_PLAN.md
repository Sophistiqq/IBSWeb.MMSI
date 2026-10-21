# MSAP full workflow acceptance test plan

Baseline: `ba06bde81`, 19 October 2026. Owner: tester to assign. Execution status: **Not run**.

This is an executable manual acceptance plan for the current MSAP application, from master data and approved rates through scheduling, COS, actual tickets, billing, collection and clearing. It also covers provider charges, reports, dashboards, access, audit and failure recovery. It is a test specification, not evidence that the cases passed. Later implementation changes must update this baseline and the affected expected results.

## 1. How to execute and record results

1. Use a dedicated test database, test cloud-storage location and named test accounts. Have the application owner apply the required migrations. Do not run these cases on production financial records.
2. Record the application commit, database/migration version, browser, server Philippine date/time and tester before starting. Use two independent browser profiles for simultaneous users; two tabs of one profile share the same login.
3. Prepare the fixtures below. Prefix human-entered test references with a unique run code, respecting field-length limits. Retain a ledger mapping aliases to saved IDs and document numbers.
4. Execute the end-to-end journeys first, then each module's independent and negative cases. Use separate COS/bills for destructive or locking transitions; do not reuse a cleared journey to test editable drafts.
5. For every mutation, verify the visible result, reload the page, inspect related lists/Job Progress, and check audit evidence where available. A success toast alone is insufficient. For financial cases, reconcile saved totals and exported/printed totals.
6. Record one result per case and per listed variant. Use **Not run / Pass / Fail / Blocked / Not applicable**. A blocked dependency is not a pass. State the reason for Not applicable; deferred features belong in section 20 instead.
7. For denied actions, test both the absent/disabled control and a direct request using the restricted user's own session. A hidden button is not authorization. Use browser Network tools to copy a request, retaining the test user's own antiforgery token; never share authentication cookies in evidence.
8. Record browser console/network errors, screenshots, relevant IDs, actual amounts and the expected result on failures. Retest the original failure and the connected journey after a fix.

Execution ledger template (copy one row per case/variant):

| Run | Case / variant | User | Fixture IDs / numbers | Expected / actual amounts | Result | Evidence / issue | Tester / Philippine timestamp |
|---|---|---|---|---|---|---|---|
| RUN01 | E2E-01 | Named test user | Schedule / COS / tickets / bills / receipt | Attach reconciliation | Not run | Screenshot / export / issue link | Name / timestamp |

Unless stated otherwise, dates refer to Philippine time, rates are approved and applicable to the actual service date, schedules are current-approved, and records belong to MMSI. A request rejected without changing persisted state is a successful negative test. If observed behavior conflicts with a safety or accounting expectation below, file a defect or a policy question; do not change the expected result merely to match the screen.

## 2. Fixtures and independent calculation sheet

| Alias | Prepare |
|---|---|
| C-LOCAL | Active local vatable customer; known address, terms, 10% default COS discount, EWT/WVAT flags recorded. |
| C-NONTAX | Active non-vatable customer, zero discount, no withholding. |
| C-OTHER | Second active customer, used for cross-customer validation and separate rate keys. |
| C-INACTIVE | Inactive customer for eligibility checks. Do not deactivate the main journey customer mid-run. |
| P-VALID / P-OTHER / P-INACTIVE | Active principal belonging to C-LOCAL; principal of another customer; inactive principal. Include distinct address and terms. |
| LOC-A / LOC-B | Two ports and at least two terminals under one port, plus a terminal under the second port. |
| S-A / S-B | Two maritime service types. |
| T-OWN | Company-owned tugboat. |
| T-EXT1 / T-EXT2 / T-EXT3 | Two external tugboats with the same owner/provider, one with another owner. Also prepare an external tugboat missing an owner for a negative case. |
| CAP-A | Valid captain/tug master. |
| BANK-A / BANK-B | Two MMSI receiving bank accounts with distinguishable account numbers. A third other-company account is used only for authorization/scope checks if available. |
| R-BASE | Tariff 10,000 and final BAF 2,000 for C-LOCAL / LOC-A terminal / S-A, effective by the service date. Approve tariff and BAF independently. |
| R-HISTORY | Earlier, current and future dated rates for the same key; second customer/terminal/service keys with different amounts. |
| FILES | Valid small ticket image/video; provider PDF/PNG/JPEG; invalid extension, disguised content, zero-byte and oversized files. Use synthetic content only. |
| TIME | A past Tuesday D, its following Monday D+6 and Tuesday D+7, all before today; a past cross-month pair; past overnight service; a deliberately future end time. |

Approval timestamps are server-generated. For a fresh run, use a previous-month actual service and approve billing in the current test month. The April-service/May-Sales-Book example requires existing controlled historical fixtures; entering a May billing date today does not create a May approval timestamp. Do not change production clocks or hand-edit financial history to manufacture a passing example.

Maintain an independent expected-amount sheet; do not copy the application's total as the oracle. Record exact input amounts, method, duration, discount and rounding. Finance must sign off the tax/report examples separately.

| Example | Expected service-charge arithmetic, before billing tax presentation |
|---|---|
| Per-hour tariff + per-hour BAF, 6 minutes, 10% discount | Raw 6 minutes / 0.1 hours; minimum chargeable duration 1 hour. Gross 10,000 + 2,000 = 12,000; discounted amounts 9,000 + 1,800 = 10,800. |
| Same rates, 90 minutes, 10% discount | Gross 15,000 + 3,000 = 18,000; discounted amounts 13,500 + 2,700 = 16,200. |
| Per-move tariff + per-move BAF, 90 minutes, 10% discount | Gross 10,000 + 2,000 = 12,000; discounted total 10,800, independent of duration. |
| Per-hour tariff + supplied Lumpsum/Industry BAF 2,000, 90 minutes, 10% discount | Gross 15,000 + 2,000 = 17,000; discounted amounts 13,500 + 1,800 = 15,300. No fuel formula is inferred. |
| Skip discount, 90 minutes, both per hour | Gross and net service charges 18,000. |
| SBMA enabled, 30 actual minutes | 87.5 × 30 = 2,625. Do not impose the one-hour service-charge minimum on SBMA. |

Also calculate cases with 0%, 100%, explicit zero rates, four-decimal rate inputs, and durations that produce rounding boundaries. The implementation calculates money to two decimals using .NET decimal rounding; record any sum-of-rounded-components difference instead of hiding it with a spreadsheet tolerance. Distinguish gross billing, discounted service revenue, VAT, withholding, cash/check received, outstanding gross balance and provider cost.

## 3. Test accounts and access matrix

Create active named User accounts with only the stated grant, plus Admin and SuperAdmin. Do not use Admin for all functional tests: it conceals missing permissions. Combined-duty users should be tested after isolated grants.

| Account alias | Supported grant / expected work |
|---|---|
| U-NONE | No access row or empty row; cannot enter MSAP. Repeat with a retired-only assignment if a controlled fixture exists. |
| U-PLAN | Create schedule (`CreateCustomerOrderSlip`); operational reads, no approval/edit unless separately granted. |
| U-EDITPLAN | Edit schedule/COS; permitted cancellation/completion, no creation/approval implied. |
| U-APPROVEPLAN | Approve schedule; no planning writes implied. |
| U-TICKET | Record actual ticket; no ticket edit/recalculation implied. |
| U-EDITTICKET | Edit actual ticket and review/recalculate charges; no schedule/rate approval implied. |
| U-RATEVIEW | View rates/history/export only. |
| U-RATEEDIT | Maintain rates and submit own drafts; no rate approval implied. |
| U-RATEAPPROVE | Review/approve submitted rates; no editing another preparer's draft. |
| U-BILL | Prepare billing; no final approval/edit/discard implied. |
| U-EDITBILL / U-DISCARD | Edit eligible single draft / discard eligible preparation, respectively. |
| U-APPROVEBILL | Final bill approval; no preparation writes implied. |
| U-COLLECT | Prepare/edit eligible receipts; supporting bill/COS reads; no banking writes. |
| U-BANK | Deposit, clear, return and redeposit; supporting receipts/bills/COS reads; no receipt preparation/edit. |
| U-PROVIDER | Prepare provider charges; no confirmation implied. |
| U-CONFIRMPROVIDER | Confirm provider charges; no preparation writes implied. |
| U-MASTER | Maritime master-file maintenance, subject to additional Admin boundaries. No workflow read access solely from this grant. |
| U-REPORT | Maritime reports and supporting workflow reads; no financial/operational mutations. |
| U-ADMIN / U-SUPER | All supported MSAP actions, still subject to current state, completeness and reviewed-revision checks. Shared Admin retains IBS access. |
| U-INACTIVE | Inactive account; MSAP denied even with old cookies or saved grants. |

No separate-person approval restriction has been agreed. A user with both preparation and approval grants may perform both; do not treat this as an implemented four-eyes policy.

## 4. End-to-end journeys

For every journey record schedule, COS, expected-service IDs, actual-ticket IDs, bill number(s), approved revision, receipt, bank events and corresponding report rows. Confirm no duplicate source records after refresh/retry.

| ID | Steps | Expected result |
|---|---|---|
| E2E-01 | Approve R-BASE; create C-LOCAL schedule with T-OWN/S-A; approve schedule; record past 90-minute ticket; prepare combined billing; review/approve; print; collect full net by check; deposit then clear. | One continuous linked workflow; amounts reconcile; Sales Book exists only after final approval; Job Progress reaches Fully Cleared. |
| E2E-02 | Repeat for C-NONTAX, no discount, per-move charging and cash receipt. | No invented VAT/withholding; per-move amount unchanged by duration; cash follows supported deposit/clear controls. |
| E2E-03 | C-LOCAL with valid principal; two tugs/services; prepare general split Basic/BAF; approve both statements as required; collect both in one check; clear. | Two distinct billing numbers/balances and Sales Book rows; principal presentation retained; service activity counted once. |
| E2E-04 | Tentative schedule; open urgent entry; record actual service before approval; attempt billing; later approve schedule, resolve charges, bill and clear. | Factual ticket retained without premature financial progression; same COS/ticket reused after approval. |
| E2E-05 | Record a ticket in D+7 cycle with only D BAF approved; save; approve D+7 BAF; review/recalculate; finish billing and collection. | Ticket saving works while calculation pending; BAF update alone does not silently rewrite saved ticket; explicit recalculation unblocks billing. |
| E2E-06 | Plan three services; record two; cancel the genuinely unperformed third with reason; finish operational booking; bill remaining valid services and clear. | Cancelled requirement retained in history, excluded from missing-ticket count; no deletion bypass or lost actual tickets. |
| E2E-07 | Use T-EXT1 and T-OWN; record tickets; save provider draft, confirm it; bill customer and clear independently. | Confirmed AP Other Tugs reflected in reports; pending provider cost never blocks actual ticket/customer billing; no MSAP payment/check created for provider. |
| E2E-08 | Two approved COS billings for one customer; select both in one check receipt; deposit, return, redeposit, clear. | Both full settlements retained; banking history recorded; no duplicate receipt/revenue and no Fully Cleared state before final clearing. |
| E2E-09 | Record previous-month actual service and finalize billing in current test month; also inspect April/May historical fixture if available. | Service reports retain service month; Sales Book uses final-billing approval period, independently of service/billing date. Month closing does not block current agreed workflow. |
| E2E-10 | Revise approved schedule before any actual ticket/billing; use stale reviewer tab; reapprove current revision; continue through billing. | Same COS identity, new revision; stale approval rejected; current approved terms govern tickets. |
| E2E-11 | Finish tickets; prepare single billing draft; edit allowed fields; attempt stale final approval; approve latest; collect. | Draft revision changes; stale approval denied; one approved revision snapshot, correct persisted totals. |
| E2E-12 | Prepare split draft; discard eligible pair; prepare corrected split pair; finish normally. | Both old draft components removed together and tickets reusable; corrected pair linked; no orphan bill or Sales Book snapshot for discarded drafts. |

## 5. Master files and reference preparation

Apply MF-01 through MF-04 to each supported page: Customers, Principals, Vessels, Tugboats, Tugboat Owners, Ports, Terminals, Maritime Services, Captains/Tug Masters, Payment Terms, Bank Accounts, and any Employee/Supplier reference page still exposed to the tested account. Execute only actions actually exposed by that page; record unsupported operations as Not applicable with the reason. Supplier records are not a prerequisite for Other Tug Charges.

| ID | Steps / variants | Expected result |
|---|---|---|
| MF-01 | List, search, sort/page where available; create valid reference; reload and find it. | Correct values persist; record available to authorized dependent selects. |
| MF-02 | Edit supported fields, reload; repeat with missing required fields, duplicate business code and invalid relationships. | Valid edit persists; invalid input gets actionable validation without partial save. Capture exact duplicate policy by entity. |
| MF-03 | Attempt supported delete/deactivate on unused then referenced records. | Dependencies/history preserved; referenced data not silently orphaned. Record supported block/deactivation behavior. |
| MF-04 | Test unauthorized page, lookup, save and export requests directly. | Existing master-file/Admin boundaries enforced; no data/write leak through alternate routes. |
| MF-05 | Customer discount 0, 10, 100, negative, above 100; change default after a schedule is approved. | Allowed range enforced; approved COS retains its reviewed discount rather than silently inheriting later default changes. |
| MF-06 | Customer VAT types, EWT/WVAT flags, address and terms; principal with distinct terms/address. | Billing/collection use configured settings; expected tax examples recorded for Finance sign-off. |
| MF-07 | Change port in a schedule and editor; attempt terminal from another port. | Terminal options and saved relationship match selected port; forged mismatch rejected. |
| MF-08 | Company-owned/external tug and owner assignments. | Own/external distinction retained; provider editor uses external tug's actual owner, not a Supplier name/link. |
| MF-09 | One and then two receiving bank accounts. | Dropdown labels identify account; saving bank ID populates correct account details without retyping numbers. |
| MF-10 | Customer export and Admin-only employee/bank/supplier export, including a U-MASTER direct request. | Export scope follows access; additional Admin boundary enforced even with master-file grant. |
| MF-11 | Admin role-management page: inspect available users/roles; perform a supported role change on a disposable account; repeat as ordinary User and with stale cookies. | Admin boundary and active-role resolution retained; role selection alone does not grant an ordinary User unsupported procedures. Do not change the sole administrator's access. |
| MF-12 | Open in-app documentation from navigation, follow workflow links and return; repeat without MSAP access. | Available documentation renders and links to supported flow; no module-entry bypass for denied user. |
| MF-13 | Admin opens Master File References → Master File Import; select supported CSVs/folder, validate, review table and import. Repeat as ordinary master-file user. | Admin-only route; dependency-safe master-only import. Unsupported financial/user files explicitly ignored; ordinary User denied. tariff.csv stages separate Tariff/BAF review drafts. Preview changes no records. |
| MF-14 | Reimport add-only, then select explicit update mode; alter source/options/stored master between preview and import; submit bad flag/reference/code/header. | No duplicate IDs; only mapped fields updated; stale fingerprint rejected; one invalid row prevents whole selected batch saving. No reset/financial/rate writes. |
| MF-15 | Blank VAT/ownership/terms, missing TIN/address, optional tug PORT versus selected fallback, repeated principal/terminal/vessel names. | Unknown classifications/terms rejected; no invented TIN/ZIP/home port; existing operational settings preserved. No name-based merges. Meeting confirmations remain tracked in CSV/model review. |
| MF-16 | Correct blank VAT/ownership/terms in editor; change file/filter/page, revalidate, then import. Attempt changed/removed/extra rows or hidden metadata through JSON. | Edits survive navigation and retry; highlight and review fingerprint invalidate; server checks all corrections and source identity. Unauthorized row/metadata changes rejected. |
| MF-17 | Download headers and validated cleaned Excel; inspect codes, account/TIN text, bool choices and formula-like names. | Readable supported-field headers, no fabricated missing data or executable source formulas; export alone changes no database records. Completed XLSX can be uploaded and freshly validated; report sheets/notes are ignored and formulas rejected. |
| MF-18 | Inspect Customer Create/Edit, model and export after cleanup; submit retired commission/station/credit/retention/multiple-terms/price-adjustment fields. Inspect Port and schedule SBMA controls. | Retired fields, controls and commissionee lookup/relationship absent in MSAP; posted extra fields cannot restore them. Port has no SBMA field; schedule SBMA remains authoritative. IBS models/tables unchanged. Cleanup migrations drop retired values; rollback restores defaults only. |
| MF-19 | Validate a batch with missing customer WHT/WVAT/discount/ZIP, invalid values and unselected masters; export issues, complete highlighted cells and return XLSX. | All supplied records retained; comments, row notes and Issues explain problems. File status distinguishes unselected/existing/absent masters. Explicit False/0 retained; no tax guessing or writes on export/upload. Fresh validation and reviewed import required. |
| MF-20 | Import four-digit customer codes, then create new customers with existing legacy and prefixed codes; repeat concurrent creation. | Known implementation gap: current generator assumes a three-character prefix. After numbering policy/fix, generate unique valid MSAP codes, preserve imported identities and keep dependent selects/exports/reimports consistent. Do not mark passed before the fix and execution. |


## 6. Rates: spreadsheet editor, drafts, approval and exports

Run common cases once for **Tariff** and once for **BAF**. They are independent tables. The supported entry path is Master Files → Rates / rate editor; spreadsheet file upload is not the current workflow. Exports remain supported.

| ID | Steps / variants | Expected result |
|---|---|---|
| RATE-01 | Open editor as view-only, preparer, approver and Admin. | All authorized users can read/export; only maintenance grant enables edit/save; approval is independently controlled. |
| RATE-02 | Switch Tariff/BAF, load historical/current/future as-of dates. | Correct table and applicable dated rows; switching does not mix tariff amount with final BAF. |
| RATE-03 | Filter customer, port, terminal, service, text search, changed-only; combine and clear filters. | Matching rows/counts; terminal follows port; hidden edited rows do not disappear from saved draft. |
| RATE-04 | Edit amount, revert it, change effective date, revert date. | Changed/new/unchanged labels and subdued highlights track actual differences; existing reference columns locked. |
| RATE-05 | Add rate with all four references; try with missing references or mismatched terminal. | Valid new combination added; invalid combination rejected; existing rows' identity not overwritten. |
| RATE-06 | Tab between cells, Enter down rows; paste one amount column, then ISO-date plus amount columns across several rows. | Correct target cells/rows, preserved references and visible validation; paste does not shift amounts to wrong customers. |
| RATE-07 | Paste beyond visible rows, invalid dates/text, locale separators, blank lines and excess columns. | No silent misalignment/corruption; unsupported input rejected or visibly marked before save. Record accepted numeric format. |
| RATE-08 | Blank amount, explicit 0, negative, four/five decimal places, maximum 99,999,999,999,999.9999 and above maximum. | Blank invalid, zero valid; nonnegative/four-decimal/maximum constraints applied server-side. |
| RATE-09 | Missing/invalid dates, years below 1900/above 9998; duplicate customer+terminal+service+effective date. | Invalid rows identified; no draft/live partial writes. |
| RATE-10 | Invalid/non-MMSI customer, missing reference or forged existing rate ID. | Server validates reference ownership/key instead of trusting browser-provided names/IDs. |
| RATE-11 | Save mixed edited/new/unchanged rows; reload saved draft and compare. | Draft values preserved; live rates unchanged; counts and previous/new values understandable. |
| RATE-12 | Reopen own unsubmitted draft and edit/save; attempt another user's draft or submitted/approved draft editor URL. | Only owner's unsubmitted draft editable; forbidden/unavailable draft not overwritten. Admin does not bypass ownership/state rules. |
| RATE-13 | Select only some changed/new rows, submit; attempt no selection or unchanged-only selection. | Only actionable selected rows submitted; empty selection rejected; unselected rows do not activate. |
| RATE-14 | Preparer submits, approver reviews; repeat submit and approve requests. | Submit leaves live values unchanged; approval activates selected rows once; retries do not duplicate dated rates. |
| RATE-15 | Correct same effective date, then create a new later effective date. | Correction updates that dated record with old/new evidence; new date creates retained dated history; earlier dates still resolve correctly. |
| RATE-16 | Two preparers compare same key; approve A; submit/approve stale B. | Baseline conflict blocks B; comparison shows current stored values in a table. No silent last-writer overwrite. |
| RATE-17 | Refresh stale comparison, inspect differences, resubmit and approve. | Refresh clears submitted state; renewed review required; selected changes activate only after approval. |
| RATE-18 | Two approvers approve same draft concurrently; approve overlapping drafts concurrently. | One activation per draft; baseline/transaction guards prevent duplicate or stale overwrite. |
| RATE-19 | Account missing full name attempts save, submit, refresh and approve. | Clear full-name requirement; no anonymous/username-only rate change evidence. |
| RATE-20 | Export current rates at date before/on/after a change; export date-range history. | Correct independent table, effective dates, four-decimal amounts, reference codes, names and history metadata. Read-me sheet explains final amounts. |
| RATE-21 | Empty export range, reversed range, invalid dates/table kind and unauthorized export request. | Clear validation/empty result; no malformed workbook, server exception or unauthorized export. |
| RATE-22 | Compare exported same-date correction with review history. | History export represents retained dated records; correction's old/new evidence remains in review/audit, not fabricated as duplicate effective-date rows. |
| RATE-23 | Navigate to old Upload action and old tariff maintenance links. | Supported redirect goes to editor; no file-upload prerequisite or retired tariff-writing UI. |
| RATE-24 | Simulate failed draft save/network interruption; retry and reload. | Error visible, in-page edits retained when request fails; inspect for duplicate draft/live changes before retrying. |
| RATE-25 | Save 10,000 rows, then zero/10,001 rows in a controlled request; exercise large paged/filtered grid. | Supported limit enforced; no unintended partial draft; usable scrolling/filtering without lost edits. |
| RATE-26 | Load legacy tariff.csv, correct in editor, validate and import with/without new masters; inspect both generated drafts; submit/approve through Rates. Retry old fingerprint and simulate second draft failure. | All tariff/final BAF amounts staged with source IDs/types; no live activation before approval, duplicate retry or partially committed master/draft batch. Unsupported vessel-specific rates blocked; legacy scope visible for reviewer. |


## 7. Rate resolution, BAF freshness and recalculation

| ID | Steps / variants | Expected result |
|---|---|---|
| CALC-01 | Use service dates before/on/after dated tariff/BAF changes; ticket entry date is today. | Resolution follows actual service start date, not data-entry/ticket/billing date; future rates never apply early. |
| CALC-02 | Same key across two customers, terminals and services; include fallback combination configured in current resolver. | Exact key/fallback choice matches recorded rule; no unintended other-customer/location rate. Finance confirms fallback examples. |
| CALC-03 | Service on D, D+6 and D+7 with BAF effective D. | Tuesday–Monday coverage valid for first two; next Tuesday needs current-cycle approved BAF. |
| CALC-04 | New-cycle BAF exists only as draft/submitted; save actual ticket and try billing. | Actual facts save; calculation remains pending and billing blocked until approved applicable BAF plus recalculation. |
| CALC-05 | Old tariff but fresh BAF; fresh tariff but old BAF. | Weekly freshness applies only to table-derived BAF; refreshing tariff does not satisfy missing BAF coverage. |
| CALC-06 | Approved manual BAF, with table BAF missing/stale; manual tariff with table-derived BAF. | Manual BAF uses approved final value without weekly-table gate; manual tariff alone does not bypass BAF freshness. |
| CALC-07 | Missing tariff, missing BAF, explicit zero tariff/BAF. | Missing remains pending; zero is calculable where explicitly supplied and valid; no invented zero/fuel calculation. |
| CALC-08 | Approve new BAF affecting unbilled saved tickets. | Saved ticket amounts not silently changed; approved update identifies eligible affected tickets and leads to charge review. |
| CALC-09 | Review affected tickets including manual-BAF, billed, cancelled and already-prepared billing COS. | Only supported unbilled candidates shown; manual BAF/financially locked jobs not offered unsafe recalculation. |
| CALC-10 | More than 200 candidate tickets in a controlled dataset. | Impact-limit notice shown; first 200 are not presented as exhaustive; use COS charge review for older jobs. |
| CALC-11 | Open old/new charge preview, cancel, reopen and confirm. | Preview/cancel changes nothing; confirmation persists reviewed charges, status and audit evidence. |
| CALC-12 | Missing or stale fingerprint; edit ticket/rates in another profile before confirm. | Recalculation rejected; refresh comparison required; no stale amounts applied. |
| CALC-13 | Recalculate unapproved COS, wrong COS/ticket pair, mismatched planned service or COS with draft/approved billing. | Blocked by current-approval/identity/unbilled checks, including when caller is Admin. |
| CALC-14 | Recalculate while applicable rate still missing/stale. | Job remains visibly pending; failure does not falsely mark For Billing or expose zero as confirmed revenue. |
| CALC-15 | Per-hour/per-move tariff crossed with all four BAF methods: Per hour, Per move, Lumpsum, Industry. | Eight combinations match independent arithmetic; only Per hour BAF multiplies by chargeable duration. Supplied final values suffice. |
| CALC-16 | Durations 6, 59, 60, 61, 90 minutes and multi-day; repeat discounts 0/10/100 and skip. | Raw duration retained; one-hour minimum applies only to hourly service charging; per-move/final BAF stays flat; rounding consistent. |
| CALC-17 | Approve rates after customer billing is prepared/approved and re-open statements/reports. | No silent mutation of saved financial documents or Sales Book snapshots; linked-billing guard blocks recalculation. |

## 8. Calendar and schedule preparation

| ID | Steps / variants | Expected result |
|---|---|---|
| SCH-01 | Open month, previous/next/today; select day; direct day URL; choose schedule. | Correct dates and selected context; calendar replaces separate daily schedule without navigation dead end. |
| SCH-02 | Filter port/terminal, service/status where exposed; combine/reset; include no matches. | Cards/counts/day panel reflect filters; no unrelated terminal data or blank broken layout. |
| SCH-03 | Hover/open vessel/day preview, inspect assigned tugboats; use keyboard/click on touch-size viewport. | Informative card/panel with accessible way to open details; no hover-only essential action. |
| SCH-04 | Tug bookings view, overlapping own/external tugs, midnight and month boundary plans. | Correct visible booking spans/conflict context; cancelled bookings excluded from conflict occupancy. |
| SCH-05 | Create minimal valid plan; inspect summary, save for reviewing, reload. | Same entered data/expected services persist; tentative approval state; no automatically fabricated actual ticket. |
| SCH-06 | Missing customer/vessel/location/service/tug/required dates; end before start; forged IDs. | Useful validation; no partial schedule/COS or lost valid form fields. |
| SCH-07 | Multiple tugs, multiple/repeated services; add/remove unsaved row then save. | Each requirement has stable separate identity and correct tug grouping; no unintended duplicates/omissions. |
| SCH-08 | Detected tariff/BAF, absent rate/manual fallback, tariff basis and each BAF method. | Detected values locked; legitimate missing-rate manual inputs clearly identified; no override through ordinary locked controls. |
| SCH-09 | Recurring/non-recurring display with table-derived/manual terms. | Label matches current rate-source classification; recurring does not fabricate repeating calendar bookings. |
| SCH-10 | Default discount, skip discount, 0/100%, invalid discount; SBMA toggle on/off. | Approved commercial settings preserved; invalid range rejected; summary matches actual saved settings. |
| SCH-11 | Overlapping bookings; save/approve without then with supported conflict acknowledgment. | Conflict warned/blocked where required; explicit override recorded; no silent acknowledgment from urgent entry. |
| SCH-12 | Two users save/approve conflicting tug bookings concurrently. | Revalidation prevents unnoticed conflicting confirmation; override, if used, requires explicit current acknowledgment. |
| SCH-13 | Clear/reset modern selects, change dependencies, keyboard navigation and validation focus. | No hidden-required-control error; dependent selections reset consistently; labels and summary stay synchronized. |
| SCH-14 | Cancel confirmation dialog for save/approval and reopen it; then scroll. | No saved change and no locked body scrolling after dismissal; primary/secondary actions remain distinct. |

## 9. Schedule review, COS revisions, urgent entry and cancellation

| ID | Steps / variants | Expected result |
|---|---|---|
| COS-01 | Review new schedule; inspect booking/commercial/tug service data and approve. | Correct review summary; one linked COS; expected rows preserved; no actual tickets until factual entry. Initial UI does not imply prior revision. |
| COS-02 | Refresh/repeat approval; two approvers confirm same current schedule. | Same COS identity; no duplicate COS/expected rows/tickets. |
| COS-03 | Edit approved plan before any tickets/bills, then review changes and reapprove. | Revision increments, approval invalidated; old/new terms visible; COS identity retained; current terms approved explicitly. |
| COS-04 | Reviewer A keeps page open; planner B changes plan; A approves old review. | Stale reviewed timestamp/revision rejected; reload required. |
| COS-05 | Attempt plan revision after actual ticket, billing or cancelled service row exists. | Revision blocked; no removal/replacement of existing service history. |
| COS-06 | Open urgent entry from tentative schedule twice; record one then several required tickets. | One stable COS and one ticket per requirement; facts allowed before approval without granting approval. |
| COS-07 | Pending urgent COS: try bill preparation, charge progression and provider confirmation; later approve. | Financial gates block while unapproved; approval reuses tickets and calculates eligible charges, still respects missing/stale rates. |
| COS-08 | Cancel whole unused tentative/approved booking; try subsequent approve/edit/ticket entry. | Cancelled state retained with audit; invalid future actions denied; calendar/progress updated. |
| COS-09 | Cancel an unperformed expected service with reason; repeat with empty reason or service with actual ticket. | Genuine unperformed cancellation retained; reason required; actual-service record cannot be erased as a billing shortcut. |
| COS-10 | Complete booking with missing requirements, then complete after requirements resolved/cancelled. | Incomplete job cannot be falsely completed; operational completion does not mean billing paid/cleared. |
| COS-11 | Attempt cancel/complete once billed/collected, and repeat completed action. | Financial/history guards respected; no duplicated side effects or unexplained reopening. Record exact supported message. |
| COS-12 | Legacy standalone COS create URL and permanent delete URL. | Create redirects to schedule; retired destructive/direct-create route cannot bypass reviewed flow. |

## 10. Actual tickets and attachments

| ID | Steps / variants | Expected result |
|---|---|---|
| TKT-01 | Record expected service with actual ticket number/date, captain, start/end and remarks. | Ticket belongs to correct COS/tug/service; actual dates persisted; applicable charges calculated automatically, no manual tariff approval stage. |
| TKT-02 | Blank/over-20-character ticket number, missing date/captain/time; invalid captain ID. | Validation explains missing field; no partial ticket. Test duplicate ticket number and record enforced scope; escalate if operational uniqueness is undefined. |
| TKT-03 | End equals/before start, future end, valid past 6 minutes, past overnight/multi-day service. | Invalid times rejected using Philippine current time; valid past interval accepted even when planned time differs. Raw duration and billable hours distinct. |
| TKT-04 | Change actual service start date across a rate boundary versus planned date/ticket date. | Actual service-date rate resolved; no reuse of wrong planned-date amount. |
| TKT-05 | Save confirmation: cancel then confirm; double-click/retry same expected row. | Cancel saves nothing; confirmation works; one actual ticket per expected requirement. |
| TKT-06 | View ticket, then edit beside Close; change allowed facts and save. | Same reusable entry form; view captain/selects locked; edit activates proper inputs; no unexpected auto-scroll/underlined action. |
| TKT-07 | Billed, cancelled/closed or operationally completed booking: try new ticket/edit and forged POST. | State guards reject disallowed factual changes; no alteration of approved bill contents. |
| TKT-08 | Forge expected-row ID, tug/service or COS belonging to another booking; concurrent two-person record. | Identity rechecked; no cross-COS ticket or duplicate recorded requirement. |
| TKT-09 | Valid image/video attach, reload, view/download and replace through supported edit. | Correct file shown for that ticket; no broken reference or attachment on another ticket. |
| TKT-10 | Invalid/oversized/empty/disguised ticket attachment and failed storage upload. | Enforced upload rules show useful error; no executable served as trusted media, corrupted ticket or abandoned successful-looking save. Record actual ticket media limits separately from provider limits. |
| TKT-11 | Open recorded ticket from ticket registry/COS; inspect actions/status/row highlight. | View Ticket opens existing entry; correct tug/Entering details row highlighted; no obsolete checkbox, ForTariff action or Delete/Restore control. |
| TKT-12 | Create, edit and recalculate; inspect AccountingSourceId with authorized data inspection. | Server-generated nonempty source ID stays unchanged; no source identity advertised as a posted GL journal. |

## 11. Other Tug Charges / AP Other Tugs

| ID | Steps / variants | Expected result |
|---|---|---|
| AP-01 | Open Record charges from actual T-EXT1 ticket/COS/dashboard. | Provider inferred from tug owner; actual recorded tickets available; no IBS Supplier link prerequisite. |
| AP-02 | Try company-owned tug, missing-owner external tug, no actual times, cancelled expected service or wrong COS. | Ineligible selection not accepted; any dashboard link to an unusable editor is recorded as a defect. |
| AP-03 | Allocate T-EXT1/T-EXT2 tickets from same provider across COS; attempt T-EXT3 from another provider. | Same-provider multi-tug/multi-COS document supported; different provider rejected; retain at least one line for editor's COS. |
| AP-04 | Provider reference/date, Includes VAT choice, remarks and line amounts; save and reopen. | Draft persists correct allocation/sum; false VAT choice valid; modern select does not block submit with hidden required control. |
| AP-05 | Blank vs zero, negative/three-decimal/above-limit line amount; duplicate ticket, zero or over-500 allocations; excessive total. | Blank remains missing, explicit zero valid; two-decimal/uniqueness/count/total constraints enforced. |
| AP-06 | Missing/over-100 reference, future/invalid date, over-500 remarks or no VAT choice. | Server rejects before partial save. |
| AP-07 | Repeat provider reference with case/whitespace variations; allocate ticket already used on another charge. | Normalized reference uniqueness/provider scope and one-allocation-per-ticket enforced; open existing document instead of duplicating. |
| AP-08 | Valid PDF/PNG/JPEG up to 10 MB; invalid MIME/signature/extension, empty/over-limit document. | Supported document accepted; invalid files rejected; signed link opens correct document. |
| AP-09 | Edit draft/reference/allocations/document; simulate storage failure; two editors save same revision. | Draft revision/audit preserved; stale save rejected; no half-updated allocations/document reference. |
| AP-10 | Confirm with one linked schedule unapproved; approve all, review and confirm. | Unapproved source blocks confirmation; valid confirmation locks charge and populates reporting allocations. |
| AP-11 | Confirm stale revision, changed actual ticket/provider assignment, then repeat/concurrent confirmation. | Stale/current-source mismatch rejected; valid confirmation only applied once. |
| AP-12 | Confirm zero charge; leave another ticket missing and another draft-only. | Confirmed zero distinct from pending/missing; only confirmed amounts feed known AP/net-sales reporting. |
| AP-13 | Try editing confirmed charge; inspect payment and reversal controls. | Confirmed immutable; no fabricated amendment/reversal or IBS payment/check function. |
| AP-14 | Bill customer before provider cost entered/confirmed; subsequently record provider cost. | Billing not operationally blocked by pending cost; separate provider confirmation updates appropriate reporting without rewriting Sales Book revenue. |
| AP-15 | Open same multi-tug charge from different linked COS; inspect stored allocation identities. | Same charge/document; actual tugboat and stable ticket source retained per line for future IBS payment matching. |

## 12. Billing preparation, principal billing and split statements

| ID | Steps / variants | Expected result |
|---|---|---|
| BILL-01 | Select customer then eligible COS; inspect all tickets and summary. | Whole-COS preparation; no manual ticket-selection/without-ticket mode or empty editable COS reference. |
| BILL-02 | COS missing expected ticket, pending rate, unapproved revision, cancelled requirement/booking or already billed ticket; request direct save. | No partial billing around unresolved requirements; cancelled unperformed rows treated consistently, cancelled whole COS denied. |
| BILL-03 | Combined billing save, refresh and reopen. | One draft, correct links/number/date/terms/due date, all tickets included; no Sales Book entry/final print permission yet. |
| BILL-04 | Customer billing versus P-VALID principal billing. | Service customer/COS retained; billed-to identity/address/terms switch correctly; no transfer of source tickets to principal as customer. |
| BILL-05 | P-OTHER/P-INACTIVE, invalid principal/customer/COS IDs. | Active principal must belong to service customer; forged/invalid combination rejected. |
| BILL-06 | Toggle split for ordinary customer and principal, not only PHIL-CEB. | Two linked statements in one preparation, separate numbers/balances, Basic excludes BAF and BAF excludes Basic; same source service set. |
| BILL-07 | Blank/auto-generated numbers, custom number, same Basic/BAF number, duplicate in same company/year, over-10-character number. | Supported generation works; required length/uniqueness and distinct split numbers enforced server-side. |
| BILL-08 | Two users create bill/preparation for same COS or same year/number concurrently. | One valid preparation/number reservation; losing request clear error; no orphan pair or partially billed tickets. |
| BILL-09 | Documented vs Undocumented; save, review and print later. | Flag retained consistently; numbering/required fields follow current supported rules without fabricating tax-document identity. |
| BILL-10 | Try change VAT/withholding or tariff/BAF amounts through browser-modified request. | Customer tax settings and approved COS charges authoritative; no editable tax/rate override at preparation. |
| BILL-11 | Preview unsaved preparation, close, return to form and save. | Preview does not save/post; no Print Statement/options/Back to List; Close works with direct-tab fallback. |
| BILL-12 | Edit allowed single draft fields, reload and review. | Correct revision and totals; whole-COS completeness/approved terms retained. |
| BILL-13 | Try single-draft editor on either split component, including direct POST. | Blocked with paired-correction guidance; no one-sided split corruption. |
| BILL-14 | Discard eligible single/split draft; cancel dialog, then confirm; attempt GET/missing antiforgery token. | Cancellation no-op; authorized POST removes eligible preparation/pair atomically and releases tickets; GET/invalid token cannot delete. |
| BILL-15 | Try discard posted, paid, collected or book-history bill; try editing current-approved bill. | State/history guards deny; no routine reversal/unpost control or routed bypass. |
| BILL-16 | BAF rates 35,000 and 29,000 plus two matching rates; toggle combined BAF print after approval. | Matching rates may group; distinct rates retain separate visible groups and reconcile to unchanged BAF total. |
| BILL-17 | SBMA-enabled multi-ticket split job; inspect both statements/reports. | Agreed presentation preserved; service hours/share not duplicated merely because there are two bill numbers. Finance confirms any presentation-specific note. |

## 13. Final approval, print and Sales Book persistence

| ID | Steps / variants | Expected result |
|---|---|---|
| POST-01 | Open review with many tickets; Preview Statements at top next to return action; open final preview. | No bottom-only major action; consistent page spacing; final-review ticket images always available; actionable controls placed on preview as designed. |
| POST-02 | Try draft print endpoints/print-copy parameters/export directly. | Application print generation denied until current final approval; preliminary preview does not expose print controls. Browser/OS screenshot prevention is not a supported guarantee. |
| POST-03 | Approver confirms current single bill; preparer/report-only user tries same endpoint. | Only approval grant can post; persisted approval revision/actor/date and For Collection state; no source incompleteness bypass for Admin. |
| POST-04 | Edit draft after reviewer opens it; submit old reviewed revision. | Stale approval rejected without posting/book entry; current review required. |
| POST-05 | Approve split through supported review; inspect both component approvals and Sales Book rows. | No falsely fully-approved pair; every collectable/printable component has current approval and its own number/amount. |
| POST-06 | Repeat approval, refresh, double-click and simultaneous reviewers. | No duplicate revenue/book snapshot; one snapshot per billing number/approved revision. |
| POST-07 | Print approved statement with/without ticket images, optional matching-BAF grouping and supported dot-matrix export. | Original billing layout preserved; ticket images optional in print only; statement totals unchanged; correct pages for split. |
| POST-08 | Many tickets, long names/remarks, missing image, multiple image sizes, page breaks. | Legible complete print; no cropped totals, missing bill number, leaked Job Progress, screen controls or blank extra financial pages. |
| POST-09 | Close print tab; direct preview tab; refresh/reprint same approved revision. | No state/revenue duplication; close fallback usable; correct approval-dependent controls persist. |
| POST-10 | Finalize previous-month service now, with a previous-month billing date; export Sales Book for service/billing month then current approval month. | Book snapshot appears in server-recorded approval period, keyed to distinct bill number; billing date does not backdate recognition; service date preserved independently. |
| POST-11 | After posting, change master address/tax/default rates in separate fixtures; re-export saved book. | Posted snapshot remains historical evidence; no silent recomputation from changed masters. Escalate any report path using live rather than snapshot values. |
| POST-12 | Force test-only failure during final approval persistence, then retry after recovery. | No half-approved bill/split/book state; transaction rollback and idempotent retry checked using authorized data inspection. |

## 14. Collection preparation and full settlement

Run supported payment variants separately: cash only, check only, cash plus check; customer and principal billed-to selection; documented and undocumented receipt; single and multiple bills, including split components.

| ID | Steps / variants | Expected result |
|---|---|---|
| CR-01 | Select customer/billed party, outstanding approved bills and receiving account. | Modern selects; clear customer versus billed-party meaning; only eligible bills shown, correct account details; COS context disabled until a bill is selected. |
| CR-02 | Select bills from several COS, switch COS context and billed-party filter; remove/reselect rows. | Full settlement per selected bill; context leads to correct COS without altering allocation; no stale total/hidden selected bill after party change. |
| CR-03 | Set check number/date/issuing bank and receiving MMSI account. | Issuing bank stays distinct from company's receiving account; both persist correctly. |
| CR-04 | Missing receipt number unless undocumented; missing date/check date/number/issuing bank when check amount >0. | Field-specific validation, no hidden-select submit failure or partial receipt. Cash-only does not require fake check details. |
| CR-05 | Underpay/overpay a selected bill, duplicate bill allocation, negative cash/check/withholding or mismatched cash+check totals. | Full remaining net required per bill; no partial payment, overpayment or duplicate allocation accepted. |
| CR-06 | Local vatable EWT/WVAT, non-vatable EWT, no withholding, principal billing. | Server-derived per-bill withholding totals agree with reviewed Finance examples; net cash/check plus withholding reconciles gross settlement. Principal handling explicitly signed off. |
| CR-07 | Forged EWT/WVAT, wrong customer bill, unapproved/stale bill, already linked bill or zero balance. | Invalid allocation rejected server-side even if browser selection was bypassed. |
| CR-08 | Save multi-bill receipt and reopen each billing/COS. | Correct receipt link, paid amounts/balances and per-billing state; no premature Fully Cleared. |
| CR-09 | Same customer, several approved bill numbers including split; collect components separately then together on independent fixtures. | Independent balances maintained; grouping in one check allowed without merging bill numbers/revenue. |
| CR-10 | Concurrent collectors select same bill; retry same submission. | One valid allocation/receipt link; duplicate collection prevented; losing request leaves no orphan receipt. |
| CR-11 | Edit unprinted/undeposited receipt; add/remove bill, adjust payment method and totals. | Original allocation reversals/reapplication correct; removed bills restored to collectible balance; retained bills not double paid. |
| CR-12 | Change customer on saved receipt; edit after deposit, return or clearing; direct forged edit. If a controlled historical IsPrinted fixture exists, test that too. | Customer immutable; state locks enforced; validation preserves financial allocations. Do not invent a new receipt-print action to produce the historical fixture. |
| CR-13 | Preview receipt, refresh and follow each billing/COS reference; reopen eligible draft edit. | Correct references, payment and withholding; preview does not create a second receipt or invent a print transition. Current receipt preview has banking/edit actions; billing printing is covered separately. |
| CR-14 | Large selection with many bills, empty selection and no eligible bills. | Usable table/totals, meaningful empty state, no enabled save with missing bill selection. |
| CR-15 | Open AR monitoring/list filters including Pending, posted/uncollected/collected/cleared. | No enum/string numeric parsing error; correct bill/receipt amount and status distinctions. |

## 15. Deposit, return, redeposit and clearing

| ID | Steps / variants | Expected result |
|---|---|---|
| BANK-01 | Deposit undeposited receipt to BANK-A on valid actual date. | Bank snapshot/date saved, deposit state and audit updated across receipt/dashboard/progress. |
| BANK-02 | Missing/invalid/other-company bank; future date, date before receipt or check date. | Rejected with no partial state change; valid MMSI receiving account required. |
| BANK-03 | Deposit already-deposited receipt; clear before deposit; clear before deposit date. | Invalid transition denied, no duplicate history/amount. |
| BANK-04 | Clear deposited cash/check/mixed receipt on/after deposit date and no later than today. | Cleared state persisted; all linked bill/COS progress reflects that receipt. |
| BANK-05 | Return deposited check on/after deposit; try cash-only return, undeposited return, early/future return. | Valid return records date and removes current deposit; invalid transitions denied; settlement identity not duplicated. |
| BANK-06 | Returned check: redeposit BANK-B after return, then clear; repeat return/redeposit cycle on separate fixture. | Correct current receiving account/date, retained audit trail and totals; not counted as newly received revenue each cycle. |
| BANK-07 | Try redeposit before last return, clear while returned/undeposited, and bank updates after clearing. | Invalid transition denied; cleared receipt cannot be altered through these banking actions. |
| BANK-08 | Two users deposit/return/clear same receipt concurrently. | Row/state checks serialize result; no impossible simultaneous deposited/returned/cleared state. |
| BANK-09 | Multi-receipt COS: one cleared, another undeposited/returned. | Whole COS not Fully Cleared until all required current bills/receipts qualify. |

## 16. Reports, SBMA and financial reconciliation

Export every exposed Maritime Report: **Sales Book, Dispatch For Billing, Dispatch Ticket Summary and Sales Summary**. Include AR monitoring and current dashboard finance figures in reconciliation even when accessed from workflow lists.

| ID | Steps / variants | Expected result |
|---|---|---|
| REP-01 | Each report with empty, one-day, multi-month and reversed/invalid date range. | Useful validation/empty output; inclusive intended boundaries; no server crash or unrelated-company rows. |
| REP-02 | Dataset containing planned-only, missing ticket, pending charges, ready unbilled, draft billing, posted and collected jobs. | Each report includes its intended population; pending values not presented as confirmed zero revenue. |
| REP-03 | Reconcile combined versus split jobs with same service totals. | Two bill numbers do not double ticket count, duration, tariff, BAF, SBMA or COS revenue. |
| REP-04 | April service with May approval and later collection/clearing. | Service activity, Sales Book recognition, received cash and cleared cash retain distinct date meanings. |
| REP-05 | SBMA toggle off/on; 6, 30, 60 and 90 actual minutes; multiple tickets and split billing. | 87.5 per actual minute when applicable, no hourly minimum or split duplication; expected allocation reconciled. |
| REP-06 | External provider cost missing, draft, confirmed zero, confirmed positive; mixed owner/company tugs. | Missing/draft visibly pending; confirmed cost included once with proper tug attribution; known net-sales totals exclude unknown costs. |
| REP-07 | Customer/principal and VAT/withholding variants; compare bill, receipt and report rows. | Agreed tax treatment/rounding reconciles; record Finance decision rather than assuming sample workbook proves policy. |
| REP-08 | Export after rate/master change, repeat same export, refresh approved billing. | Saved Sales Book snapshot stable; no duplicate approved revision or recomputed historical sales. |
| REP-09 | Compare exports with independent fixture ledger: counts, gross, discount, BAF, tax, receipts, balance and provider cost. | Every difference explained by report scope/rounding; no unexplained variance accepted. |
| REP-10 | Report-only and non-report users request report export directly. | Report grant required; read access does not grant mutation; sensitive report data absent from unrelated dashboards. |
| REP-11 | Spreadsheet outputs: reference codes with leading zeros, long names and text starting with formula-like characters. | Literal references/text preserved safely; correct numeric/date cells and usable headers, widths, totals/page layout. |

## 17. Dashboard, shared Job Progress and navigation

| ID | Steps / variants | Expected result |
|---|---|---|
| UI-01 | Repeat dashboard for every isolated account in section 3 and combined duties. | Only relevant sections/actions/queries; rate-only/reference-only users do not see unrelated operations/finance; no empty dropdown group labels. |
| UI-02 | Compare each dashboard queue/count with corresponding list and eligible records; include empty queues and >6 records. | Counts/population consistent; six-row previews show total and lead to correct record; no truncated list presented as complete. |
| UI-03 | Missing provider cost and provider-review tables with long names, many records, empty state and narrow viewport. | Aligned table headers/cells, usable horizontal scroll, correct per-row action/access; no huge wrapping action blocks. |
| UI-04 | Inspect Job Progress at planning, approved, partial tickets, pending charges, billing draft, final approval, collection, deposit, return and clearing. | Same reusable sequence and connectors on all pages; correct blocker/past-tense done label; approved bill does not say Awaiting approval. |
| UI-05 | Click completed/current/future stages as isolated users, including multi-bill/multi-COS context. | Appropriate supported page/context; unavailable/unauthorized action not offered; no stage bypass or empty COS selector. |
| UI-06 | Back from filtered calendar/list, COS, rate details, billing review/preparation, receipt and provider editor. | Browser previous context used; preparation COS-details link separate; direct/new-tab/no-script entry has usable fallback. |
| UI-07 | Ctrl/Meta/Shift/middle-click Back; explicit breadcrumbs; Escape with/without open modal. | Native modified link behavior retained; breadcrumbs intentional destinations; Escape does not escape modal/alert into unwanted navigation. |
| UI-08 | Cancel then confirm SweetAlerts for all save/approve/discard/banking actions. | No scroll lock, double overlay or silent save; primary dark action distinct from neutral Back/Cancel. |
| UI-09 | Compact/full-width pages, breadcrumbs, modern selects, ticket layout, keyboard focus, validation and attention colors. | Consistent sharper controls/type/spacing; breadcrumbs use >; visible status text as well as color; entry row highlighted. |
| UI-10 | Empty/loading/failure dashboard data, long lists and back navigation after saving. | Meaningful state instead of misleading zeros/stale success; no JavaScript errors or dead-end major actions. |

## 18. Authorization, retired routes and audit

For ACC-02, execute each supported action with its isolated authorized account and at least one unauthorized account. Include GET page, POST mutation and supporting AJAX/download endpoint. A login redirect/challenge for anonymous users and a denial for authenticated unauthorized users are acceptable framework-specific responses; no successful unauthorized mutation is acceptable.

| ID | Steps / variants | Expected result |
|---|---|---|
| ACC-01 | Anonymous, U-NONE, empty/retired-only grants, inactive user and stale MSAP role cookie enter all MSAP areas. | Module denied; IBS-hosted MSAP link hidden; cookie role alone does not grant entry. |
| ACC-02 | Schedule create/edit/approve; ticket record/edit/recalc; rates read/edit/approve; billing prepare/edit/discard/approve; receipt prepare; banking; provider prepare/confirm; master files; reports. | Exactly the appropriate grant permits each action; supporting read dependencies do not imply write authority. |
| ACC-03 | Admin/SuperAdmin repeat supported actions on valid and invalid/stale records. | All supported duties accessible; state/completeness/revision guards still block invalid work. Shared Admin remains able to enter both applications. |
| ACC-04 | Grant/revoke duty while user has page open; user retries save/approval and refreshes dashboard/nav. | Next request respects effective access; denied action cannot succeed from stale UI/cookie; new visibility matches saved grants. |
| ACC-05 | User Access create/edit with named target, save and reload. | Supported grants stored and audit names shown; no retired flags in form; individual row not required for Admin. |
| ACC-06 | Forge target UserId/Id mismatch, inactive/non-User target, unsupported bound flag; non-admin maintains access. | Server rejects/ignores unsupported input per contract; no unauthorized reassignment or privilege escalation. |
| ACC-07 | Prepare-only collection versus banking-only, rate-view-only versus edit/approve, provider-prepare versus confirm. | Independent authorities verified on direct requests, not just hidden buttons. |
| ACC-08 | Retired billing Reverse, manual tariff Set/Edit/Approve/Disapprove/batch, ticket Delete/Restore/ChangeStatus/standalone attachment deletion, direct COS create POST, schedule permanent Delete. | No supported UI or routed mutation; direct request cannot restore retired flow. Historical service methods are not evidence of a public route. |
| ACC-09 | Old bulk financial import/reset and SuperAdmin raw financial editor URLs; month-close controls. | Reset/financial import actions unavailable; raw financial editor remains NonController; month-closing navigation absent. Restored Admin-only master importer cannot process financial files. |
| ACC-10 | Invalid/missing antiforgery, changed entity ID, cross-company reference and malformed enum/date/amount for each mutation family. | Request rejected or validated; no data leak or partial financial write. |
| AUD-01 | Inspect audit for schedule create/revise/approve/override/cancel, ticket save/edit/recalc, rates draft/submit/refresh/approve, billing/post, receipt/banking and provider save/confirm. | Successful transitions traceable to correct entity/source and actor; old/new evidence available where implemented; no invented success event for rejected action. |
| AUD-02 | Rate, provider and User Access UI/audit displays; user changes display name after historical action. | Full names used, stable user IDs retained; inspect historical/current-name behavior and flag ambiguous attribution. Usernames must not replace available full names. |
| AUD-03 | Search COS audit timeline, sort/page/filter trails; test allowed role and denied no-MSAP role. | Correct related history and supported access boundary; no unrelated COS history from bad ID/context. Record audit visibility policy if further restriction is requested. |
| AUD-04 | Compare source ID, COS/ticket IDs, bill number/revision, Sales Book identity and provider allocation. | Traceable identities remain stable across reload/edit/recalc and future-integration boundary; no claim of IBS payment or GL posting without such record. |

## 19. Recovery, concurrency and automated-check coverage

| ID | Steps / variants | Expected result |
|---|---|---|
| REL-01 | Network failure during schedule/ticket/rate/provider/billing/receipt save; reload and inspect before retry. | Each result is complete saved state or clean failure, never a successful-looking partial financial document. Retrying does not duplicate required records. |
| REL-02 | Expired login/antiforgery while form open; reauthenticate and resume. | Clear challenge/error; no accidental submission as another user; persisted state checked before resubmitting. |
| REL-03 | Controlled cloud storage outage on upload and signed-link retrieval. | Error/availability message; no cross-ticket document or financial corruption; operational data not falsely shown as fully saved. |
| REL-04 | Restart application and revisit representative approved/cleared records, submitted rate update and provider draft. | Database-persisted states, approvals, names, amounts and attachments survive; no reliance on browser/session-only bookkeeping. |
| REL-05 | Repeat stale/collision cases RATE-16/18, SCH-12, COS-02/04, TKT-08, AP-09/11, BILL-08, POST-04/06, CR-10 and BANK-08 in separate profiles. | Transaction/revision guards verified against real concurrent requests, not merely sequential UI clicks. |
| REL-06 | Owner verifies supplied migrations on test environment and reads fresh data through each module. | Required schema present, no unapplied-migration runtime failures; migration/reset remains owner-managed. No new migration needed for latest access slice. |
| REL-07 | Build solution; build existing Checks/MSAP project; run targeted workflow-access checks. | Build/access/rate-access/Job Progress checks pass; existing EF1002 check warning recorded separately. These are not substitutes for browser/database acceptance. |
| REL-08 | Run the focused schedule rendering check and investigate/rerun the broader Checks/MSAP path. | Focused schedule rendering now passes after fixture/route/label corrections, including an urgent COS without approval history. The broad command remains blocked by its stale 32-controller inventory assertion; do not label the full suite green. |

Existing commands (use isolated output if the running application locks its binaries):

```powershell
dotnet build "Integrated Business System.sln"
dotnet build Checks/MSAP/Check.csproj --no-restore --nologo -v:q -p:OutDir="$env:TEMP/msap-acceptance-checks/"
dotnet "$env:TEMP/msap-acceptance-checks/Check.dll" --workflow-access-only
dotnet "$env:TEMP/msap-acceptance-checks/Check.dll" --schedule-render-only
```

The targeted command is database-free. Inspect `Checks/MSAP/Program.cs` before running other modes; broader checks can depend on database/environment fixtures. Do not run resets or migrations merely to turn a test green.

For later Chrome Recorder automation, use one recording per journey/module segment, named with the case ID. Keep role provisioning, database inspection, exact exported-workbook checks and real two-user concurrency as separately documented steps. Browser recordings must not embed credentials or be treated as complete financial/assertion coverage simply because replay reaches the final page.

## 20. Deliberately unsupported or awaiting policy

| Boundary | What to test now / what must not be claimed |
|---|---|
| GL recognition and IBS synchronization | Stable AccountingSourceId and reliable source facts only. Actual journals/account mapping/reclassification/synchronization require separate implementation and accountant-approved examples. |
| IBS AP Trade – Tugboats | Confirmed allocations retain actual tug identity. No supplier prerequisite, provider payment, cheque generation, unpaid synchronization or duplicate-payment integration exists here yet. |
| Provider correction after confirmation | Verify immutable confirmation. Amendments/reversals and multiple provider documents per ticket need design/policy work. |
| Paired split edit | Verify blocked single-component editor and eligible discard/reprepare. A paired edit screen is not implemented. |
| Month closing | Current MSAP closing restrictions are suspended by agreement. Do not fail valid late-ticket workflows because an older plan expected closed-month gates. Closing/reopening policy needs a meeting. |
| Fuel | Final BAF entry works for all supported methods. Fuel prices/consumption/inventory/formulas and responsible personnel are not prerequisites or implemented fuel workflows. |
| Partial payments | Underpayment must be rejected; full selected billing balances may be grouped in one receipt. Partial settlement is outside current scope. |
| Per-tugboat workspace | Preserve tug attribution. Separate navbar company/workspace switching, opex/performance accounting is future IBS work. |
| Approval separation | Individual action grants exist. Different-person/self-approval prohibition and named department assignments are not automatically enforced policies. |
| Finance decisions | Principal withholding, rate fallback examples, provider tax treatment, VAT-inclusive assumption and SBMA settlement responsibility need explicit Finance reconciliation. Calculation/presentation alone does not settle payer/tax policy. |

## 21. Acceptance and sign-off

Do not sign off the workflow until:

- Every end-to-end journey and every applicable module case/variant has an execution result and traceable fixture IDs.
- No unresolved unauthorized write, duplicate billing/collection/book entry, lost ticket, stale approval acceptance, unexplained balance, cross-company leak or financial-history mutation remains.
- Finance signs the independent calculation/report reconciliation, including principal/split/withholding/SBMA/provider examples. Unresolved policy is recorded as Blocked, not passed by assumption.
- Operations confirms calendar, urgent service, expected-service completeness, cancellation and ticket correction behavior.
- Administrators confirm real named staff assignments, independent banking/approval duties and live access revocation.
- Prints and exports have been opened and checked; screenshots of a preview alone do not verify workbook content or page breaks.
- All observed defects are fixed and retested or explicitly accepted with owner/reason; deferred boundaries remain clearly documented.
- The known broader rendering-check failure is resolved or explicitly accepted as an outstanding verification limitation.

| Sign-off | Name | Date | Run / evidence | Open exceptions |
|---|---|---|---|---|
| Operations | | | | |
| Finance / Accounting | | | | |
| Banking / Treasury | | | | |
| System administrator | | | | |
| Release owner | | | | |

Source references: [current blockers](BLOCKERS.md), [SBMA decision](../../Meetings/sbma-explanation.md), current MSAP controllers, `RateUploadService`, `ScheduledTicketCharges`, `VesselScheduleService`, `DispatchTicketService`, `OtherTugChargeService`, `BillingService`, `BillingBookEntries`, `CollectionService`, `DashboardService`, `UserAccessService` and existing `Checks/MSAP` checks. Earlier wireframe/upload/month-closing proposals are historical where later decisions supersede them.
