# MSAP implementation baseline

Source inspection: 16 October 2026. No production database was queried or changed. This is the phase 0 source trace; production inventory, Finance sign-off, and migration rehearsal remain release prerequisites.

## Existing flow and required coverage

| Boundary | Current implementation | Implementation consequence |
|---|---|---|
| Schedule confirmation | `VesselScheduleController.Confirm` requires CreateJobOrder. `VesselScheduleService.ConfirmAsync` locks the schedule and compares the reviewed timestamp, then calls `JobOrderService.CreateJobOrderAsync`. A linked job short-circuits confirmation. | Dedicated approval permission, service-level check, and revision-aware reapproval must replace this shortcut together. |
| Numbering | `JobOrderRepository.GenerateJobOrderNumber` takes transaction advisory lock 20771001 and issues `JO-{year}-{sequence}`. `COSNumber` is a separate editable field; `BuildJobOrder` does not assign it. | Preserve existing identifiers. Do not silently treat the generated JO number as the COS number or invent a COS sequence. |
| Schedule revisions | `VesselScheduleService.UpdateAsync` preserves confirmation for added tugs; other eligible edits invalidate/unlink the old job. Existing tickets or billing block edits. | Replace both revision paths together, retaining one linked COS and requiring reapproval before downstream use. |
| Tug assignment | `JobOrderService.AssignTugboatAsync` sets PreferredTugboatId for the first assignment; additional assignments create `P{jobId:X}T{tugId:X}` tickets using the first master service. | Synthetic assignment records cannot establish actual service or expected-service completeness. Preserve legacy records until individually classified. |
| Ticket lifecycle | `DispatchTicketService` owns create/edit, tariff set/edit, individual approval/revocation/disapproval, batch set/approval, delete/restore, and image/video handling. `SuperAdminService` also edits operational records. | All write paths need shared new-workflow gates, including restore and privileged edits. Historical statuses remain readable. |
| Rate lookup | `CheckForTariffRateAsync` filters AsOfDate <= DateLeft and tries customer/terminal/service, then customer/terminal, then customer. `TariffTableRepository.GetAsync` selects descending AsOfDate. | Monday service entered Tuesday already selects Monday's applicable dated row. Fallback validity remains unconfirmed; no tug-specific key exists in the current tariff entity. |
| Rate maintenance | `TariffRateService.UpsertAsync` inserts a new dated key, overwrites the same dated key, or explicitly edits an existing row, including its effective date. Delete removes it. | Historical resolution depends on retaining old rows. New-period uploads must not overwrite the previous period; approved corrections must preserve before/after evidence. |
| Billing | `BillingService` create/edit accepts BAF overrides. Customer and job ticket-selection endpoints coexist. `CreatePhilCebSplitAsync` removes BAF from original tickets and creates BAF-only ticket clones with an `-A` suffix. | Whole-COS gates must cover every selection/create/edit path. New Basic/BAF separation must avoid cloned operational tickets and preserve separate numbered balances. |
| Posting | `BillingService.PostBillingAsync` changes tickets to Billed and billing to For Collection. It constructs a SalesBook object but never adds it to a repository/context. | This is not an established service-level ledger posting implementation. Do not move this method earlier and claim GL posting works. |
| Accounting boundary | `MsapDbContext` maps no SalesBook, journal, or GL entity. The MSAP unit of work owns only its own context; its shared Terms repository does not establish a cross-context posting transaction. | Finance must identify the destination journal/account map, taxes, reversal path, and idempotency key before implementing service posting. |
| Collection | `CollectionService.GetSettlementAllocationsAsync` locks bills, validates shared receipt totals and per-bill allocations, and calls the progress gate. Banking applies to the receipt. | Final approval must be enforced here and in selection APIs. Retain one receipt across multiple bills, separate bill balances, and deposit/clearing/return controls. |
| Withholding | Collection settlement conditions withholding on customer flags, Local billing, and VAT. Browser print uses PrintWht; principal print/collection treatment is not uniformly established. | Preserve existing calculations until the intended treatment is reconciled. |
| Progress | `JobProgressCalculator` aggregates entered tickets and linked billing/receipts. `JobOrderRepository.SearchBillableJobOrdersAsync` also checks ticket statuses. | Neither can prove that an unentered expected service is complete. Expected rows must be authoritative across both paths. |
| Imports | `MsapImportController` writes tariff, dispatch, billing, collection, and allocation records directly through MsapDbContext. | New-flow activation requires import/legacy classification and equivalent gates, not merely hidden screen actions. |
| Authorization | `ProcedureEnum` currently occupies 0 through 21. `UserAccessService.CheckAccess` grants Admin/SuperAdmin all procedures before inspecting stored flags. Access maintenance allows MSAP Users. | Append stable explicit values; new approval operations must check explicit assignments before the blanket administrative shortcut. Do not seed anyone automatically. |

## Workbook arithmetic checks

Read-only inspection used the workbook's OOXML formulas and cached values, not Excel recalculation. Source: `MMSI PNL 202605 by Mgt.xlsx`, worksheet ` MAY 2026 SALES FINAL`. Rows 7–14 were checked for gross = net of VAT + VAT and net sales = balance minus net AP other tugs, within 0.01.

| Example | Source values and result | Limit |
|---|---|---|
| Row 7, owned tug | Q7 61,886 / 1.12 = R7 55,255.357142857; S7 6,630.642857143; AI7 55,255.357142857. | VAT-inclusive example, not a universal rule. |
| Row 11, subcontractor | Q11 = R11 541,601; VAT 0. AG11 = 325,000 + 23,797.5 = 348,797.5; AH11 = AG11 / 1.12 = 311,426.339285714; AI11 = 230,174.660714286. | Customer revenue and subcontractor VAT treatment differ. |
| Row 13, subcontractor | Q13 = R13 50,000; AH13 70,312.5; AI13 -20,312.5. | Negative net sales are present; do not clamp them to zero. |

These examples reconcile workbook arithmetic only. They do not approve SBMA settlement placement, tax policy, recognition dates, or journal accounts. The workbook is a sales/AR report, not a rate-upload template.

## Legacy migration cases

Inventory on a database copy must distinguish tentative/unlinked schedules; confirmed linked jobs without tickets; jobs with synthetic assignment tickets; jobs with actual tickets; standalone imported tickets/bills; split originals/clones; posted/partially collected/fully collected bills; and deposited, returned, and cleared receipts. Preserve IDs, amounts, audit history, closed periods, and historical rates in every category.

Do not infer an expected service from a synthetic ticket's first-master ServiceId, infer an actual service from its number pattern alone, fabricate an approval timestamp, or backfill customer discounts from conflicting tariff/BAF percentages. Source inspection cannot establish production counts or prove that a legacy job has no missing tickets.

## Next independent foundation and blockers

The three explicit approval permissions can be added without enabling the new schedule/rate/billing workflow. Leave all assignments false until the designated RML account is provisioned through access maintenance. Existing procedures retain their behavior.

Before implementing dependent milestones, resolve:

- Actual tariff/BAF upload columns, matching keys, company/table scope, and weekly coverage. Actual service date governs historical lookup, as clarified; this does not settle upload scope or mid-Tuesday effective-date policy.
- Existing destination GL interface/account map and posting/reversal transaction boundary.
- COS numbering, cancellation of an unperformed service, urgent-entry permission, and legacy conversion evidence.
- SBMA settlement treatment, principal withholding, and numbered split-bill approval/revision semantics.
- Disposable PostgreSQL checks and an actual production-copy inventory before cutover. No production migration or approval assignment is authorized by this baseline.
