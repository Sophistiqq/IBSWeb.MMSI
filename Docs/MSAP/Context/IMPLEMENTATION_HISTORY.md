# Implementation history — archived 19 October 2026

This is the implementation-plan snapshot preserved during context organization, including earlier proposals and superseded next goals. Use [current status](CURRENT_STATUS.md) and [blockers](BLOCKERS.md) for ongoing work. Read this archive only for the history or rationale of the affected area; it is not the active backlog.

---

# MSAP schedule/COS implementation plan

## Current status and next goal — 19 October 2026

This section is the current implementation checkpoint. Earlier phase tables, source inspections and wireframe proposals below are retained as history; they are not a current backlog. Later direct user decisions take precedence, including general reviewer wording, Admin access, final-value BAF entry, and suspended MSAP month closing. No staff member is assigned approval authority merely because an older meeting example names them.

### Implemented scope

- Schedule/calendar, revision-aware COS approval, urgent operational ticket recording before approval, and cancellation of unperformed services.
- Actual ticket recording/editing, automatic tariff/BAF calculation, expected-service completeness, explicit recalculation, and reusable clickable Job Progress.
- Independent tariff/BAF spreadsheet-style maintenance, exports, staged old/new comparisons and selected approvals. Tuesday–Monday freshness applies to table-derived BAF only; saving factual tickets remains possible while calculation is pending.
- Customer/principal billing, general split Basic/BAF preparation with distinct numbers and balances, final approval, approval-gated printing, optional ticket images and persisted per-number/per-approved-revision Sales Book snapshots.
- Multi-billing collection receipts, receiving-bank selection, deposit/clearing and returned-check/redeposit workflow. Partial settlement is outside the agreed current scope.
- Other Tug Charges capture, allocation, review/confirmation and named audit evidence in MSAP. IBS payment/check processing remains separate future work.
- Dashboard and compact portable UI system, committed as `cd11c80cd`. Shared primitives live in `ui-system.css` (`.app-ui`, `ui-*`, `--ui-*`); Job Progress uses `workflow-ui.css`. Schedule CSS is separated into shared `vessel-schedule.css`, form `vessel-schedule-form.css`, and calendar `vessel-calendar.css`. Use dark primary actions, neutral secondary actions and restrained semantic attention colors; preserve billing print layouts.

### Remaining gaps and decisions

| Item | Current boundary / remaining work |
|---|---|
| User access | Access cleanup implemented below. Confirm actual named staff assignments and complete live multi-user acceptance; initial inspection remains recorded below. |
| Master-file imports | Commit 3257f348b supports Admin-only master import, paged corrections, explicit update mode, named audit, header/clean Excel exports and failed-validation correction workbooks with comments, Issues and File status sheets. Completed XLSX files can be returned for fresh validation. Legacy tariff.csv stages separate Tariff/BAF approval drafts. Port SBMA and unused IBS customer fields are removed; two cleanup migrations are supplied, with application user-managed. Complete the confirmed master data and verify import/reimport/update/rollback in the test database. See [CSV/model review and meeting confirmations](MASTER_FILE_IMPORT_REVIEW.md). The agent has not run live imports or applied migrations. |
| Customer numbering | High priority before creating customers after import: CustomerRepository.GenerateCodeAsync assumes a three-character prefix (for example IND0001), while the importer retains four-digit legacy codes (for example 0015). Agree one MSAP numbering policy and implement generation/uniqueness checks compatible with imported and existing records without changing referenced codes. |
| GL / IBS recognition | `AccountingSourceId` supplies stable source identity only. GL recognition, journals, reclassification, account mapping and IBS synchronization are not implemented by that identity or by Sales Book persistence. |
| IBS AP Trade – Tugboats | Dedicated tugboat payables/payment integration remains future IBS work. Do not require an IBS Supplier link. Define source matching, unpaid eligibility and idempotent synchronization before implementing. |
| Other Tug corrections | Confirmed charges are immutable. Agree amendment/reversal and multiple provider bills per ticket before extending the current one-allocation-per-ticket workflow. Capture/review responsibilities remain a development proposal needing MMSI feedback. |
| Split corrections | No paired split-billing editor yet: delete/reprepare an eligible unposted preparation. Bills with book history cannot be deleted. |
| Finance reconciliation | Validate reporting examples, principal withholding across statements/collections, rate-match fallback rules, provider VAT/withholding and the fixed VAT-inclusive billing assumption (`IsVatInclusive = true`). Do not infer new policy from sample amounts. |
| Workflow acceptance | Execute the [full workflow acceptance test plan](WORKFLOW_TEST_PLAN.md), including rates/master files, denied actions, stale reviews, pending BAF/recalculation, urgent services, cancellations, split/principal billing, provider costs, reports, multi-bill checks and returned checks. User testing has been reported, but full case-by-case acceptance results are not yet recorded; Chrome Recorder/importable browser artifacts have not been produced. |
| Migration / checks | Migrations are supplied; application is user-managed. The last broader `Checks/MSAP` run encountered `ScheduleFormRenderingCheck.cs:98` ("Review action does not match revision state"); investigate before claiming the complete check suite passes. |
| User manuals | Docs/MSAP/manual still contains Job Order terminology and obsolete financial-import instructions/permissions. Update navigation, terminology and instructions to the current COS/master-only import workflow, and review the remaining manuals against current supported actions. Historical meeting records remain history. |

Deferred for discussion: MSAP month closing and late-arriving tickets; fuel inputs, consumption/inventory, formulas and responsible personnel; per-tugboat workspaces/performance/opex reporting; and partial payments unless explicitly requested. Supplied final BAF amounts remain usable without fuel inputs. An April service finally approved for billing in May retains its service date and appears in the May Sales Book; separate management-accounting recognition still needs the GL work above.

### Back navigation clarification — 19 October 2026

Back controls return through browser history, preserving the previous screen and its filter/context instead of always opening an index. Reuse portable `ui-navigation.js` and `a[data-ui-back]`; retain a real, relevant `href` as the fallback for direct/new-tab entry without back history and for no-script navigation. Keep modified clicks and explicit destinations/native link behavior intact. Back labels say `Back`; breadcrumbs and deliberate navigation links retain their named destinations. Billing preparation keeps Back separate from its selected-COS details link. Collection return controls and billing Review use the same behavior. Escape activates the page's Back control when present, retaining existing modal/alert guards. This does not change save/approval redirects or print-preview Close behavior.

### Dashboard responsibilities and MSAP entry — 19 October 2026

Later user direction: shared Admin retains both-app access; IBS authorization is outside this slice. MSAP must deny ordinary users with no MSAP work and tailor dashboard information to their responsibilities.

- `AccessMsap` is a derived permission, not a new user type or database flag. Active Admin/SuperAdmin retain supported MSAP access; an ordinary User needs an existing User Access assignment with at least one supported grant. A retired-only, empty or missing assignment cannot enter. The shared-user role fallback remains available for provisioning; it is no longer sufficient for module entry. A role claim alone is not a procedure assignment.
- Enforce entry through the module authorization handler for all three MSAP areas, rechecking effective access even when a cookie contains an MSAP role. Hide the MSAP link in its IBS-hosted navigation partial for denied users. The dashboard also denies entry before loading data. No IBS roles/routes are restricted, and no live account grants are changed.
- A valid MSAP User claim does not downgrade shared Admin authority. Preserve explicit MSAP SuperAdmin identity; invalid/ambiguous role claims and inactive accounts remain denied.
- Resolve dashboard section flags in `DashboardService.SetAccessAsync`, using the existing access service. Apply the same flags to query execution and view sections; supporting-record read access does not automatically fill the dashboard with unrelated responsibilities.

| Dashboard information | Relevant grants |
|---|---|
| Calendar, today/upcoming schedules and operational activity | Schedule create/edit/approve, ticket record/edit, or maritime reports |
| Missing actual tickets | Ticket record/edit or maritime reports |
| Schedule, rate and final-bill approval queues | Their respective approval grants only |
| Billed and known-unbilled amounts | Billing prepare/edit/discard/final-approve or maritime reports |
| Receipt totals | Collection preparation, banking or maritime reports |
| Outstanding approved-bill balance | Billing or collection dashboard access |
| Deposit/clearing/returned-check queue | Banking or maritime reports; receipt preparation alone does not load it |
| Pending calculations | Ticket editing/recalculation, billing prepare/edit/final-approve, rate prepare/approve, or maritime reports |
| Missing provider-cost entries | Other Tug Charges preparation |
| Draft provider documents | Other Tug Charges preparation or confirmation; actual confirmation remains a separately authorized editor action |
| Rates/history/exports and master-file entry | Their respective existing grants |

The new provider queues use MMSI scope and exclude cancelled/invalidated COS and deleted tickets. Missing-cost entries require factual service times and no existing provider allocation; draft/confirmed zero entries are not classified as missing. Queues show at most six records with total counts and lead to existing COS/provider editors. Missing provider costs still do not block operational ticket entry or customer billing. Rate-only calculation follow-up does not grant COS access; the COS link requires its existing read permission.

This slice adds no schema changes and leaves billing amounts, Sales Book recognition, collection state transitions and print appearance unchanged. Live dashboard/browser and database-query acceptance remain separate from compilation and permission checks. The web application and check project build passed; the targeted `--workflow-access-only` checks passed entry/revocation, shared Admin handling and dashboard responsibility combinations, plus the existing rate-access and Job Progress checks. Dashboard tour JavaScript syntax and diff/UTF-8/LF/final-newline checks passed. The existing EF1002 warning in `Checks/MSAP/SchedulingCheck.cs:72` remains. New dashboard queries and visual layouts have not been exercised against a live database/browser in this slice.

### Access cleanup implementation — 19 October 2026

The user authorized implementing the access cleanup together with retiring actions from the previous workflow. This supersedes the initial access inventory below; that inventory remains an inspection record, not current grants.

- Keep active Admin/SuperAdmin authority for supported workflow actions. Ordinary Users need explicit action flags; missing access records, inactive/invalid roles and unknown procedure values deny access. No actual user assignments were changed by the agent.
- Use derived `ViewOperations`, `ViewBillings` and `ViewCollections` checks for lists, details, previews, supporting lookups and navigation. These are read dependencies of existing grants, not new stored flags. Operational grants permit operational reads; billing grants permit billing and operational reads; collection/banking grants also permit supporting billing reads; maritime report access permits reading linked workflow records. Rate-only and reference-only grants do not grant workflow access.
- Retain separate preparation and approval controls for schedule/COS, rate changes, final billing and provider charges. Ticket editing includes explicit recalculation. Booking editing includes cancellation of unperformed services. No new different-person/self-approval rule is invented.
- `CanCreateCollection` prepares/edits receipts; existing `CanAccessTreasury` now explicitly controls deposit, clearing and returned-check/redeposit actions, independently of preparation. An ordinary User previously granted only collection preparation needs an administrator to assign banking if that is their responsibility. No automatic grant expansion or migration is supplied.
- User Access create/edit share one supported-permission form. Its list summarizes grants by workflow and displays names. Writes accept supported fields only, require an active User target, reject a mismatched posted target identity, retain stable user IDs/names in audit evidence and clear retired flags when a permission record is saved. Admin/SuperAdmin permissions do not depend on these individual rows.
- Remove billing reversal/unpost from the index and controller. Keep eligible draft edit/discard and separate final approval; discard now requires POST plus antiforgery validation. Split preparations cannot be edited through the single-bill editor; retain delete/reprepare and all existing service-side book-history/state restrictions.
- Remove manual ticket tariff setting/editing/approval/rejection/batch actions, arbitrary status changes, ticket deletion/restoration and standalone attachment deletion from routed actions and controls. Registry filters follow calculation pending / for billing / billed. Linked ticket inspection/editing reuses the COS actual-ticket form. Historical unlinked previews remain read-only; legacy tariff states remain blocked instead of acquiring a manual-approval bypass.
- Start new bookings through Vessel Schedule; the old direct COS create POST/view are retired and its GET redirects to schedule preparation. Require create-schedule authority on both schedule create routes. Cancel a mistaken/unperformed booking with audit retention instead of permanently deleting the plan.
- Retire old bulk financial imports/reset and the SuperAdmin raw financial-data editor because they bypass current approvals/snapshots. Later master-file cleanup restores `MsapImportController` as an Admin-only master-reference importer; its financial/reset actions and financial CSV inputs are removed. The raw financial editor remains `NonController`. Keep enum numbers, mapped retired columns, historical workflow service implementations, audit records and past migrations intact. Month-closing navigation is removed while that work remains deferred.
- Calendar, dashboard, Job Progress, COS/billing/collection indexes and receipt controls use the same effective read/action checks. A read grant never permits a save, approval or banking update. Rate access independently exposes its master-file entry. Master-file export requires reference access; employee/bank/supplier exports additionally require Admin policy, matching their administrative boundary.

Acceptance still requires the user's live multi-user walkthrough and confirmation of actual named staff assignments. The implementation does not assign permissions from department titles or introduce a role-template system. Validation: the web project and `Checks/MSAP/Check.csproj` built successfully. `--workflow-access-only` passed the new action-filter/permission checks, existing rate-access checks and updated Job Progress checks without querying or changing the database. The check project retains its pre-existing EF1002 warning in `SchedulingCheck.cs:72`. Changed inline JavaScript parsed after substituting Razor expressions, and `schedule-actions.js` passed `node --check`. Diff/UTF-8/LF/final-newline checks passed. These checks do not replace live browser, multi-user or full database-workflow acceptance; the previously recorded broader rendering-check failure has not been rerun in this slice.

### Next goal: solidify user access

**Outcome:** a reviewed action-by-action access matrix and consistent enforcement. Keep the existing role/individual-permission approach unless a concrete requirement requires changing it. Do not invent Operations/Finance roles or grant actual users permissions from job-title assumptions. Admin and SuperAdmin retain the user's instruction to perform all supported workflow actions; financial state/completeness/revision controls still apply.

Source baseline inspected on 19 October: `MsapRoles`, `RoleService`, `MsapRoleHandler`, `UserAccessService`, the MSAP module policy convention, access attributes, and the schedule/COS/billing/collection/rate/Other Tug/User Access controllers. This is a starting inventory, not a completed endpoint/security audit or a list of live user grants. No database/user-account query was performed.

| User category | Current source behavior |
|---|---|
| Active Admin / SuperAdmin | `UserAccessService.CheckAccess` grants procedure access without individual flags. User Access maintenance is protected by `MSAP.Admin`. Separate admin/superadmin policy restrictions must still be inventoried. |
| Active User | Procedure actions depend on the saved per-user access record; missing records deny procedure checks. Having a role does not itself grant every procedure. |
| Inactive / invalid role | Live role resolution denies access. The MSAP module convention adds the module access policy; stale cookie role claims are not sufficient. |
| Shared identity fallback | Without an explicit MSAP role claim, shared Admin maps to MSAP Admin; other active identities resolve to User. Review this module-entry policy explicitly. |

Initial action groups (ordinary User grants; Admin/SuperAdmin procedure bypass as above):

| Action group | Existing permission / observed boundary | Access-review question |
|---|---|---|
| Calendar and new schedule | Schedule index/create currently rely on module/authentication entry; create has no dedicated procedure attribute. | Who may read schedules versus create them? Check the service and every lookup/save route before closing this gap. |
| Schedule edit / approval | `EditCustomerOrderSlip`; separate `ApproveSchedule`. | Who prepares, revises and approves? Confirm urgent entry and reapproval visibility. |
| COS / service records | COS create/edit/delete/close flags; details accepts several workflow grants while its list accepts a narrower set. | Ensure each user's permitted work is reachable through lists and Job Progress without granting unrelated actions. |
| Actual tickets | `CreateDispatchTicket`, `EditDispatchTicket`, `DeleteDispatchTicket`. | Separate recording/editing/viewing, service cancellation and recalculation; examine legacy endpoints and post-billing restrictions. |
| Rates | `ViewRates`, `ManageRates`, `ApproveRateUpload`; prepare/approve also imply view. | Confirm who prepares versus approves, exports/history access and whether self-approval is allowed. Existing enum naming does not imply workbook upload remains the user flow. |
| Billing | Create/edit/delete/reverse flags; distinct `ApproveFinalBilling`. Review/preview/print currently commonly accept create or final-approve grants. | Confirm read-only/print access for collection and report users, correction/reversal authority and every split component. |
| Collections / banking | `CreateCollection` currently covers list, create, edit, preview, banking and selection lookups. | Decide whether receipt preparation and deposit/clear/return need separate grants; do not split them without an agreed need. |
| Other Tug Charges | `ManageOtherTugCharges` prepares; `ConfirmOtherTugCharges` confirms; either opens the module. | Identify actual preparers/reviewers; decide self-confirmation policy. Preserve pending provider costs without blocking factual service entry. |
| Master files / reports / import | `ManageMaritimeMasterFile`, `ViewMaritimeReport`, `ViewInventoryReport`, `ManageMsapImport`. | Inventory every screen/export, dashboard measure and lookup; confirm data scope and import authority. |
| Access administration | `UserAccessController` requires Admin policy. | Review user/role administration, allowed target users, grant-change audit and account deactivation separately from workflow approvals. |
| Legacy flags | `SetTariff`, `ApproveTariff`, treasury/disbursement and posted-period enum entries remain in source. | Identify active callers versus obsolete controls. Do not reintroduce manual tariff approval or month closing merely because a flag exists. |

Work sequence:

1. Inventory all MSAP endpoints and action controls, including read/list/export/print routes, AJAX/lookups, dashboard queues, Job Progress, legacy entry points and service-layer guards. Record current permission, required state, data scope and caller.
2. Map the agreed responsibilities to named users (display names plus stable IDs), distinguishing view, prepare, edit/correct, approve/confirm, reverse/delete, banking and administration. Department labels are discussion aids, not implemented roles. Confirm self-approval/separation-of-duties rather than assuming it is mandatory or allowed policy.
3. Review the matrix with the user, preserve Admin/SuperAdmin authority, and identify the minimum additional grants or retired flags needed. Avoid bundling unrelated financial authority merely to make navigation work.
4. Align UI visibility and direct server enforcement, including list/read dependencies. Hiding a control is not authorization; approval permission never bypasses financial state gates.
5. Verify representative active Users with no grants, one grant and combined grants, Admin/SuperAdmin, inactive users and changed/revoked grants. Check direct GET/POST/AJAX access as well as visible controls; preserve named audit evidence. No full-workflow verification claim until exercised.

Initial documentation-only checkpoint, before the access implementation above: no application behavior, permissions, database records or migrations changed. Previous UI checks were targeted fixture/static checks, not a full live workflow test. No build is needed for this documentation update.

## Important correction — tugboat payables, not supplier payables

**User clarification, 18 October 2026:** AP Other Tugs will be paid through a dedicated **IBS AP Trade – Tugboats** module. Select the actual tugboat as the payable identity. Supplier-based AP Trade was a temporary workaround and must not become the required model for this flow.

- Record, allocate, review and confirm external-tug charges in **MSAP**. No IBS supplier registration/link is required to enter or confirm these charges. Keep each allocation tied to its recorded ticket, actual tugboat, COS and stable accounting source ID. Owner/reference grouping is supporting document context, not the future IBS payment identity.
- IBS will pull confirmed, unpaid allocations for the selected tugboat and handle the payable/payment/check. This dedicated IBS module, payment status synchronization and duplicate-payment prevention remain future implementation work; MSAP must not claim they already exist.
- IBS already exposes an MmsiTugboats master path and a Tugboat subaccount type. Inspect/reuse the tugboat master and accounting dimensions when that enhancement starts. Do not convert a tugboat into a Supplier, infer links from names, or assume IDs from separate public/MSAP tables are interchangeable. Multi-tug documents retain separate tugboat allocations so selecting one tugboat cannot consume another tugboat's charges.
- Remove the previously proposed supplier dropdown and supplier-link guards from this MSAP flow. Keep existing provider name/reference and financial audit snapshots. Corrective migration 20261018082734_RemoveOtherTugSupplierLinks removes only the three obsolete supplier-link columns; the earlier supplied accounting migration remains unchanged. It discards those obsolete link values, retains charges/allocations/provider snapshots, and recreates empty nullable columns if rolled back. It has not been applied by the agent.
- This clarification supersedes all earlier owner-to-IBS-supplier linkage proposals below. The overall Other Tug Charges capture/review proposal still needs MMSI feedback on responsibilities and confirmation controls.


## Accounting follow-through — 18 October 2026

These later user decisions supersede the accounting/freshness deferrals below.

- Give actual tickets an immutable, server-generated AccountingSourceId. IBS can use this together with the ticket/COS IDs, actual service dates, current charge state, rate/manual provenance and billing links. It is a source identity, **not a GL posting or a revenue-recognition assertion**. Pending approval, incomplete COS or stale charges must not be consumed as accounting-ready merely because the identity exists. No debit/credit mapping, journal, integration sync or automatic recognition is invented here.
- Persist one MSAP Sales Book snapshot per numbered bill and approved revision, atomically with final approval. The Sales Book transaction date is the Philippine final-approval date; retain the billing document date separately. Thus an April service or billing finalized in May appears in the May book. Preserve separate BASIC and BAF bill numbers and prevent duplicate revision entries. Capture the actual billed party, tax amounts, undocumented flag, approver ID/name and source billing link. Reversal voids the snapshot rather than deleting history; reapproval creates a new revision snapshot. Billings with book history cannot be deleted. Existing approved records are not silently backfilled.
- Provide **Reports → Sales Book** for active, persisted snapshots by approval-date range. Reversed snapshots remain stored for audit and are excluded from this active-book export. The operational Sales Summary remains a separate report.
- Enforce BAF coverage against the actual service week's Tuesday–Monday cycle for table-derived charges, using an approved effective-dated row within that week. Entry/approval dates do not replace the actual service date. An unchanged amount still needs a newly dated weekly row and approval. Coverage is row-specific; approval of one combination does not renew another. Historical dates use their historical cycle. Approved manual BAF terms remain exempt from the weekly table gate; tariff has no weekly expiry.
- Operations can still save factual tickets with stale/missing BAF. Charges remain Calculation Pending and billing stays blocked. After the applicable weekly row is approved, use the existing explicit comparison/recalculation action; approval never silently rewrites tickets or billed values.
- SBMA follows the schedule/COS toggle, independently of the port master. Use 87.50 × actual service minutes; no billable-hour minimum or COS discount applies. Sum actual service time independently for each COS/tugboat and allocate any rounding residual across its report rows so the total matches. Missing schedule evidence is not replaced with port eligibility.
- AP Other Tugs uses the tugboat identity and confirmed ticket allocations. The dedicated IBS AP Trade – Tugboats module owns payments/checks; no supplier-link prerequisite applies in MSAP. See the important correction above.
- Migration 20261018061929_AddMsapAccountingSourcesAndSalesBook adds the Sales Book and ticket identities. Its supplier-reference columns were superseded by the corrective tugboat-payables migration. It is supplied for the user to apply; the agent does not migrate or reset the database. Up widens provider names and adds data structures; rolling it back removes the new book/identity/link data and narrows provider names.


## Important discussion item — proposed AP Other Tugs capture and review

**Development proposal, 18 October 2026 — not a requirement established in the meeting.** The user authorized implementation for review, but the placement, responsibilities and controls below were decided in this development discussion and must be presented to MMSI Operations/Finance for their thoughts before treating them as an accepted business policy.

- Place **Other Tug Charges** in COS Details against recorded subcontractor tickets, separately from Operations' actual-ticket form. Actual service recording does not require the provider charge, and customer billing is not blocked just because the provider's invoice/cost has not arrived.
- Proposed responsibility: Finance records the provider's reference/date, supporting PDF/image, final allocated amounts, VAT-inclusion flag and remarks; an authorized reviewer confirms the saved bill. Exact personnel and whether a separate confirmation is appropriate require client feedback. New explicit preparation and confirmation permissions default off for MSAP Users; Admin/SuperAdmin can perform both; no different-person confirmation rule is imposed. Supporting documents are optional when a provider reference is supplied. These controls also need their feedback.
- A provider bill may allocate amounts across multiple tickets/tugboats/COS for the same external tugboat owner. Derive the provider from the recorded tugboat's owner, retain that identity and reference, prevent duplicate owner/reference bills and duplicate ticket allocations. Current scope is one provider-charge allocation per ticket; installments, multiple provider bills per ticket and charge reversals require a later agreed workflow.
- Save drafts for review without changing AP reporting. Confirmation checks the reviewed revision and linked approved COS, locks the provider charge, and copies confirmed allocations to existing AP Other Tugs report fields. Capture full actor names plus stable IDs and audit before/after allocations. Missing is pending; explicitly confirmed zero is valid. Pending provider costs must not be presented as confirmed zero or included in known net-sales totals.
- The VAT flag retains the current report convention only; it does not establish supplier VAT/withholding treatment or invent GL accounts. Those remain subject to Accounting confirmation. Provider references are normalized to uppercase for duplicate prevention.
- **Payment stays in IBS.** This MSAP work creates no payment voucher, supplier payable journal or GL posting, and does not claim an IBS payment status. The dedicated IBS AP Trade – Tugboats module/payment integration remains deferred; no supplier link is required. No navbar workspace/company restructuring is included. Migration `20261018013929_AddOtherTugChargeReview` supplies the charge tables, allocation uniqueness and explicit User permissions; it is supplied for the user to apply, not applied by the agent.

## Important deferred direction — tugboat workspaces and IBS integration

User decision, 18 October 2026: keep the current implementation focused on MSAP. Record the following as important future work; do not introduce the navbar selector or restructure company scope in this phase.

- A navbar workspace selector should offer MMSI / All tugboats and individual authorized tugboats, remember the selection, and consistently scope supported pages. Selecting a workspace must never reassign existing transactions or their allocations.
- MMSI intends to track performance, operating expenses and finance per tugboat. Preserve actual service-tugboat identities and source references in MSAP. Later income/expense allocations must retain tugboat attribution alongside supplier/bank identities, including documents covering several tugboats. IBS already has a Tugboat subaccount type; inspect/reuse that path when integration resumes.
- A dedicated **AP Trade – Tugboats** module is the user-selected direction for IBS payment of confirmed external-tug charges. Select the tugboat itself, independently of Supplier/Commission/Hauler payment flows. Preserve source allocation identities for reconciliation and duplicate-payment prevention. This IBS module remains deferred while MSAP is the focus.
- Treating tugboats as company-like operating workspaces is a proposed design, not a decision to create separate legal/company books now. Actual company segregation, numbering, permissions, banking and reporting require their own defined scope.
- AP Other Tugs charge capture/review and ticket allocations are implemented. Finance responsibilities and confirmation policy still need client review; the previous owner-to-supplier proposal is superseded by the tugboat-payables correction above. Do not infer tax rules or create a payable merely by selecting an external tugboat.

## Accounting and remaining-gap decisions — 18 October 2026

These user decisions supersede conflicting earlier proposals below:

- Weekly freshness applies to BAF only; tariff has no weekly renewal requirement. Approved BAF updates now show an affected-unbilled-ticket review list. Saved unbilled charges are checked against service-date rates before billing and can be recalculated after an explicit before/after comparison. The comparison fingerprint is rechecked while applying the change. Bills and approved manual terms remain protected. Historical service dates continue to determine the applicable BAF.
- Each selected numbered billing must be settled in full after withholding. One receipt/check may cover multiple fully paid bills. Partial payments and repeat receipts against partially paid bills are not part of the requested workflow.
- Management accounting needs GL capture once the approved COS, complete actual tickets and applicable charges are reliable, before final billing. Finance needs a separate Sales Book event at final billing approval. Link both events to the COS/tickets/billings and avoid duplicate revenue. Exact debit/credit account mapping and billed/unbilled reclassification must use an accountant-approved example; no mapping has yet been identified in MSAP.
- Pending COS may span months because actual tickets arrive late. Preserve actual service dates separately from entry dates and billing dates. Month-closing policy is deferred for a meeting. Following the instruction to proceed, existing MSAP month restrictions and new closing actions are suspended; stored period history is retained. Shared IBS/Filpride controls are outside this change.
- Unperformed services can now be cancelled with reason, stable actor ID and audit, without deleting their planned rows. Cancelled rows do not count as missing tickets; a row with any actual ticket or a COS with billing cannot be cancelled. Cancelling every planned service cancels the empty COS/booking. No reactivation or ticket cancellation was introduced. Migration `20261018005410_AddPlannedServiceCancellation` adds the nullable cancellation metadata and consistency constraint; it has not been applied by the agent. Finance reconciliation of principal withholding, SBMA and reports remains pending their clarification. Fuel tracking remains deferred.

## Urgent service entry — 18 October 2026

Pending schedules can open the existing COS ticket form through **Record urgent service**, using the existing Create Dispatch Ticket permission. This creates/reuses the linked COS without approving the schedule. Factual tickets retain their stable service-row links, actual dates/times and supporting files; charges remain pending and billing stays blocked. Schedule approval retains the COS/tickets and applies the normal service-date rates and approved terms. Missing rates still leave calculation pending. Existing closed-period, completeness, approval and audit controls remain in force. Bookings with tickets cannot be revised. No automatic approval or separate urgent-entry permission/deadline was introduced.

## Rate maintenance clarification — 18 October 2026

This later user clarification supersedes earlier upload-only and unresolved-formula instructions in this document.

- Replace user-facing workbook upload with an in-app spreadsheet-style editor. Keep current and historical Excel exports.
- Store tariff and BAF independently, with separate dated entries and submissions. Existing combined BAF values move to the separate BAF table; do not infer past approval.
- Enter the supplied final BAF amount. Per hour applies billable hours; Per Move, Lumpsum and Industry apply it once per service entry. Missing is pending; explicitly entered zero is valid. Retain COS percentage discounts, taxes and rounding. Unknown external fuel formulas no longer block a supplied valid amount.
- Fuel input/calculation personnel, formulas, VAT treatment and inventory remain deferred. Do not add unused fuel fields or derive customer charges from the example workbook.
- Editor drafts and submitted comparisons do not change active rates. The authorized approver activates only selected changes after checking the current baseline. Retain immutable old/new comparison and approval evidence. Weekly changes add effective dates; explicit corrections update the selected dated rate.
- Current implementation connects independent rate lookups to schedule estimates, actual ticket calculations and Job Progress. Old direct rate editing and CSV rate activation are retired. Existing ticket/billing amounts are not rewritten by rate approval; unresolved unbilled tickets retain the COS recalculation action.
- Implemented since this editor milestone: BAF-only Tuesday–Monday coverage enforcement, unbilled-ticket impact review and explicit correction/recalculation. Rate approval does not silently rewrite tickets. The impact preview is bounded to the latest 200 candidates; downstream COS/billing guards still check older work. Rate-matching fallback policy remains for confirmation.
- A schema migration is supplied but is not applied by the agent. Browser acceptance and database migration execution remain with the user.


Prepared: 16 October 2026.

Historical status at the initial foundation milestone (superseded by the 19 October checkpoint): implementation started. Phase 0 source trace is recorded; phase 1a adds the three explicit approval permissions. The calendar milestone is complete. Phase 1b adds structural expected-service rows; phase 1c adds customer-default COS discount maintenance and exports; phase 1d stores schedule commercial settings and per-service charging choices/manual values; phase 1e adds revision-bound approval evidence to schedule and billing; phase 1f adds dispatch ticket linkage to expected service entries, rate provenance flags, and raw duration separation. Schedule preparation UI, commercial/service-row saving and revision-aware schedule review have been continued from the partial implementation; see the baseline for checks and limits. Authoritative rate resolution, expected-ticket completeness and the new financial approval gates remain subsequent slices. The complete new COS/ticket/rate/billing workflow is not enabled; schedule confirmation now uses the explicit schedule-approval permission. Production data has not been changed.

Implementation tracking: see [IMPLEMENTATION_BASELINE.md](../../Meetings/IMPLEMENTATION_BASELINE.md) for the source trace, accounting boundary, sample reconciliation, and outstanding release prerequisites.

> Naming clarification (16 October 2026): replace the MSAP JobOrder implementation with CustomerOrderSlip throughout active code, views, permissions, DTOs, routes and schema. Display COS and use `/MSAP/COS/...`; do not retain a JobOrder route or add legacy-data adapters. Current database contents are disposable. Do not reset the user's database unless requested.

> Ticket creation clarification (16 October 2026): approving a schedule creates/approves the COS only. Expected service rows remain pending requirements; create the dispatch ticket when Operations saves its actual ticket number, ticket date, start/end timestamps and required details. Do not insert blank or synthetic dispatch tickets during approval. Every expected service, including subcontractors, must be recorded before service completion or billing.

## 1. Authority, outcome, and scope

### Subsequent wireframe decisions — 16 October 2026

- Latest access/navigation clarification: active MSAP Admin and SuperAdmin may approve schedules without an individual schedule-approval grant. Ordinary users still need that grant; rate and final-billing approval restrictions are unchanged. Resolve live roles and inactive/ambiguous-role denial as before. `VesselSchedule/Details` is the canonical wireframe-02 booking/review page for all viewers; only eligible authorized users see the approval form. Approved/closed records use read-only details and live Job Progress. Existing Confirm links redirect to Details, while AJAX compatibility is retained. Preserve revision/completion/cancellation/deletion actions and their guards.

- Schedule review now follows wireframe 02 as a full page at the existing Confirm route, using the shared styling and progress panel. Use COS wording on the review screen. Table rates remain explicitly labeled estimates; saved manual values are reviewed as entered. Retain schedule approval permissions, stale-review/closed-period guards and explicit overlap acknowledgement. Customer account navigation remains unavailable until an actual account screen exists; show recorded customer details for now.

- Reusable styling: portable primitives now live in `ui-system.css` through `.app-ui`, `ui-*` and `--ui-*`; `workflow-ui.css` retains Job Progress. Follow [MSAP_STYLE_GUIDE.md](../../Meetings/MSAP_STYLE_GUIDE.md) for typography and spacing. Keep shared primitives portable to IBS and isolated from billing print layouts.

- Schedule rate clarification: use `Recurring` / `Non-recurring` rather than `Standard`. Detected table rates are read-only; allow manual entry only after a successful lookup confirms no match. Keep inputs locked during incomplete, pending or failed lookups. A detected rate supersedes a previous manual entry; table estimates remain outside manual override fields.

- Deployment clarification: the current application has no actual business data and will start fresh on deployment. Do not build legacy conversion/backfill or require production-copy inventory as a release prerequisite. Verify fresh-database initialization and the new workflow instead. This does not request deletion/reset of the current database or waive financial, approval, import, or closed-period controls for new records.
- Calendar review complete: use linked breadcrumbs and bold page titles, light tabs with an active indicator, and dark action buttons. Remove the calendar status legend and separate information buttons; vessel previews remain available through hover/keyboard focus, and selecting a vessel opens its booking. In the day panel, emphasize tugboat names and put the assignment count beneath them as `2 of 2 assigned`; tug-booking links have no text underlines.
- Calendar layout refinement: follow the reviewed wireframes for subsequent UI work. The selected-day sidebar is a full-height right drawer below the existing top bar and occupies 40% of desktop screen width; keep the date/tabs above its scrolling content and the create action at the bottom. Reflow above the calendar on phones. Reuse the existing modern select component for dropdowns, including the calendar and tug filters. Use dark calendar action buttons and a larger 44px sidebar close control.
- Current implementation direction: defer rate uploading and its screens for now. Continue independent operational and layout milestones; do not invent the pending financial integration or silently waive new-workflow guards.
- Implementation clarification: a service performed Monday whose ticket arrives Tuesday must use the rate effective on Monday, even if a new BAF table was approved Tuesday. Resolve against actual service start, not ticket arrival, entry, upload, or billing date. Retain dated history; a correction to an erroneous historical row is distinct from a new period's rate.
- Implementation styling clarification: retain the compact, full-width wireframe layout, with a few restrained accent colors for primary actions, active progress, and warnings. This supersedes the earlier prohibition on all decorative color accents; status must still be understandable without color.
- Wireframe filenames and editable tab names use unique decimal numbers for related states: calendar `00.1`–`00.4`, customer/principal billing preparation `06.1`–`06.2`, deposit/clearing/return `09.1`–`09.3`, and rate upload/comparison/review/changed comparison `10.1`–`10.4`. Dashboard remains `00`; other single-screen steps retain their existing integer numbers. Renumbering changes organization only, not workflow stages.
- Finalize the screen wireframes before resuming application implementation. Use a compact vertical Job Progress panel on the left instead of an invented navigation sidebar. Use general reviewer wording in screens, including `Save for reviewing`; this display wording does not broaden approval permissions.
- Split billing is an option within one billing preparation, rather than a PHIL-CEB-only entry flow. Basic and BAF have different billing numbers, as required by the user's BIR reporting process. Each numbered component has separate amounts, balances, and collection tracking. The user's later correction supersedes the temporary proposal for a combined collection balance. Final persistence and approval granularity remain to be designed.
- Preserve the existing printed statement appearance and ticket-image printing. For split presentation, show the Basic and BAF statements separately in the same print job, with each component's own number and amounts; include actual ticket images once when requested. Do not clone actual service tickets to achieve the print separation.
- Combined BAF printing must keep different rates visible on separate lines: for example, 35,000 and 29,000 must not be merged into one rate. Preserve differing BAF methods and charging bases as well. Whether equal-rate entries may be grouped across different tugboats or service types remains open. Grouping is presentation only and must not change ticket records or charges.
- Preserve existing principal billing. The screen 06 principal variant proposes explicit `Bill to: Customer / Principal` selection and a customer-linked principal selector, while retaining the original service customer and approved COS discount/rates. Show the principal's statement address, TIN, and terms. The proposed split preparation addresses both components to that principal while preserving separate billing numbers and collection balances. Browser preview already uses principal details; the inspected dot-matrix header still reads customer details and needs alignment without redesigning its layout.
- SBMA is provisionally shown with Basic. **Reminder: confirm SBMA placement and financial treatment before implementing the affected totals.** Showing the share alongside Basic does not decide whether it is added to or deducted from the amount due.
- Current editable screens are in `MSAP_WIREFRAMES.drawio`; numbered PNGs are in `wireframe-previews/`. Screen 06 covers billing preparation and its principal variant. Screen 07 proposes review/approval of one numbered bill at a time, with the related component's readiness visible; approval granularity remains subject to user review. Its example reviews a calculable BAF bill while Basic awaits SBMA treatment confirmation. The illustrative BAF amounts use existing preview arithmetic, not a new tax policy. These artifacts describe proposed layouts and do not establish that application behavior has been implemented.
- Latest collection clarification: one check/receipt can cover multiple selected approved billings. Separate Basic/BAF collection tracking means separate allocations and remaining balances per numbered bill, not a requirement for a different receipt or check for every bill. This supersedes the restrictive single-bill screen 08 draft. Preserve existing customer/company scope and editable per-bill net allocations, with withholding attributable per bill and receipt totals reconciling to the shared cash/check payment. Unapproved or stale-approved bills remain ineligible. Screen 08 now illustrates two approved bills from two COS under one check, with a pending Basic bill unavailable for selection; the left Job Progress is scoped to the chosen COS. A zero balance after allocation does not mark a check as cleared: retain existing deposit, clearing, and returned-check controls. Sample amounts illustrate the existing settlement relationship and do not define new tax rules.
- Screen 09 proposes three receipt-banking states: deposit, clearing, and returned-check redeposit. Retain the existing receiving-company account selection, actual dates, closed-period checks, and shared receipt scope. Banking events apply once to the receipt covering the selected billings; they do not merge balances or recreate billing allocations. A returned check needs redeposit before clearing. Clearing one receipt does not finish a COS with other pending obligations. These are layout proposals for the existing banking actions, not newly implemented transitions.
- Implementation inspection follow-up: principal withholding needs reconciliation. The browser statement computes withholding from `PrintWht`, while `CollectionService.GetSettlementAmounts` conditions EWT on the customer's withholding flag and `BilledToLocal`. Do not treat the principal sample's illustrative withholding amounts as a confirmed uniform policy across print and collection. Confirm and align the intended existing treatment before implementing those calculations.
- Screen 10 proposes upload validation, staged comparison, reviewer selection/approval, and a changed-baseline state requiring refresh before approval. The uploader's `Save for reviewing` submits the comparison; it is not an extra approval stage. The reviewer-selection placement is a proposed layout. Rate maintenance uses its own progress panel because it is independent of a single COS. The MMSI/BAF example, workbook columns, business keys, effective dates, and weekly coverage are illustrative; they do not settle the open rate-format, scope, matching, freshness-boundary or historical-effective-date questions. Selection/approval must not incorrectly mark unrelated tables or scopes current. No live-rate writes occur during upload or preview, and no rejection, bulk-submission approval, no-change renewal, or continue-anyway bypass is introduced.
- Dashboard is now screen **00**, the first editable tab and `00 - Dashboard.png`; it supersedes the former screen 11 preview. The expanded draft keeps the full-width, compact layout under the existing top bar. It includes the three approval cards and queues, rate status, today's schedules and tug-assignment gaps, upcoming vessels, urgent bookings awaiting approval, missing tickets, billed and known-unbilled amounts, received amounts, outstanding gross balances, receipt banking follow-up, valid dispatch counts, distinct vessels served, and pending calculations. Preserve permission/company scope for every card and linked list. Operational/day, month-to-date, all-open and as-of scopes are labelled separately. Receipt amounts are distinguished from bank clearing, and pending calculations are excluded from known unbilled totals rather than shown as zero. Outstanding balances are not inferred by subtracting net receipts from gross billed amounts. New financial measures, recognition dates and their treatment of legacy/split records still require Finance reconciliation before implementation. A record's vertical Job Progress appears after opening that record, not as a fabricated global COS status on the dashboard. All figures are illustrative.
- Calendar clarification: replace the separate Daily Schedule screen with one calendar workspace. Port, dependent terminal, vessel/voyage/tug search, and status filters remain active across month navigation and selected-day interaction; changing port clears an incompatible terminal. Display the selected port/terminal in the calendar heading. Each vessel entry shows planned time, status and assigned/required tug count. Hover or keyboard focus previews customer, complete date/time window, location and assigned tug/service details; support tap access, dismissal and moving the pointer into the preview without losing it. Clicking a day or `+ more` opens all of that day's schedules in a panel on the same calendar, with a tug-bookings tab preserving booked/all/unbooked views and search. Opening a vessel leads to its existing booking/COS details. Retain overnight continuity and overlap warnings, including relevant conflicts outside the filtered location; filters must not invent operational availability or weaken conflict acknowledgement. Preserve existing permissions, audit and post-ticket booking-edit restrictions. Adapt old daily-view links to the calendar's selected-date context during implementation. The four calendar variants (`00.1`–`00.4`) appear after the dashboard; earlier Create/dashboard navigation labels now point to the calendar. No application routes or views have been removed yet.

[DEVELOPMENT_BRIEF.md](../../Meetings/DEVELOPMENT_BRIEF.md), particularly its subsequent-discussion baseline, is the requirements source. Later direct user clarification wins over the original meeting notes. This plan translates those decisions into work against the current repository. Entity names, internal statuses, and phase boundaries proposed here are technical recommendations, not additional business approvals.

Deliver a schedule-driven process in which RML individually approves the booking, approval creates one linked COS, Operations completes the expected tickets, confirmed service reaches the established financial posting path, all required tickets must be complete before billing, and RML separately approves the final bill before Finance collection.

The largest visible change is Vessel Schedule/COS; the work also reaches rate maintenance, dispatch, progress gates, authorization, billing, collections, reports, imports, and persistence. Keep the layered MVC/EF Core structure. Reuse the current unit of work, transactions, access model, UI components, and installed Excel package. Do not build a generic workflow engine or a parallel accounting system.

### Mandatory visual and layout baseline

The later portable-system and muted attention-color decisions in the 19 October checkpoint supersede grayscale-only and MSAP-specific primitive naming below. Retain compact layout and print isolation.

The reviewed `MSAP_WIREFRAMES.drawio` screens and numbered PNGs in `wireframe-previews/`, together with the user's later clarifications, are the visual baseline for implementation. Match their hierarchy, field grouping, information density, and action placement. Older application styling must be adapted to this baseline within the affected MSAP screens. A wireframe does not resolve business rules still marked provisional in this plan.

- **Flat and grayscale:** use white, black, and neutral-gray surfaces, text, borders, controls, and status treatments. Use thin borders, restrained corner rounding, and clear typography. Avoid decorative color accents, gradients, prominent shadows, oversized headings, and ornamental cards. Keep the primary action clearly distinguishable through contrast and weight.
- **Full available screen width:** remove centered content-width caps and large empty side margins from the affected workspaces. Use the existing full-width host layout where available, and inspect ancestor containers for inherited width limits, padding, and margins. Main content must expand with the viewport rather than stop at a fixed maximum width. Use small practical outer gutters, approximately 8–16 px; the user's latest instruction supersedes the wider gutters visible in some early wireframe images. Do not hard-code the wireframe canvas width as the application width.
- **Compact content:** use small, consistent section gaps and internal padding. Keep related fields in aligned rows and make tables use the available width. Avoid excessive whitespace, tall summary cards with little content, and unnecessarily stretched single inputs. Preserve readable text, clear labels, useful validation messages, visible keyboard focus, and usable touch targets.
- **Progress beside the work:** keep Job Progress as a narrow vertical panel on the left of record-specific screens. Let the information and inputs fill the remaining width. Do not add an application navigation sidebar. Calendar and dashboard use the full workspace without a fabricated selected-job progress panel; independent rate maintenance uses its own Rate Progress panel.
- **Responsive layout:** allow columns to reflow and the progress panel to collapse or move above the content on narrow screens. Dense tables may scroll within their own container; the entire page must not acquire horizontal overflow. Preserve hover-card information through keyboard focus and tap access.
- **Explicit states:** use text, structure, borders, and icons where helpful to distinguish current, completed, pending, blocked, and selected states. Grayscale must not obscure warnings or make status depend on color alone. Unknown amounts remain visibly pending rather than zero.
- **Scoped application styling:** reuse existing controls and components while adapting their presentation for these screens. Keep CSS changes scoped to the affected MSAP flow; avoid a global redesign of unrelated IBS modules. Preserve existing routes, permissions, validation, financial controls, and supported actions unless an agreed workflow decision explicitly changes them.
- **Preserve printed documents:** this screen-layout baseline does not redesign billing statements or their paper sizing, margins, typography, or dot-matrix layout. Keep screen-workspace styles isolated from print styles. Implement the agreed Basic/BAF statement separation, distinct billing references, matching-rate BAF grouping, and optional ticket-image printing within the existing print appearance.

For each UI slice, compare the implemented screen with the relevant wireframe and these later layout rules. Inspect representative laptop, wide-desktop, and narrow viewports: verify that the workspace uses the available width, controls remain usable, the progress panel stays compact, and print preview retains its existing layout. Report actual visual checks separately from compilation; a successful build does not verify layout. Skip `dotnet build` when changes are limited to `.cshtml`, CSS, and JavaScript, per the user's instruction, and perform relevant targeted/visual checks instead.

### Confirmed rules to preserve throughout implementation

| Topic | Required behavior |
|---|---|
| Hierarchy | One COS contains tugboats; each tug has service entries; each ticket represents exactly one service type. |
| Rate inputs | Use applicable tariff and BAF tables. Missing required entries reveal manual inputs and make the booking non-recurring. Preserve any matching table-derived charges. |
| Rate provenance | Approved manual values remain attached to their entries. Later uploads cannot silently replace them. Table-derived values use the actual service start date. |
| Discount | Customer default; COS-wide skip; one percentage on both tariff and BAF for every ticket. No per-ticket override or automatic discount carry-forward. |
| BAF | One of Lumpsum, Industry, Per hour, Per Move per service entry. |
| Hours | Hourly charges use max(actual duration, one hour), then actual duration without whole-hour rounding. SBMA always uses actual duration. |
| SBMA | Enable once on schedule/COS for all tugs, independently of port/terminal. Each tug uses combined actual hours of its services; COS sums the shares. |
| Approval | RML only, individual records, no backup/bulk/automatic approval. No Disapprove action or rejection workflow. |
| Reapproval | Booking/commercial changes require reapproval of the same COS before actual ticket entry. After ticket entry, block booking changes. Use established financial corrections/reversals after posting. |
| Urgent service | Allow operational details before approval; do not allow financial posting before RML approval. |
| Billing | Any pending required ticket blocks the entire COS from billing. Final billing approval is separate from service posting. |
| Weekly rates | At Tuesday 00:00 Asia/Manila, rates become For Updating. Required upload plus RML approval clears the applicable block. No warning bypass or no-change confirmation. |
| Upload corrections | Compare full upload with stored table, review/select changes, individually approve the submission, and apply only selected changes. Corrections may overwrite selected erroneous entries. |
| Deferred | Lumpsum/Industry formulas, fixed/flexible discounts, and actual fuel tracking have the explicit boundaries in section 10. |

## 2. Findings from the current code

These are source observations, not runtime verification or a full audit.

| Existing location | Current behavior and implementation implication |
|---|---|
| `IBS.Models/MSAP/VesselSchedule.cs` | Operational header and JSON tug-ID list, without per-tug services or commercial approval details. A stable expected-service structure is needed. |
| `IBS.Services/MSAP/VesselScheduleService.cs` | Confirmation already creates a linked `JobOrder`. Some tug additions preserve confirmation; other edits invalidate/unlink the job. Replace these paths for the new reapproval rule while preserving one COS identity. |
| `IBSWeb/Areas/MSAP/Controllers/VesselScheduleController.cs` | Confirmation uses CreateJobOrder permission. Introduce the specific RML approval permission and allow review of pending revisions even when a job is already linked. |
| `IBS.Models/MSAP/JobOrder.cs` | Already holds COS number, schedule-related header values, and tickets. Prefer extending this existing header over introducing a second COS header. Verify numbering/legacy meaning first. |
| `IBS.Services/MSAP/JobOrderService.cs` | Assignment can create a placeholder ticket using the first configured service and a synthetic number. Replace that shortcut for the new explicit expected-service rows. |
| `IBS.Services/MSAP/JobProgressCalculator.cs` | Shared progress/blocker logic exists, but checking entered tickets cannot identify an expected ticket that was never created. Compare planned requirements with completed tickets. |
| `IBS.Models/MSAP/TariffRate.cs` | Combines dated dispatch/BAF rates and separate discounts. Introduce the requested BAF table without losing historical BAF data or applying both old discounts and the new COS discount. |
| `IBS.Services/MSAP/DispatchTicketService.cs` | Already applies an hourly minimum, has tariff lookup, and exposes tariff approval/disapproval/batch flows. Actual duration must remain available separately from minimum billable hours. |
| `CheckForTariffRateAsync` and `TariffTableRepository` | Existing service-date lookup falls back from customer/terminal/service to broader matches. Verify those fallbacks represent valid tariffs before treating a missing exact entry as a recurring match. |
| `IBS.Models/MSAP/MasterFile/Customer.cs` | No customer default discount property found in the inspected model; current discount fields are in tariff/ticket data. Add the authoritative customer percentage and plan its initialization explicitly. |
| `IBS.Services/MSAP/BillingService.cs` | Billing can adjust BAF during preparation/editing; split billing also exists. New approved commercial terms must not be overridden through these paths. Existing posting advances bills to For Collection; it is not evidence of the required earlier GL trigger. |
| `IBS.Services/MSAP/UserAccessService.cs` | Admin/SuperAdmin get blanket procedure access. RML-only approvals need an explicit restriction for the new approval operations, without removing unrelated admin abilities. |
| MSAP Home and IBS User Home | MSAP has basic pending counters; IBS has permission-aware approval presentation. Adapt the house pattern for schedule/COS, rate-upload, and final-bill cards. |
| `IBSWeb/MSAP/MsapModule.cs` | Existing module registration and authorization setup; register focused services here as needed. |
| `IBS.DataAccess/IBS.DataAccess.csproj` | EF Core/PostgreSQL and EPPlus are already available. No package or framework upgrade is needed for this work. |

No separate test project was found in the repository file search. `Docs/` is locally excluded by `.git/info/exclude`; these planning files will not appear in an ordinary Git diff unless explicitly included later.

## 3. Proposed data changes

Use additive, focused migrations in `IBS.DataAccess/MSAP/Migrations`, with the matching `MsapDbContext` configuration and generated snapshot. Do not edit historical migrations.

| Record | Minimum responsibility |
|---|---|
| Customer | Default COS discount percentage, maintained through existing customer screens/services. |
| Schedule | Existing booking header; discount percentage snapshot and skip flag; SBMA enabled flag; approval identity/time and revision information; urgent-entry indicator where needed. |
| Schedule service entry (proposed new child) | Stable ID, schedule, tugboat, service type, BAF choice, tariff charging basis, rate source/reference, proposed/approved values, and calculation readiness. Group rows by tugboat in the UI; do not add a tug-group entity unless it owns real data. |
| Existing JobOrder/COS | Retain its identity and number through approval/reapproval. Link its operational ticket requirements to the stable service entries. Preserve approved commercial values so editing a schedule cannot silently change an active COS. |
| Dispatch ticket | Link to its expected service entry; retain one service type; actual ticket number/start/end; separate raw duration from billable hours; applied tariff/BAF provenance and amounts. |
| BAF rate (proposed new master) | Applicable business key, method, effective date, supplied values, upload association, and existing audit metadata conventions. Required inputs for unresolved formulas remain pending, not guessed fields. |
| Rate upload and selected rows (proposed staging records) | Uploader/time, target weekly cycle/table scope, parsed proposed values, match identity, reviewed selections, RML approval/time, and original values needed for review/audit. Staging does not become live data before approval. |
| Billing | Separate final approval evidence tied to the reviewed bill revision; retain existing billing/payment fields and correction controls. |
| User access | Focused schedule/COS, rate-upload, and final-bill approval permissions, with no general admin auto-approval bypass for these operations. |

Prefer explicit columns and normal relationships to another serialized hierarchy. A stored service-row ID, not tug count or list position, identifies an expected ticket. Distinguish planned requirements from actual tickets: an auto-created requirement must not count as a completed ticket. Do not fabricate actual ticket numbers or service times merely to satisfy current non-null fields.

Proposed constraint: one active actual ticket per expected service entry. Verify the operational cardinality before adding the database constraint; if multiple tickets can satisfy one planned service, model that explicitly instead. Preserve separate entries if the same tug performs the same service more than once.

Use existing decimal precision and financial rounding conventions after reconciling calculations. Do not make nullable pending amounts appear as zero through a display or aggregate. Scope all new records and lookups using existing company/access rules.

## 4. Workflow design and server-side gates

Keep approval, operational completeness, calculation readiness, posting, billing, and collection as distinct facts. The current linear progress stages will need updating; do not make final bill approval substitute for service confirmation/posting.

| Operation | Checks and effects |
|---|---|
| Save schedule | Validate booking and explicit tug/service entries; resolve available rates; expose only required manual inputs. Pending formula methods may be saved. Preserve manual scheduling and existing overlap acknowledgement. |
| Approve schedule | RML-only; revalidate the reviewed revision, commercial details, applicable rate readiness, and state inside the transaction. Create the COS once or update approval of its existing revision. Record actor/time. |
| Revise approved booking | Before actual tickets, move approval back to pending and retain COS identity. Block downstream use of unapproved revisions. Remove the current add-tug exception and invalidation/replacement behavior for this path. |
| Record urgent service | Save against the schedule's stable service rows while approval is pending. Approval later links/reuses those operational records; it must not duplicate tickets or allow prior posting. |
| Complete ticket | Validate number and full start/end dates/times; calculate actual duration; resolve table rates by actual start date or retain approved manual rates; apply approved COS discount. Preserve supporting documents and captain fields where required. |
| Confirm/post service | Require COS approval, confirmed actual details, applicable approved/current rates, complete calculations, and open accounting period. Use existing accounting rules and idempotent posting. Whole-job pending status remains intact if another required ticket is missing. |
| Prepare billing | Recheck every required service entry across the COS, including subcontractors and unentered tickets. Block if any remain pending or amounts are unresolved. No partial billing around the check. |
| Approve final bill | RML-only, individual bill, reviewed gross and detail; recheck completeness and bill revision. A changed bill cannot reuse stale approval. Preserve closed-period controls. |
| Collect | Finance permissions plus final billing approval. Check directly in the service, not only in selectable lists or buttons. |
| Delete/cancel | Preserve existing record protections; delete only eligible unprogressed drafts, cancel active records without destroying financial/audit links. No rejection workflow. |

Urgent entry does not grant an exception to rate-freshness or posting controls. If the table is stale, retain factual service entry and drafts, but do not use the stale prices for affected progression. The precise set of price-dependent actions is an implementation decision listed in section 11.

Use the current transaction/locking pattern. Approval must be tied to what RML actually reviewed; stale browser pages, duplicate clicks, and competing updates must not approve different content. Enforce sensitive transitions in services as well as controller authorization. Recheck gates after acquiring the relevant lock, not solely before starting the transaction.

## 5. Calculation behavior

For each service ticket, derive positive actual duration from complete start/end timestamps, including overnight services. Keep actual minutes/duration available; do not use minimum-adjusted `TotalHours` as SBMA input.

- Hourly tariff gross = applicable hourly rate × max(actual hours, 1).
- Hourly BAF gross = applicable hourly BAF rate × max(actual hours, 1).
- Per Move BAF is duration independent; reconcile the rate amount/unit with existing data before implementation.
- Apply the shared COS percentage to each tariff/BAF charge, or zero discount when skipped. Do not apply it again at billing-header level.
- SBMA per tug = 87.5 × sum(actual service minutes for that tug). COS SBMA = sum(tug shares), only when enabled.
- Lumpsum/Industry selected method plus supplied values is not a calculable result until their rules are confirmed. Represent that state explicitly.

Keep these rules in the owning MSAP business service/shared calculation method. Create/edit, preview, billing, reports, and exports should consume the same results rather than implement their own formulas. Reuse existing tax/accounting helpers; do not invent VAT, withholding, commissions, subcontractor settlement, or SBMA accounting treatment.

Trace representative rows from `MMSI PNL 202605 by Mgt.xlsx`, worksheet ` MAY 2026 SALES FINAL`. The initial inspection found headers for gross sales, net of VAT, VAT, withholding, SBMA, AP other tugs, net sales, and payment references. Sample formulas include VAT-inclusive division by 1.12 and net-sales deductions; other amounts are manual or use different treatment. These observations are supporting examples, not a universal financial specification. Capture a small reconciled example set before changing money calculations.

## 6. Rate maintenance and Tuesday control

### Upload and approval

1. Define the rate-upload template from the actual rate workbook/sample; the supplied sales-report workbook is not automatically the rate-upload format.
2. Parse with existing EPPlus support. Validate expected sheet/columns, values, dates, precision, master-file references, table scope, and duplicate keys. Give row/column error messages. Treat keys as identifiers, preserving significant leading zeroes.
3. Stage the full initial list. Later uploads compare against the applicable existing entries and show only new/changed rows with old/new values. Unchanged rows remain stored; missing rows do not imply deletion.
4. Allow selection of updates; bind the selection to a stable reviewed submission. RML approves that submission individually. Do not add a separate uploader approval stage or a Disapprove action.
5. Inside a transaction, recheck the comparison baseline and apply the selected changes only. If another update changed that baseline, refresh/review instead of blindly overwriting unseen values.
6. Permit approved corrections to overwrite selected erroneous entries. Keep existing effective-date history and before/after audit evidence; do not require users to manufacture a new version to correct a mistake.
7. Activate only on RML approval, update the approved weekly-cycle evidence, and retain uploader/approver information. A pending upload does not clear the block.

Validate uploaded files using existing size/storage conventions, keep staging protected from public access, and ensure previewed text is safely rendered. These are upload-boundary checks, not an automated judgment of whether RML chose the commercially correct rates.

### Freshness, effective dates, and notification

Compute the current cycle using Asia/Manila: Tuesday 00:00 through the next Tuesday 00:00. From Tuesday midnight, required rates show For Updating until the cycle's required update has been uploaded and approved. No no-change confirmation or override. This should be evaluated at request time in the shared rate gate, so it works after server downtime and cannot depend on a scheduled job firing exactly at midnight.

Keep weekly approval freshness separate from rate effective dates. A valid historical table remains usable for its earlier service date. Preserve manual-rate snapshots and posted records. Resolve the historical boundary around a mid-Tuesday approval before implementing live lookups; do not silently make approval time equal service-effective time.

Inspect existing notification delivery first. Reuse an in-app notification pattern for Ivy/RML's respective responsibilities, plus visible rate status on the relevant dashboards/forms. A focused scheduled reminder may be needed; its timing and channel are not authorization to send email, Viber, or other external messages. Avoid duplicate reminders on every request. Approved batch coverage must be defined so updating one selected row cannot incorrectly make unrelated stale tables current.

## 7. Implementation sequence and deliverables

These are dependency-based slices, not promised dates. Keep each slice reviewable and avoid releasing an enabled UI path before its server checks exist.

| Phase | Work and principal files | Exit condition |
|---|---|---|
| 0 — Baseline and remaining seams | Trace schedule confirmation, JobOrder/COS numbering, ticket expectations, all rate/billing entry points, imports, and actual GL integration. Reconcile sample report rows. Resolve only decisions that block the next slice. | Reviewed flow map, fresh-deployment assumptions, and accounting integration boundary; no invented financial map. |
| 1 — Data and authorization foundation | Add schedule service rows, customer discount, rate provenance/readiness, approval evidence, BAF/staging records, and necessary relationships in `IBS.Models/MSAP` and `MsapDbContext`. Extend existing repositories/unit of work, access model/screens, and registration in `MsapModule.cs`. | Focused migrations reviewed on a disposable database; fresh-database defaults checked; RML-only checks deny other users including admins for these approval actions. |
| 2 — Rate lookup, upload, and freshness | Extend `TariffRateService`, existing tariff repository/interfaces and screens; add focused BAF/upload service and MVC views; shared date-aware rate resolution and Tuesday gate. | Full-list preview/selection/individual approval/overwrite works; pending uploads cannot change live rates; Tuesday block is enforced server-side. |
| 3 — Schedule creation and COS approval | Extend `VesselScheduleViewModel`, `VesselScheduleController`, `VesselScheduleService`, `_ScheduleForm`, review/details/day/calendar views, `vessel-schedule-form.js`, and `schedule-confirmation.js`. Group services by tug; show defaults/fallback, discount skip, SBMA switch, source labels, pending formulas, and terminal calendar heading. | RML approves once to create one COS; reapproval keeps identity; operational staff cannot approve; service hierarchy persists and reloads correctly. |
| 4 — Ticket completion and calculations | Adapt `JobOrderService`, `DispatchTicketService`, their controllers/view models/views, and relevant DTOs. Bind actual tickets to expected entries, retain urgent records, apply actual-time/date rules and shared calculations. | No synthetic ticket satisfies completion; missing expected/subcontractor tickets are visible; manual rates persist; SBMA and discount examples reconcile. |
| 5 — Progress and financial integration | Update `JobProgressCalculator`, shared progress models/partials/JS, posting service path, `BillingService`, `CollectionService`, repositories, and all alternate billing/import paths. Remove new-flow reliance on Set Tariff and duplicate tariff approval; block legacy/batch bypasses. | Confirmed service posts exactly once via established accounts; billing waits for all required tickets; final approval gates collection; corrections preserve period/audit controls. |
| 6 — Dashboard and reporting | Extend MSAP Home controller/view/model using IBS approval-card conventions. Add three approval queues, pending-service follow-up, rate status, and billed/unbilled visibility in existing maritime reports/exports. | Counts and lists use matching permissions/scope; pending amounts do not appear as zero; reports distinguish service activity from billing/payment. |
| 7 — Fresh deployment and release | Rehearse initialization on an empty PostgreSQL database, verify master setup and import gates, assign only explicit approval permissions, and perform end-to-end user verification. | Fresh setup works; new workflow has no bypass; rollback and deployment instructions reviewed. No legacy conversion/backfill is required. |
| 8 — Wider IBS objective | Implement COS/invoice-reference module, then replace DCR/disbursement dependencies against actual interfaces and retention/cutover requirements. | Matching works before and after invoice reference exists; Finance workflow reconciles; DCR can be retired without a permanent legacy feed. |

Phases 1–6 are connected: do not enable the new schedule workflow without approved-rate, pending-ticket, and financial guards. Fresh deployment supersedes the earlier legacy-read/conversion prerequisite; no legacy compatibility subsystem is required. Phase 8 is part of the primary brief but is a separately scoped integration delivery; this conversation does not supply its mapping or migration procedure.

## 8. Change coverage and compatibility

Before each behavior edit, enumerate all callers, not only the visible screen. In particular:

- Update schedule create/edit/review/details, confirmation route, calendar/day projections, tours, labels, and navigation together. Current text says some tug additions retain approval; remove that promise.
- Update JobOrder direct create/edit/assign/unassign/complete/autoclose paths so they cannot bypass expected services or reapproval. Preserve a controlled urgent-entry path.
- Inspect DispatchTicket SetTariff/EditTariff/Approve/Revoke/Disapprove/BatchApprove/BatchSet and restore/delete paths. Retire or gate new-workflow bypasses while retaining historical readability. Do not delete historic status values or remap them blindly.
- Cover billing create/edit/post/reverse, customer-based ticket selection, and `CreatePhilCebSplitAsync`. Preserve supported split presentation only if it satisfies whole-COS completeness and final approval; do not silently remove company-specific accounting behavior.
- Cover collection creation/edit and all selection APIs with the same final-approval check. Hiding an unapproved bill in a list is not enforcement.
- Inspect imports and other write paths for shared gate use. Repositories retain persistence ownership; controllers retain request/authorization ownership.
- Append new permission enum values with stable explicit values after checking existing numeric assignments. Do not shift existing permissions or inherit the blanket admin approval shortcut.
- Review reporting joins and exports after introducing expected rows, so planned entries, actual tickets, and any existing split-billing clones do not double-count services, hours, or SBMA.

## 9. Migration, release, and rollback

1. Start deployment with an empty business database, as clarified by the user. Legacy inventory, conversion, and backfill are out of scope. Do not reset the current database during implementation without a concrete reset task.
2. Rehearse all migrations on an empty disposable PostgreSQL database and verify constraints/indexes, master-data setup, and numbering. Retain the existing migration history rather than rewriting historical migrations.
3. Enter customer defaults and dated tariff/BAF records through the agreed maintenance flows. Do not fabricate prior approvals or seed synthetic actual tickets as requirements. Preserve rate history created after deployment.
4. Provision RML's approval authority through the existing identity/access setup. Do not grant backups or let a general administrative role approve by default. Preserve normal administration elsewhere.
5. Establish the first approved weekly rate cycle before enabling price-dependent new work, so deployment does not create an unexplained system-wide block. Rate uploads remain deferred during current development.
6. Perform end-to-end operational and financial checks, including alternate entry/import paths. No production migration is applied by this implementation milestone.
7. Document deployment and rollback. Once actual operational records exist, do not blindly run a destructive Down migration; prefer a forward fix or coordinated restore with reconciliation. Account for code rollback compatibility with newly saved states.

## 10. Deferred work and explicit placeholders

| Item | What ships initially | What remains excluded |
|---|---|---|
| Lumpsum / Industry | Exact selectable labels and explicit Calculation pending confirmation state; store agreed supplied data and allow operational planning. | Guessed formulas, silent zero amounts, posting/finalization of affected unresolved charges. |
| Fixed / flexible discounts | Shared COS-wide percentage and skip behavior; explanatory pending-confirmation note. | Fixed allocations, separate tariff/BAF percentages, per-ticket overrides, deferred discount carry-forward logic. |
| Fuel tracking | Documentation of separate actual inventory/consumption workflow and designated owner still to be identified. | Estimated fuel calculations, mandatory Operations/Finance input, or a fuel-completion gate on COS/billing. |
| Viber/text export | Existing screenshot practice. | Automatic messaging/integration and uncommitted export features. |
| DM/CM | Preserve existing unrelated functionality. | New post-billing discount DM/CM workflow. |
| Approval conveniences | Individual RML reviews and dedicated cards. | Backup, automatic or bulk approval, rejection/resubmission stages. |

Unresolved financial methods block only affected calculations/progression. They do not prevent developing or using unrelated confirmed operational paths. Keep pending indicators visible in print/export/report views as well as forms.

## 11. Remaining decisions and inspection tasks

Do not restart the whole discussion or request already-settled formulas. Resolve these narrowly as their phase approaches:

| Detail | Required action / impact |
|---|---|
| Historical effective date around Tuesday approval | Decide whether the uploaded rate carries its own service-effective date, including services between midnight and approval. Activation and effective-date lookup are different. Blocks final rate-resolution behavior only. |
| Weekly update coverage | Define whether the weekly obligation applies to both tariff and BAF tables and at what company/table scope. Clarify whether unchanged numeric rows may be part of a required weekly upload; do not invent a requirement that every price must differ. |
| Freshness action boundaries | Enumerate price-dependent actions to block, including schedule approval and service posting. Preserve factual operational entry and historical lookups. Clarify how already-approved schedules spanning Tuesday are treated without silently creating extra reapproval. |
| Rate matching | Inspect whether customer/terminal/service fallbacks and tug-specific rates are valid. Establish required BAF matching columns from actual rate data. |
| Discount editing and rounding | Inspect established percentage limits, who maintains customer defaults, whether users can change a default beyond skipping it, and calculation precision/order. Flexible/fixed support remains deferred. |
| Expected service/ticket cardinality | Verify one actual ticket per service entry, repeated services, and cancellation of an unperformed expected service. Legacy conversion is not needed for fresh deployment. Do not allow deleting a pending requirement as a billing bypass. |
| Urgent entry and late service dates | Confirm eligibility/permission from policy; retain real service dates and closed-period controls. Approval remains mandatory before posting. |
| Financial posting integration | Trace actual journal/GL persistence, account map, tax rules, idempotency, reversals, and cross-context boundaries. The inspected billing-post method is not sufficient evidence of service-level GL posting. |
| Final bill revisions | Define which supported edits invalidate approval and how a correction after posting flows through existing reversal rules. Never leave a changed gross bill approved under a stale review. |
| Reporting definitions | Establish billed/unbilled recognition dates and treatment of pending amounts with Finance, using existing reports and the workbook. Do not rename/rebuild an unspecified AR report. |
| Rate Excel sample | Locate the actual upload workbook/columns before finalizing the template. The sales report supports reconciliation but does not establish that template. |
| IBS references and DCR | Identify reference cardinality, matching keys, disbursement interfaces, legacy retention, and cutover ownership/date before implementation. |

## 12. Verification and acceptance checklist

Follow `AGENTS.md` and effective `.editorconfig` for every code change. Run `dotnet build "Integrated Business System.sln"`; run `dotnet test` only if a test project exists at implementation time. Do not create a large test harness solely for process. Add focused assertions/tests for new financial, temporal, and state-transition logic using a minimal runnable check or an existing appropriate project, and exercise EF/authorization behavior on a disposable database. A successful build is not evidence that workflow/accounting behavior works.

| Scenario | Expected result |
|---|---|
| Two tugs, each with two different services | Four expected service entries retained under one COS; distinct BAF choices persist. |
| RML approves twice / two requests race | One COS and one approved revision; no duplicate expected entries/tickets. |
| Operations or unrelated admin calls approval URL directly | Denied for schedule, upload, and final-bill approval. |
| Pending approval | Record stays pending, no Disapprove action or automatic timeout approval. |
| Add tug/service or change discount before tickets | Same COS requires reapproval; downstream use blocked until approved. |
| Booking edit after actual ticket entry | Denied; completion of other expected tickets still allowed. |
| Urgent entry before approval | Real ticket/time saved; no posting; later approval reuses records. |
| Missing tariff, BAF, or both | Only missing inputs appear; booking non-recurring; matching values remain. |
| Customer default 10%, not skipped | Every tariff and BAF charge receives 10%; SBMA unchanged; no second billing discount. |
| Same customer, skip discount for this COS | All its tickets have no COS discount; customer default and next COS unaffected. |
| Actual 0.5 hour, hourly tariff/BAF | Charged for 1 hour; raw duration remains 0.5 hour. |
| Actual 1.5 hours | Hourly charges use 1.5, not 2. |
| Overnight start/end | Positive actual duration calculated using both dates. Invalid/reversed times rejected. |
| One tug: 0.5-hour Docking + 1-hour Undocking, SBMA enabled | Tug share 7,875. A second tug gets its own duration/share; COS sums both. |
| SBMA disabled | No SBMA charge, regardless of port or terminal. |
| Lumpsum/Industry unresolved | Visible pending result, not zero/Per Move; affected posting/finalization blocked. |
| Table changed before delayed billing | Actual service start date selects historical rates; approved manual rates remain unchanged. |
| Monday service; ticket received/entered Tuesday after a new BAF rate takes effect | Monday's applicable rate is selected from retained history; Tuesday's upload/entry date does not reprice Monday's service. |
| Full upload has wrong columns, duplicate keys, or invalid values | Actionable errors; no active table writes. |
| Full initial upload then weekly changed file | Preview shows new/changed rows and old/new values, with selectable updates. |
| Only some rows selected | Only selected changes apply on RML approval; omitted rows remain. |
| Correct existing erroneous rate | Selected entry overwritten with audit evidence; existing posted amounts unchanged. |
| Stored rate changes after preview | Approval detects stale baseline and requires renewed review. |
| Monday 23:59:59 to Tuesday 00:00 Philippine time | Applicable rate status becomes For Updating at the boundary, even on a server using another timezone. |
| Uploaded but not approved Tuesday | Price-dependent progression remains blocked; no continue-anyway. |
| RML approves weekly upload | Applicable cycle becomes current; approved selection active at approval time. |
| Server offline across Tuesday midnight | First relevant request still enforces block; reminder delivery does not control correctness. |
| Historical pre-Tuesday service | Valid historical rate is not rejected simply because current-week rates need updating. |
| Subcontractor expected ticket missing | Entire job pending; no billing through create, edit, customer selection, or split route. |
| All required tickets complete | Billing can proceed only with approved COS and calculable charges; final bill approval still required. |
| Confirmed service posting retried | Established journal/accounting effect occurs once; no double posting at final billing approval. |
| Bill awaits RML / bill modified after approval | Cannot collect against missing or stale final approval. |
| Dashboard cards | Three permission-scoped queues; counts/list contents agree; reapprovals visible. |
| Legacy/split/import records | No duplicated service/hour/SBMA totals, fabricated approvals, or bypassed gates. |
| Reports and Excel reconciliation | Billed/unbilled/pending distinctions and representative financial rows reconcile without treating unresolved amounts as zero. |
| Fuel fields absent | Current scheduling, ticket completion, posting, and billing proceed without a fuel workflow prerequisite. |

Before each slice is reported complete: review the full diff, remove unrelated edits, inspect migration artifacts where applicable, check formatting/encoding/LF/final newlines, and report actual build/check outcomes and any unverified behavior.

## 13. Planning review and next implementation slice

This plan reviewed the updated brief and the principal schedule, job-order, dispatch, tariff, progress, billing, access, dashboard, context, and module-registration code. The brief's older backup-approval, auto-approval, BAF-selection, and post-ticket-edit wording was aligned with the latest decisions. No application build, database migration, runtime workflow test, or full workbook reconciliation was performed for this documentation task.

Next: solidify user access using the 19 October action inventory and review sequence above. The original phase 1 foundation is implemented; remaining accounting/integration, Finance reconciliation and workflow-acceptance work stays tracked in the current gap table.


### Master reference cleanup — 19 October 2026

- Schedule SbmaEnabled is the only SBMA applicability control. Remove unused port HasSBMA from forms, model, imports and exported headers.
- Remove inherited IBS customer fields with no current MSAP workflow consumer: commissionee/rate, retail station, credit/available credit, retention, multiple terms and price adjustment. Remove their controls, export columns, repository mapping and unused commissionee lookup.
- Retain customer type/code generation, documented/undocumented status, tax/discount settings and source record IDs used for import identity checks. IBS's models/tables are not changed.
- Two focused migrations drop retired MSAP columns and the customer commissionee relationship/index. Applying them discards those old values; Down restores defaults only. Review and apply to the test database separately.
