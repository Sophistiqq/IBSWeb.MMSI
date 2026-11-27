---
name: msap-editor
description: Specialized MSAP implementation workflow for feature edits, bug fixes, and UI changes in IBSWeb. Enforces MSAP_AGENTS.md house components, checks Linear/Graphify context, routes targeted Checks/MSAP verification, and closes the loop with proof-verify.
---

# MSAP Editor Workflow

Use this skill when implementing features, fixing bugs, or updating views/scripts/services for the **MSAP** module (including any required shared changes outside `IBSWeb/Areas/MSAP` such as `IBS.Models`, `IBS.DataAccess`, `IBS.Services`, `IBS.DTOs`, or migrations).

This skill is designed to work either directly in the main session or inside a **fresh-context implementer subagent** dispatched by a planner/coordinator.

## 1. Gather Context & Invariants (Before Editing)

1. **Ticket context:** If a Linear issue key is referenced (e.g., `IBS-126`), read it via `linear_linear` (`get_issue` + `list_comments`) to confirm acceptance criteria and edge cases.
2. **Codebase tracing:** Use `graphify query "<question>"` or targeted reads to trace the controller → service → repository/DbContext → view call path before modifying code.
3. **Applicable rules:**
   - Follow root `AGENTS.md` and `.editorconfig` for all C# / shared / backend edits.
   - Read `MSAP_AGENTS.md` before editing any MSAP `.cshtml`, `.js`, or `.css` file.
4. **Verification target:** Consult `.agents/msap-check-map.json` to identify the exact `Checks/MSAP` command(s), `proof-verify` routes, and `guided-hands.mjs` playbooks that cover the modules you are about to touch.

## 2. Confirm a Short Edit Brief (Spec-First)

Before making edits on any non-trivial task, state (or confirm from the coordinator's brief):
- **Files in scope:** Exact MSAP and shared files to modify.
- **Invariants to preserve:** Accounting calculations, approval/SoD roles (`MsapRoles.AccessPolicy`), `msap` schema isolation, period locks, or audit trails.
- **Proof command:** The exact `proof-verify` / `Checks/MSAP` / playbook command that will verify the change.

## 3. Apply Minimal, Clean Edits

- **MSAP UI files (`IBSWeb/Areas/MSAP/**`, `IBSWeb/wwwroot/msap/**`):**
  - Strictly use house utilities: `ModernAlert`, `ModernTable.config(...)`, `.js-modern-select` / `window.refreshModernSelect(...)`, `.modern-layout.app-ui`, `.modern-card.ui-card`, and theme CSS variables (`var(--surface)`, `var(--on-surface)`, etc.).
  - Never introduce `alert(...)`, `confirm(...)`, `prompt(...)`, direct `Swal.fire(...)`, raw `toastr.*`, raw `.DataTable({...})`, or direct `.select2(...)` (enforced deterministically by `.agents/hooks.json`).
- **Shared / Backend files (`IBS.Services`, `IBS.DataAccess`, `IBS.Models`, etc.):**
  - Keep controllers thin and business rules in services.
  - Use `var` where C# permits, async end-to-end, `AsNoTracking()` for read-only queries, and preserve nullable correctness and `.editorconfig` formatting (UTF-8, LF, final newline).
  - Do not perform unrelated refactors or reformatting outside the task scope.

## 4. Verify & Close the Loop

1. **Run mapped domain checks** from `.agents/msap-check-map.json` (e.g., `dotnet run --project Checks/MSAP/Check.csproj -- --billing-integrity-database` or `node Checks/MSAP/<Check>.cjs`) whenever backend/business logic changed.
2. **Run `proof-verify`:**
   - Fast deterministic gate (0 tokens): `node .agents/skills/proof-verify/scripts/proof-verify.mjs --skip-runtime --skip-ai`
   - Full UI / visual / review proof when views, scripts, CSS, or routes changed: `node .agents/skills/proof-verify/scripts/proof-verify.mjs --urls /MSAP/<Controller>/<Action>` (add `--linear-issue IBS-XXX` when tied to a ticket).
   - Multi-step interactive workflow changes: run the matching playbook or preset via `node .agents/skills/proof-verify/scripts/guided-hands.mjs --playbook <name>` (or `--preset <name>`).
3. **Update `MSAP_CHANGELOG.md`** only if the change directly affects MSAP end users (skip for refactors, internal plumbing, or non-user-facing fixes).
4. **Review `git diff`** before reporting completion to ensure no unintended edits, trailing whitespace, or `.editorconfig` drift remain.

## Delegating to a Fresh Implementer Subagent

When a coordinator/planner delegates an MSAP edit to a fresh `self` subagent (to avoid context rot), pass a compact prompt with:
1. Activate the `msap-editor` skill.
2. Linear issue / goal + exact acceptance criteria.
3. Exact files to inspect/modify and invariants to preserve.
4. Exact verification commands from `.agents/msap-check-map.json` + `proof-verify`.
5. Ask the subagent to return: changed files, verification results, and any blockers or follow-ups.
