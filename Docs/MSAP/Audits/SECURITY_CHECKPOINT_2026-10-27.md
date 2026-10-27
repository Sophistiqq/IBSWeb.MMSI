# MSAP security checkpoint - 27 October 2026

Scope: IBS-43 on `chore/audits-and-reviews`, after commit `6fd488249`, compared with the pinned master baseline recorded in BASELINE.md. Changes below remain local and uncommitted. No OpenCode or external review worker was used.

## Findings and decisions

| Finding | Reproduction | Local remediation |
| --- | --- | --- |
| [IBS-60](https://linear.app/ibs-msap/issue/IBS-60), Medium: development attachment URLs bypass access | Anonymous GET of a synthetic canary returned 200 and its exact contents. Static-file middleware ran before authentication. | The user explicitly selected current MSAP access for local downloads. The storage branch authenticates and evaluates the existing MSAP.Access policy before serving files. Same-cookie revocation and restoration were verified. |
| [IBS-61](https://linear.app/ibs-msap/issue/IBS-61), High: active uploads execute on the development origin | A real demo_ops ticket upload accepted a harmless HTML file. Chrome opened the stored View image target and set a DOM execution marker on the application origin. | Active HTML/XHTML/SVG/XML responses receive CSP sandbox; all local file responses receive nosniff. Reloading the exact saved HTML after the fix retained its visible text but left the execution marker unset. The ticket editor now JSON-encodes its script argument, and local URL segments encode special filename characters. |

The related raw JavaScript-string sink in EditTicket was source-confirmed and is covered by a real Razor rendering regression with quotes and closing script tags. A separate filename-based browser exploit was not executed. The active-document isolation uses the [W3C CSP sandbox directive](https://www.w3.org/TR/CSP/#directive-sandbox); PDF and raster-image responses are deliberately outside that sandbox condition. No new upload format/size policy was introduced.

## Environment and account boundaries

All HTTP writes and access revocation occurred on disposable clone `ibs_workspace_check_86cd8c7c37f8`. The workspace runner copied the source read-only and passed isolation, accounting write guards, posting/rebuild/reversal, close/reopen and migration rollback/reapply checks. The app ran only at 127.0.0.1:5093, with automatic MSAP migrations disabled and an isolated temporary attachment directory. Configuration was explicitly verified to select that clone.

| Account | Effective role | Relevant grants |
| --- | --- | --- |
| demo_ops | User | Prepare/edit schedules and create/edit tickets |
| demo_finance | User | Prepare billing and collections |
| demo_approver | User | Approve schedules and final billing |
| demo | Admin | Current supported administrator procedures; SuperAdmin remains a distinct policy |

Current MVC discovery: 29 active MSAP/MSAPAdmin/MSAPSuperAdmin controller types, 186 action descriptors, 87 POST actions. All 87 declare antiforgery validation. Guard composition, administrator policies, workflow procedure separation, master-file export subchecks, document slug restrictions and retired entry points were inspected. Payment-terms lookups retain module-level access; the maintenance page intentionally allows anonymous access and redirects when maintenance is off.

## Live coverage

| Probe group | Count | Result and limit |
| --- | ---: | --- |
| Anonymous actions | 185 | Denied by authentication. |
| Public maintenance control | 1 | Intentional redirect to /MSAP; not an authorization defect. |
| Denied role/action requests with valid antiforgery tokens | 446 | All denied according to current effective grants and declared endpoint policy. AJAX denial uses the existing success=false JSON envelope, often HTTP 200. |
| Missing/invalid tokens on permitted POST routes | 230 | All returned 400. This verifies antiforgery, not successful business execution. |
| Local storage access controls | 9 | Anonymous denied, four current MSAP accounts allowed; finance's existing cookie denied for home/billing/file after its two grants were revoked, then file access restored after restoring those exact grants. |
| Permitted GET boundaries | 181 | No access denial or server error. Missing-record cases deliberately use nonexistent IDs; these are boundary checks, not complete business journeys. |
| Permitted export controls | 2 | Admin customer-master and tariff exports returned XLSX ZIP content. Only audit_trails changed, as expected for export auditing. |
| Isolated live account/grant profiles | 25 | Inactive and explicit invalid-role sessions denied with existing cookies; SuperAdmin allowed, then denied after restoration to Admin. Isolated rate preparer/approver, provider preparer/confirmer and treasury boundaries matched their grants. All original grant flags, active flags and module-claim rows were restored exactly. These positive checks use validation/missing-target responses where indicated, not complete successful mutations. |
| Valid workflow requests | 15 | Five successful operations plus ten cross-role denials with the same valid payloads. Ops created a tentative schedule; approver confirmed its current revision; ops recorded an actual ticket; finance prepared billing; approver final-approved that revision. Denied operations left MSAP fingerprints unchanged. |

Total: 1,094 HTTP probes, excluding login/setup requests and the browser canary navigation. The 871-probe broad matrix was repeated after the final application changes. Its before/after content fingerprints of all 32 MSAP-schema tables matched after restoring grants. The separate allowed workflow intentionally created synthetic clone records; earlier failed diagnostic assertions also left synthetic clone fixtures and were corrected for the application's actual JSON/redirect responses and ten-character billing-number limit. No source reset, source schema change, source payment or source posting was performed.

Discovery/expected permissions came from MVC metadata and the real database-backed access service. The matrix proves enforcement of declared guards for these accounts; it does not independently prove that every possible grant combination represents the intended business policy. Source review and independent grant checks in WorkflowAccessCheck/RateAccessCheck supplement it.

## Automated verification

- Solution build: zero warnings and errors.
- Check-project build: passes; existing EF1002 warning at SchedulingCheck.cs:72 remains.
- Default runner: passes, including real Razor serialization of an untrusted attachment URL and special-character/nested-path storage round trips.
- Focused development-storage HTTP checks: anonymous/unassigned/revoked denial, permitted exact-byte read, active-document response headers, normal PNG/PDF response delivery, unrelated public health route and absence of the local route in Production.
- Existing focused workflow/rate/draft-discard checks pass.
- Final diff, UTF-8/no-BOM, LF and final-newline checks pass. The code-only Graphify graph is refreshed (10,029 nodes / 29,531 edges); broad graph traversal was truncated and was used only for navigation, with source and MVC metadata providing the verification evidence.

Ignored local raw evidence is in Runtime/endpoint-inventory.json, security-accounts.json, security-http-matrix.json, security-read-results.json, security-export-results.json, security-workflow-results.json and security-profile-results.json. These contain no passwords or session cookies. The audit app is stopped and the browser tab is closed. Automatic approval review rejected deletion of the isolated temporary canary files with "blocked by policy"; those harmless files remain in TEMP/msap-ibs43-storage. The disposable database is retained for inspection.

## Remaining acceptance

IBS-43 remains In Progress. A designated deployed environment is still needed to test actual GCS bucket/object permissions, signed-link expiry and browser behavior across the deployed origin. Source uses msap/ object prefixes and a default 30-minute signing duration; that is source evidence, not live cloud acceptance. Local signed URLs now require current access but have no independent time expiry, per the user's selected local policy.

No complete successful mutation journey is claimed for every POST action or every possible grant combination. Treasury, rate preparation/review and provider-cost boundaries were exercised by temporarily assigning isolated grants to the clone finance account. Inactive, explicit invalid-role and positive SuperAdmin sessions were also checked and restored. An absent MSAP.Role claim deliberately falls back to User for legacy accounts; it does not grant module entry without current procedure permissions. The probe uses an explicit invalid claim to test persisted-role rejection. Comprehensive browser/accessibility acceptance and complete successful operations for each isolated grant remain outside this checkpoint. Broader finance/report/provider AP acceptance stays under IBS-45, and full owner workflow acceptance under the project. These are coverage limits, not additional confirmed defects.
