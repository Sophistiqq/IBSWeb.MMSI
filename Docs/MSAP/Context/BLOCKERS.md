# MSAP blockers and next work

Updated 27 October 2026 during local audit remediation and authorized cleanup. This is the active gap list. Delivered milestones and verification limits are in [CURRENT_STATUS.md](CURRENT_STATUS.md); the archived plan is background only.

## Immediate implementation and acceptance

| ID | Priority | Gap | Completion / next action |
|---|---|---|---|
| G01 | Implemented locally; review pending | Customer numbering | User confirmed new customers use type-prefixed codes; existing numeric/imported identifiers remain unchanged. IBS-47 adds transaction-scoped allocation and a unique code index. The disposable PostgreSQL check passed concurrent creates, all three prefixes, numeric-code preservation and duplicate rejection; the configured source had no duplicate codes. MF-20 still needs owner acceptance. |
| G02 | Awaiting external corrections | Master-data correction handoff | User reports using placeholders to proceed and handing off exported Excel workbooks with notes. This no longer blocks current implementation/testing. Await returned corrections; placeholders are not confirmed business policy. Revalidate returned XLSX before applying changes. See the [source review](MASTER_FILE_IMPORT_REVIEW.md). |
| G03 | User testing | Full workflow acceptance | User will start the [test plan](WORKFLOW_TEST_PLAN.md). Record results for concurrency, denied actions, stale approvals, financial reconciliation, split/principal billing, multi-bill checks and returns. Execution results are not yet supplied; do not mark acceptance complete. |
| G04 | Resolved locally | Broader automated rendering check | IBS-46 replaces the stale controller-count assertion with the explicit active controller inventory. The default runner passes. IBS-56 uses a fresh billing request after discard; the corrected original billing diagnostic and dependent provider-progress diagnostic pass on a designated clone. |
| IBS-59 | Medium | Scheduling diagnostic zero-rate mismatch | The full scheduling database runner expects ManualTariffRate=0 to save, while the unchanged schedule validator requires a positive tariff. Confirm the intended contract and repair the appropriate fixture/expectations or validation; this is separate from cleanup and does not invalidate the passing default runner. |

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
| IBS-57 | Provisional Other Tugs payable account | User authorized registration in ibs_dev: 201020500, AP - Trade - Other Tugs, Level 4, Credit, under Accounts Payable 201010000. Created through normal chart administration. Verify the proposed mapping and hierarchy at the next meeting; registration does not establish final accounting acceptance. |

## Deliberately deferred

- G05: Refresh user manuals when the user resumes that work; Job Order/import/access documentation remains outdated.
- Month closing/reopening and late-arriving-ticket policy.
- Fuel prices, consumption, inventory, calculation formulas and responsible personnel. Current final-value BAF remains usable.
- Per-tugboat workspaces, navbar switching, performance/opex accounting and company segregation.
- Partial payments unless the user requests them; current full-settlement rules remain intentional.

## Update discipline

When a gap is resolved, record its evidence in CURRENT_STATUS.md and remove or clearly mark the corresponding open item here. Keep test case IDs stable and record policy decisions in the owning reference. Do not turn historical proposals or deferred work into current requirements.
