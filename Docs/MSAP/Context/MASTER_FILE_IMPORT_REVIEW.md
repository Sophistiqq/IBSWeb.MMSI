# Master-file import review and meeting confirmations

19 October 2026. Sources inspected read-only: the ten master CSVs in `Docs/MSAP/CSVs for Imports/` and `Docs/Meetings/MMSI PNL 202605 by Mgt.xlsx`, worksheet ` MAY 2026 SALES FINAL`. No source files were edited and no import was executed.

## Current implementation

- Admin/SuperAdmin: **Master File References → Master File Import**, route `/MSAP/MsapImport`.
- Imports only bank accounts, customers, ports, services, tugboat owners, captains, vessels, terminals, tugboats and principals. Collection, collection-bill links, billing, dispatch, users/access, historical audits and reset are not supported. Later direction enables tariff.csv as migration input: its tariff and final BAF amounts are staged into separate review drafts, retaining the existing rate approval workflow.
- Select individual CSVs, a folder, or a completed master workbook (.xlsx). Exact names/aliases are used, not substring guesses. Unsupported files are listed as ignored. No server-local folder is read automatically.
- Validate first to load a paged spreadsheet-style editor grouped by file. Correct fields in place, use Needs attention / Edited filters and search, then validate again. Edited cells are marked; row messages sit beside the inputs. Additions, updates, unchanged/skipped records and validation status are retained. No writes during preview. Import rechecks files/options and database baseline before saving the entire batch in one transaction.
- Default is add-only. Explicit update mode changes only mapped source fields and retains IDs. Do not match by name: principals repeat between customers and terminal numbers repeat between ports. Duplicate/ambiguous business keys and conflicting source RECIDs are rejected.
- Principals use customer code plus principal number; terminals use port code plus terminal number. Source RECID remains a traceable external ID and is not assigned as the new database primary key.
- Customer VAT type, WHT (2307), WVAT (2306), default COS discount, ZIP, terminal IOC have explicit editor/workbook columns. New records require withholding/discount/IOC confirmations; False and zero must be deliberate. Blank added fields preserve matched existing settings. Owner fixed rates and existing tugboat home ports remain preserved unless their supported source fields are supplied. Withholding is never inferred from VAT.
- Actual financial records, posted snapshots and recorded ticket amounts are not rewritten/recalculated. Tariff migration creates Tariff and BAF drafts atomically with master changes, never activates live rates. Successful import links to both rate reviews; submit/approve through the ordinary rate workflow. Source master changes can affect future work, so review the comparison before enabling updates.
- Numeric payment terms are preserved: 0 → COD, 3 → 3D, 7 → 7D, etc. Matching shared IBS payment-term definitions must exist with the stated day count and no extra months. No unused local term or silent COD fallback is created.
- No fake TIN or ZIP is generated. Available legacy text is retained; incomplete address/TIN and format differences are flagged. The current Customer form may require correction before such a record can be edited normally. Existing non-source settings remain unchanged.
- Import audits retain the acting user's full name and stable user ID, row/code and changes. Legacy CREATEDBY values are external user codes and are not attributed to current IBS users by guessed identity.
- Download Excel headers creates a blank workbook; Export cleaned Excel exports successfully validated corrections. **Export issues for completion** is available after validation even when it fails: all supplied rows are retained, missing/invalid cells get highlights and comments, and row notes, Issues and File status sheets support the handoff. File status distinguishes unselected files from missing stored masters. Return the completed .xlsx directly for fresh validation; report sheets/notes are ignored, formulas rejected and codes/TINs retained as text. Nothing is imported by exporting or uploading alone. Original DBF-converted CSVs remain unchanged.
- Unused IBS customer fields are removed from the MSAP model, forms, projection, repository and export: commissionee/rate, retail station, credit limit/available credit, retention, multiple terms and price adjustment. The unused commissionee lookup/relationship is removed. Customer exports retain tax flags and COS discounts. These fields had no consumer in MSAP's current operational/financial calculations; IBS's own models and tables remain unchanged.
- Focused migrations remove the unused port HasSBMA column and obsolete customer columns/commissionee foreign key and index. Schedule SbmaEnabled remains authoritative. Applying these migrations discards the retired column values; rollback restores column defaults, not the old values. No migration is applied by the agent. Customer classification and documented/undocumented status remain because current code consumes them; legacy source IDs remain for import identity checks. The source-review items below determine later model changes; the original source CSVs remain the reference.

## Source inventory

| File | Rows | Business identity / relationship |
|---|---:|---|
| bankacct.csv | 1 | CODE within MMSI; ACCOUNTNO stays text |
| customer.csv | 92 | Four-character NUMBER within MMSI |
| port.csv | 12 | Three-character NUMBER |
| services.csv | 20 | Three-character NUMBER; DESC is service name |
| tugboatowner.csv | 26 | Three-character NUMBER |
| tugmaster.csv | 30 | EMPNO; preserve leading zeros, do not substitute RECID |
| vessel.csv | 912 | Four-character NUMBER; explicit L/F vessel type |
| terminal.csv | 74 | Composite NUMBER such as 001-002 |
| tugboat.csv | 154 | Three-character NUMBER; OWNER refers to owner NUMBER |
| principal.csv | 98 | AGENT/customer NUMBER plus principal NUMBER |
| **Master total** | **1,419** | No duplicate business keys/source RECIDs found within the inspected scopes |

Additional migration input: tariff.csv contains **664 dated rows**, bringing the supported source total to **2,083**. Customer/port/terminal/service codes all resolve, and no duplicate current rate keys were found. The file includes both DISPATCH and BAF: preserve their amounts in separate Tariff/BAF review drafts. Legacy TYPE is F for 451 rows, L for 209, blank for 4; VESSEL is blank in all rows. TYPE remains visible source provenance in rate review, not an invented lookup dimension. Future vessel-specific rows are rejected rather than silently broadened.

All principal agents, terminal port prefixes and non-placeholder tugboat owner codes resolve within the supplied masters. Repeated names are not proof of duplicate entities: six principal names, four vessel names and three terminal names repeat. Review individual records before any merge; import never merges them by name.

The source contains three blank customer VAT flags, ten blank tugboat ownership flags and three blank customer payment terms. All three customer rows also lack addresses. Blank booleans are unknown, not False. Validation blocks the selected batch until required unknowns are corrected; unaffected files can be selected/imported separately. Missing address/TIN values generate warnings rather than fabricated placeholders.

## Confirm at the next meeting

The user explicitly deferred these confirmations to a later meeting. Do not infer approval from the workbook or force a guess merely to complete a batch.

19 October follow-up: the user reports supplying placeholders to proceed and handing off exported Excel workbooks with notes. Await returned corrections; do not treat placeholder values as confirmed tax, ownership or other business policy. This handoff no longer blocks the user's current workflow testing.

| Item | Evidence / question | Current handling |
|---|---|---|
| Legacy tariff scope | Current rate keys use customer/terminal/service/effective date, while the DBF export also has legacy vessel TYPE. No current-key collisions occur in the supplied 664 rows. | Retain source ID/type in staged review and require reviewers to assess the scope before ordinary approval. Confirm whether later rate modeling needs a vessel-type dimension. |
| Non-vatable classification | CSV False does not distinguish VAT-exempt from zero-rated. The old importer always selected zero-rated. Which classification is correct, and can it differ by customer? | No default classification selected. Supply VAT type per customer; the batch mapping is an explicit fallback only. No inference from individual sales VAT amounts. |
| Withholding | Old import incorrectly assigned WithHoldingVat from VATABLE. VAT status alone does not establish EWT/WVAT obligation. | Accounting supplies explicit WHT/WVAT in the editor or correction workbook. New customers cannot silently receive withholding defaults; existing settings are preserved when added fields are blank. |
| Incomplete customers | Codes 0015 FLYING VESSEL, 0021 NORTEAM and 0022 HARBOR EAGLE have blank VATABLE/TERMS/address. They do not appear as exact customer-name matches in the inspected Sales Report detail rows. | Blank required classification/terms rejected; address/TIN incomplete warnings. Ask whether these are active real customers, placeholders to deactivate, or missing master data. |
| Missing ownership | Ten tugboats have blank COMPANYOWN: 005 DINAGAT, 006 ENERGY GALAXY, 007 ENERGY PACIFIC, 008 GALINA2, 009 ILONGO, 010 ISLANDER, 012 LIMAY, 014 PACIFIC ROSE 7, 015 PINDASAN, 020 WEZEN. | Source correction required. Do not convert every blank to subcontracted or company-owned. |
| Partial ownership evidence | Sales Report rows 132–134 list LIMAY as SUBCON; rows 13–15 list PACIFIC ROSE 7 as SUBCON. Other eight blank-flag tugs were not found as exact-name matches in the detail rows. | Evidence for discussion, not an automatic CSV update. Confirm current classification/owner. |
| Tugboat home port | CSV has no port column; old import hardcoded database PortId=1. Report transaction ports are not a home-port master. MT ANDRICK appears in Cagayan de Oro/Leyte, SUN JIN NO.505 in Cebu/Surigao and HOKUTO in Leyte/Surigao. | Existing home ports preserved. New tugboats require an explicit selected fallback port or optional PORT business-code column. Confirm home-port assignments; do not use last/most frequent transaction port. |
| Customer contact model | Customer CSV includes LANDLINE1/2 and MOBILE1/2; current Customer model has no corresponding properties. Principal has those fields. | Customer contact columns are not imported in this pass. Decide whether to add contact properties/related contact records and how the UI should use them. Source files are retained. |
| Service classification | services.csv TYPE includes 1–9 and repeated 10; current Service model retains only NUMBER/name. | Do not map TYPE to tariff basis, BAF method or a guessed enum. Confirm whether this category remains useful before adding it. |
| Source dates and users | CREATEDBY/UPDATEDBY are legacy employee/user codes; source metadata is incomplete on several files. | Current named import actor is audited. No import of userfile/useraccess or guessed current-user linkage; decide whether legacy metadata needs separate provenance fields. |
| Identity/required fields | Legacy TIN lengths/formats vary; current Customer validation expects 3-3-3-5 digits. ZIP is absent. | No zero-filled replacement TIN/ZIP. Agree normalized TIN and incomplete-master policy before schema/form validation changes. |
| IOC defaults | CSV terminals do not supply IOC. Workbook includes per-transaction IOC values. | New terminals require explicit IOC confirmation; existing values are preserved when omitted. SBMA is controlled only by the schedule checkbox; the unused port flag is removed. |

## Implementation follow-up

Current numbering, manual-refresh, deployment and acceptance follow-ups are maintained in [BLOCKERS.md](BLOCKERS.md). This file owns the source inventory and detailed master-data confirmations above.

## Validation and remaining checks

The database-free master-import check reads all eleven supported source files, checks allowlisted names/headers/row structure, correction boundaries, missing-flag handling, code/terminal/term parsing, Excel text/header preservation, failed-validation handoff comments/notes, returned-workbook round trips, formula rejection, EF table metadata, retired customer-field absence and migration/model alignment. The workflow-access check covers the Admin-only import boundary and absence of Reset. A passing parser check does not mean the dirty source rows are ready for import.

Before live use, validate a test batch, review exact row changes, import into the user's test database, reimport add-only to confirm no duplicates, then exercise explicit update mode and a stale-preview rejection. Verify named audit entries, no financial/rate mutations, source-code relationships, source-added port selection and rollback on one invalid row. The agent has not run live database imports or browser acceptance.
