# MSAP review checkpoint - 27 October 2026

Reviewed the 16 IBS issues currently In Review against `4d5b71054` on `chore/audits-and-reviews`. The attachment fixes were committed as `4d5b71054`; the earlier remediation, cleanup and scheduling fixes are `1126bcbf1`, `4af1917c0` and `6fd488249`. These commits are local, unpushed and undeployed.

## Findings requiring fixes

### IBS-52 - High: dispatch custom renderers still return stored HTML

`IBSWeb/Areas/MSAP/Views/DispatchTicket/Index.cshtml:142` reads persisted port/terminal names and interpolates them without escaping. The customer renderer at line 150 likewise returns the persisted customer name directly. These `columns.render` functions override the shared default text renderer added by IBS-52, so escaping the master registries does not protect the dispatch registry.

An isolated Node VM captured the actual view's DataTable configuration, substituting only inert Razor route/period values and browser dependencies. Both display renderers returned the literal `<img src=x onerror=this.alt=731>` probe unchanged. The dispatch list repository includes Customer and Terminal.Port; its authenticated JSON endpoint returns those persisted names. This is a reachable stored HTML/script boundary for another authorized operations viewer. No payload was stored and no browser exploitation was performed in this review.

Smallest correction: escape the location components and assigned customer name through the existing `ModernTable.render.text` helper while preserving the unassigned-customer placeholder. Check the remaining custom renderers, including the audit reference-number renderer, before closing IBS-52. Add a regression against the actual custom renderers; helper-only tests missed these overrides.

### IBS-51 - Medium: receipt redistribution changes individual bill settlements

`IBSWeb/Areas/MSAP/Controllers/MaritimeReportController.cs:1260` combines every bill's ticket weights into one receipt-wide pool. Lines 1290-1291 redistribute the entire receipt's cash and EWT by those weights. This preserves the receipt total but loses per-bill withholding rounding, and can move payment/withholding between COS records.

Reproduced by invoking the compiled private report allocator with in-memory repository doubles. Three non-VAT LOCAL bills for the same withholding customer each have one ticket, with earned amounts 100.26, 100.26 and 100.74. The real `CollectionService.GetSettlementAmounts` returns:

| Bill | Expected cash | Expected EWT | Report cash | Report EWT |
| --- | ---: | ---: | ---: | ---: |
| 1 | 98.25 | 2.01 | 98.25 | 2.01 |
| 2 | 98.25 | 2.01 | 98.26 | 2.00 |
| 3 | 98.73 | 2.01 | 98.72 | 2.02 |

The fully settled receipt has cash 295.23 and EWT 6.03. All three bills satisfy the existing full-settlement policy. No different customer, partial payment or tax-policy change is needed. The earlier mixed-discount tests use values whose tax rounding happens to remain proportional and do not detect this case. Filtering the report to the second or third COS exposes the mismatch independently.

Smallest correction: retain each bill's authoritative settled cash/withholding amounts, allocate that bill's components among its source tickets, then combine ticket shares and receipt references. Preserve Basic/BAF discovery and all-bills denominator/filter safeguards. Add per-bill rounding and differing stored VAT/BilledTo cases to the workbook checks. No financial records were changed by this probe.

## Reviewed issues without additional actionable findings

These are local code-review results, not deployed or business-owner acceptance. Issue states remain In Review.

| Issues | Review and verification |
| --- | --- |
| IBS-46 | Explicit 29-controller inventory, retired-controller exclusions and default runner pass. |
| IBS-47 | Transaction-scoped code allocation and unique index reviewed. Fresh PostgreSQL migration/concurrency check passes, preserving numeric imported codes; configured source duplicate audit is read-only. |
| IBS-48 | Final receipt-number assignment precedes bill updates; audit includes old/new numbers. Node ordering assertions pass. Prior persisted rename evidence is retained; this review did not independently repeat a successful receipt rename. |
| IBS-49 | Removed editor chain and dispatch methods have no remaining application callers; same-named CollectionService helper is unrelated and retained. Default rendering, access, urgent-entry, workspace and persisted ticket/billing checks protect retained routes/workflows. |
| IBS-50 | Scheduled, manual and batch totals derive from stored rounded components. Rate/export half-cent/discount checks and persisted ticket/billing checks pass. |
| IBS-53 | All eight delete actions retain procedure guards and require POST plus antiforgery; corresponding confirmation forms carry tokens. Current metadata checks and prior live HTTP evidence agree. No fresh eight-controller browser deletion run was performed. |
| IBS-54 | Port/Terminal/Vessel invalid-model guards precede persistence; Terminal selections are repopulated. Focused invalid-Port no-service checks pass; prior clone create/edit evidence is retained. |
| IBS-55 | Discounted ticket components feed AR sales/VAT/WHT; workbook tests for discount and VAT boundaries pass. IBS-51's separate settlement-allocation finding remains open. |
| IBS-56 | Post-discard input is a fresh request model. Corrected original billing diagnostic and dependent provider-progress diagnostic pass on a fresh clone. |
| IBS-58 | Controller-wide Admin policy and current-role COS timeline visibility reviewed; focused access checks pass. Prior real-account matrix evidence is retained. |
| IBS-59 | Full scheduling database runner passes, including positive tariffs, rejected zero tariffs, persisted zero BAF, linked COS actual entry, revisions and concurrency. Production rate policy is unchanged. |
| IBS-60/61 | Current-access storage branch, active-document sandbox and URL encoders reviewed. Solution/default/focused access and Razor/special-filename regressions pass. |

IBS-57 remains pending accounting-owner confirmation at the next meeting. The provisional account registration is not evidence that its hierarchy or payment mapping has been accepted. No chart changes or source payments/postings were performed in this review.

## Validation and limits

- Solution and check-project builds pass. The incremental builds emitted zero warnings/errors; the previously recorded EF1002 warning remains outside this change's scope.
- Default checks, workflow access, rate/export/rounding checks, AR workbook checks and Node collection assertions pass.
- `--customer-code-only` and `--scheduling-database` pass using their own disposable databases, dropped by the runners.
- Fresh `--workspace-database` passes and retains `ibs_workspace_check_fce8cef0929f`. `--billing-integrity-database` and `--other-tug-progress-database` pass on that clone; the provider-progress transaction rolls back. Source data remains unchanged.
- The two additional probes reproduce the remaining gaps against actual view renderers and compiled service/report methods. Probe sources and raw logs remain under ignored `Docs/MSAP/Audits/Runtime/ReviewDispatchRendering.cjs`, `Runtime/ReviewAllocation/` and `Runtime/review-*.log`.
- Graphify navigation was budget-truncated; source/caller traces and executable probes provide the findings. No external review agent was used.
- No application fixes were made during this review. Deployed GCS permissions/expiry, exhaustive browser/accessibility acceptance and full owner workflow acceptance remain outstanding under IBS-43/the project.
