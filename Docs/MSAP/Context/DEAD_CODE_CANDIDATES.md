# MSAP dead-code cleanup record

Candidates recorded 26 October 2026 for [IBS-49](https://linear.app/ibs-msap/issue/IBS-49). The user authorized cleanup on 27 October, after checkpoint 1126bcbf1. The removals below are committed in cleanup checkpoint `4af1917c0` and await review.

| Scope | Reachability evidence | Decision |
| --- | --- | --- |
| Retired MSAPSuperAdmin `Controllers/DataController.cs` and `Views/Data/Index.cshtml` | The controller was `[NonController]`. MVC inventory excluded it. Repository C#, Razor, JavaScript and reflection-name searches found only its own view, service and obsolete landing-page links. | Removed both files. The endpoint inventory still rejects a Data controller, and WorkflowAccessCheck asserts the retired controller type is absent. |
| `IBS.Services/MSAP/SuperAdminService.cs`, its local `TableColumnDef`/`FieldDefinition` records and the MsapModule DI registration | The retired DataController was its only application consumer; the records were used only within the service. No diagnostic consumers were found. | Removed the service, its records and registration together. |
| DispatchTicketService tariff methods: `SaveTariffAsync`, `ApproveTariffAsync`, `RevokeTariffApprovalAsync`, `DisapproveTariffAsync`, `BatchApproveTariffAsync`, `BatchSetTariffAsync` | No application callers remained and corresponding controller actions were already retired. SchedulingCheck had three negative calls that tested the retired path. | Removed all six methods and those obsolete diagnostic calls. WorkflowAccessCheck now asserts the service methods stay absent as well as the retired HTTP actions. Active billing-readiness checks remain. |
| DispatchTicketService `DeleteImageAsync`, `DeleteVideoAsync`, `CheckForTariffRateAsync`, `GetCustomerSelectListAsync` | Repository searches found definitions only, with no application, Razor, JavaScript, reflection-name or diagnostic callers. Standalone media-deletion HTTP actions were already retired. | Removed these four helpers. Active ticket editing, attachment replacement/rollback and current rate preview/recalculation remain. |

## Retained behavior

- MSAPSuperAdmin HomeController and its authorized route remain. Its landing view now links to the active MSAP workspace instead of the retired raw editor. A Razor rendering check verifies the destination and absence of retired editor links.
- DispatchTicketService `DeleteTicketAsync` and `RestoreTicketAsync` remain: CosTicketCheck, UrgentEntryCheck and SchedulingCheck still exercise deletion protection and restoration behavior. The shared editability guard is also used by active ticket editing.
- `GetTerminalsByPortAsync` remains because DispatchTicketController calls it. Same-named methods in other services were not treated as this service's callers.
- Models, historical status values, migrations, accounting evidence and persisted records were not removed. This record covers the reviewed candidates and adjacent confirmed unused helpers, not a claim that every unused MSAP symbol has been eliminated.

## Verification and remaining gap

Solution build, default MSAP checks, workflow access, urgent actual-ticket checks, fresh-clone workspace and ticket/billing integrity checks pass. The code-only Graphify refresh pruned nodes sourced from the removed files; direct caller searches remain the removal evidence because the extractor does not model every Razor/top-level symbol.

At cleanup checkpoint `4af1917c0`, the optional full scheduling database diagnostic stopped on a stale zero-tariff fixture before the cleanup-related fixture changes. [IBS-59](https://linear.app/ibs-msap/issue/IBS-59) subsequently repairs the scheduling and linked COS fixtures in local uncommitted changes: history confirms intentional positive tariffs, the full database runner now passes, zero-tariff rejection and zero-BAF persistence are checked. Production rate policy is unchanged.
