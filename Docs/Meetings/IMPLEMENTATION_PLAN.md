# MSAP schedule/COS implementation plan

Prepared: 16 October 2026.

Status: implementation started. Phase 0 source trace is recorded; phase 1a adds the three explicit approval permissions. The calendar milestone is complete. Phase 1b adds structural expected-service rows; phase 1c adds customer-default COS discount maintenance and exports; phase 1d stores schedule commercial settings and per-service charging choices/manual values. Ticket linkage, approval evidence/gates, and rate resolution remain subsequent slices. The new schedule/rate/billing workflow is not enabled. Production data has not been changed.

Implementation tracking: see [IMPLEMENTATION_BASELINE.md](IMPLEMENTATION_BASELINE.md) for the source trace, accounting boundary, sample reconciliation, and outstanding release prerequisites.

## 1. Authority, outcome, and scope

### Subsequent wireframe decisions — 16 October 2026

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

[DEVELOPMENT_BRIEF.md](DEVELOPMENT_BRIEF.md), particularly its subsequent-discussion baseline, is the requirements source. Later direct user clarification wins over the original meeting notes. This plan translates those decisions into work against the current repository. Entity names, internal statuses, and phase boundaries proposed here are technical recommendations, not additional business approvals.

Deliver a schedule-driven process in which RML individually approves the booking, approval creates one linked COS, Operations completes the expected tickets, confirmed service reaches the established financial posting path, all required tickets must be complete before billing, and RML separately approves the final bill before Finance collection.

The largest visible change is Vessel Schedule/COS; the work also reaches rate maintenance, dispatch, progress gates, authorization, billing, collections, reports, imports, and persistence. Keep the layered MVC/EF Core structure. Reuse the current unit of work, transactions, access model, UI components, and installed Excel package. Do not build a generic workflow engine or a parallel accounting system.

### Mandatory visual and layout baseline

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

Continue phase 1's expected-service and approval foundation, resolving narrow GL/rate-key questions as their dependent slices approach. Fresh deployment removes legacy conversion from this sequence. This is the minimum stable base for the redesigned schedule form; adding UI inputs alone would leave missing-ticket detection and approval controls incomplete.
