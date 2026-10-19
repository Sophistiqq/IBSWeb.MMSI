# MSAP blockers and next work

Updated 19 October 2026 after application commit 3257f348b. This is the active gap list. Delivered milestones and verification limits are in [CURRENT_STATUS.md](CURRENT_STATUS.md); the archived plan is background only.

## Immediate implementation and acceptance

| ID | Priority | Gap | Completion / next action |
|---|---|---|---|
| G01 | High | Customer numbering mismatch | Replace MSAP's inherited type-prefixed generation with an agreed MSAP sequence; implementation remains open. Source inspection found 92 numeric customer codes, maximum 0093, and no RET/IND/RES/GOV codes. Preserve existing codes/source IDs used by master/rate imports. Verify uniqueness and concurrent creation. Test case MF-20 is open. |
| G02 | Awaiting external corrections | Master-data correction handoff | User reports using placeholders to proceed and handing off exported Excel workbooks with notes. This no longer blocks current implementation/testing. Await returned corrections; placeholders are not confirmed business policy. Revalidate returned XLSX before applying changes. See the [source review](MASTER_FILE_IMPORT_REVIEW.md). |
| G03 | User testing | Full workflow acceptance | User will start the [test plan](WORKFLOW_TEST_PLAN.md). Record results for concurrency, denied actions, stale approvals, financial reconciliation, split/principal billing, multi-bill checks and returns. Execution results are not yet supplied; do not mark acceptance complete. |
| G04 | Partly resolved | Broader automated rendering check | Original schedule/reapproval failure resolved in the check fixture; --schedule-render-only passes. The broad command currently stops earlier at Program.cs's stale 32-controller inventory assertion, “An imported controller is missing.” Investigate that inventory separately; the full suite is not green. |

## Business decisions and integration

| ID | Gap | Current boundary / required decision |
|---|---|---|
| G07 | Finance reconciliation | Confirm principal withholding across statements/collections, applicable rate/fallback examples, provider VAT/withholding, the fixed VAT-inclusive billing assumption and SBMA settlement with agreed examples. Sample workbook amounts do not establish universal policy. |
| G08 | Staff access / approval separation | Granular controls are implemented. Confirm actual named staff assignments and perform live multi-user acceptance. A different-person/self-approval prohibition is not currently an agreed/enforced rule. |
| G09 | GL / IBS recognition | AccountingSourceId and Sales Book snapshots exist. Journals, account mapping, early management recognition/reclassification and IBS synchronization require separate implementation. |
| G10 | IBS AP Trade – Tugboats | Implement confirmed/unpaid tugboat allocation retrieval, identity matching, payment/check handling, status synchronization and duplicate-payment prevention in IBS. No Supplier prerequisite or MSAP provider payment flow. |
| G11 | Other Tug corrections | Confirmed charges are immutable. Agree amendment/reversal and multiple provider documents per ticket before extending the current allocation workflow. Capture/review responsibilities still need MMSI feedback. |
| G12 | Paired split-billing corrections | No paired editor. Eligible unposted preparation can be discarded/reprepared; bills with Sales Book history cannot be deleted. Design paired correction if required. |
| G13 | Remaining master-model decisions | Customer contacts, legacy service classification, vessel-type-specific tariff scope, TIN normalization and legacy metadata retention need confirmation. Detailed evidence/questions belong in the [master-file review](MASTER_FILE_IMPORT_REVIEW.md). |

## Deliberately deferred

- G05: Refresh user manuals when the user resumes that work; Job Order/import/access documentation remains outdated.
- Month closing/reopening and late-arriving-ticket policy.
- Fuel prices, consumption, inventory, calculation formulas and responsible personnel. Current final-value BAF remains usable.
- Per-tugboat workspaces, navbar switching, performance/opex accounting and company segregation.
- Partial payments unless the user requests them; current full-settlement rules remain intentional.

## Update discipline

When a gap is resolved, record its evidence in CURRENT_STATUS.md and remove or clearly mark the corresponding open item here. Keep test case IDs stable and record policy decisions in the owning reference. Do not turn historical proposals or deferred work into current requirements.
