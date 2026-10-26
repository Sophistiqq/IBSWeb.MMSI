# MSAP dead-code candidates

Recorded 26 October 2026 for [IBS-49](https://linear.app/ibs-msap/issue/IBS-49). These are candidates for later reachability review, not approved removals. No code was deleted or marked inline. Cleanup remains deferred until the reviews are reconciled.

| Candidate | Evidence recorded so far | Required before a removal decision |
| --- | --- | --- |
| `IBSWeb/Areas/MSAPSuperAdmin/Controllers/DataController.cs` and `Views/Data/Index.cshtml` in that area | The controller is explicitly `[NonController]`. The default endpoint inventory excludes it, and WorkflowAccessCheck asserts that raw financial editing remains unavailable. | Check explicit view rendering, reflection, scripts, diagnostics and operational use. Preserve the regression checks that prevent this workflow from becoming exposed again. |
| `IBS.Services/MSAP/SuperAdminService.cs` and its DI registration in `IBSWeb/MSAP/MsapModule.cs` | The service is still registered. The retired DataController consumes its table metadata, query, lookup and save methods. | Trace every remaining consumer and any reflection-based use before deciding whether the service, registration and retired view can be removed together. DI registration alone does not establish active use. |
| Retired tariff methods in `IBS.Services/MSAP/DispatchTicketService.cs`: `SaveTariffAsync`, `ApproveTariffAsync`, `RevokeTariffApprovalAsync`, `DisapproveTariffAsync`, `BatchApproveTariffAsync`, `BatchSetTariffAsync` | Corresponding controller actions are absent. `Checks/MSAP/SchedulingCheck.cs` still calls SaveTariffAsync, ApproveTariffAsync and BatchApproveTariffAsync to verify rejected legacy operations. | Review C#, Razor, JavaScript, reflection and diagnostic callers. Decide explicitly how to preserve the legacy-workflow rejection coverage before deleting or replacing these methods. |

The active MSAPSuperAdmin HomeController is not included merely because it shares the retired controller's area. Missing menu links, direct references or graph edges are insufficient evidence of dead code. A complete unused-code inventory and retain/remove decisions remain open under IBS-49.
