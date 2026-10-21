# MSAP agent handoff

Start here for MSAP work. Checkpoint: 19 October 2026, application commit 3257f348b. Follow the repository [AGENTS.md](../../../AGENTS.md) and applicable .editorconfig first.

## Read order

1. [Current status](CURRENT_STATUS.md): delivered milestones, verification evidence and current boundaries.
2. [Blockers and next work](BLOCKERS.md): the active gap list and deliberately deferred work. Select work from the current user request; listed work is not blanket authorization to implement every item.
3. Read the relevant reference below for the task. Historical proposals are not current requirements.

| Task | Reference |
|---|---|
| Master imports, correction workbooks, source quality and confirmations | [Master-file review](MASTER_FILE_IMPORT_REVIEW.md) |
| Workflow testing and release evidence | [Workflow test plan](WORKFLOW_TEST_PLAN.md) |
| UI layout, spacing and reusable styles | [Style guide](../../Meetings/MSAP_STYLE_GUIDE.md), [wireframe images](../../Meetings/wireframe-previews) and existing portable UI files |
| Business rationale and previous implementation decisions | [Implementation history](IMPLEMENTATION_HISTORY.md), then the relevant [meeting record](../../Meetings/meeting-record.md) |
| Original brief and source/accounting baseline | [Development brief](../../Meetings/DEVELOPMENT_BRIEF.md), [implementation baseline](../../Meetings/IMPLEMENTATION_BASELINE.md) |
| Source data | Original CSVs in Docs/MSAP/CSVs for Imports and supporting workbooks in Docs/Meetings; retain originals |
| Module structure | [Integration reference](../INTEGRATION.md); older access/workflow descriptions require comparison with current code |

## Working context

- Keep edits scoped to MSAP unless the user authorizes IBS work. IBS payment integration is a separate delivery.
- The user handles live testing and migrations in this work. Record what the agent actually ran; compilation and database-free checks do not establish full workflow acceptance.
- Skip dotnet builds for changes limited to Razor/CSS/JavaScript; use targeted checks. For C# or schema changes, build and verify the relevant paths without repeatedly running unrelated suites.
- Preserve flat, compact layouts and portable ui-* styling. Billing print appearance has its own requirements.
- Use full names on display/audits. Admin/SuperAdmin have supported actions; ordinary users require effective grants. Existing financial/revision/state guards still apply.
- Update CURRENT_STATUS.md when a milestone or verification result changes, and BLOCKERS.md when a gap changes. Keep source-specific details and test cases in their owning references.
- Older paths contain navigation pointers for existing bookmarks. Original meeting evidence stays under Docs/Meetings. The user manuals under Docs/MSAP/manual are awaiting refresh.

The active status/backlog lives in this folder. The archived plan records earlier decisions and may contain retired names, access inventories or proposed features that were later replaced.
