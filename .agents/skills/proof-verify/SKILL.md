---
name: proof-verify
description: Proof-based verification workflow for IBSWeb (.cs, .cshtml, .js, .css, and MSAP UI). Replaces blind dotnet build checks on Razor views with static JS/MSAP gates, auto-started port 5073 Desktop/Mobile headless browser proofs, and delegated OpenCode free-model review.
---

# Proof-Based Verification

Use this skill after making backend, `.cshtml`, `.js`, `.css`, or MSAP UI changes in IBSWeb to produce concrete runtime, layout, and code-review proof while minimizing primary-agent token usage.

## Why This Exists

- `dotnet build` does not validate inline `<script>` syntax, runtime Razor rendering (`NullReferenceException`, missing partials), AJAX endpoint wiring, CSS/dark-mode styling, or Desktop vs. Mobile layout overflow in `.cshtml` views.
- Self-reviewing large diffs in the primary coding conversation wastes expensive context tokens on checks that deterministic scripts and free external models can perform independently.

## 4-Stage Pipeline

1. **Stage 1 — Deterministic Static & `.cshtml` / MSAP Gate (0 tokens)**
   - Verifies `git diff --check` and `.editorconfig` compliance (UTF-8, LF line endings, final newline).
   - Runs `dotnet build "Integrated Business System.sln" -v minimal` **only** when `.cs`/`.csproj`/`.sln` or Razor C# directives (`@model`, `@inject`, `@using`, `@{`) changed (override with `--force-build` or `--skip-build`).
   - Extracts inline `<script>` blocks from `.cshtml` views (sanitizing Razor `@(...)` expressions) and checks `.js` + inline JS syntax via Node's `vm.Script`.
   - Checks `document.getElementById('...')` literals in changed `.cshtml` views against locally declared IDs.
   - Enforces `MSAP_AGENTS.md` rules on changed MSAP views/scripts (blocks native `alert`/`confirm`/`prompt`, direct `Swal.fire`, raw `toastr`, raw `.DataTable({...})`, and raw `.select2(...)`).

2. **Stage 2 — Auto-Started App (Port `5073`) & Dual-Viewport Browser Proof (0 tokens)**
   - Checks `http://localhost:5073/health`. If not running, automatically starts `IBSWeb` on port `5073` (`ASPNETCORE_ENVIRONMENT=Development`) and stops it cleanly on exit.
   - Uses installed Chrome/Edge headlessly via native Node CDP (zero npm dependencies) with a persistent profile at `artifacts/proof-verify/.browser-profile` (verified gitignored via `git check-ignore`).
   - Captures **Desktop (`1440×900`)** and **Mobile (`390×844`)** screenshots (plus `dark` theme Desktop proof for `/MSAP` routes).
   - Traps ASP.NET 500/error pages, uncaught JS exceptions (`pageerror`), `console.error`, failed local network/AJAX calls, broken `<img>` elements, and DOM horizontal overflow (`scrollWidth > innerWidth`).

3. **Stage 3 — Delegated Free-Model Proof Judge (`opencode`)**
   - **Code/Diff Judge:** Runs `opencode run -m opencode/glm-5.3-flash-free` (falling back to `opencode/deepseek-v4.1-flash-free`, then `opencode/mimo-v2.6-flash-free`) on the filtered diff + Stage 1/2 context.
   - **Visual Layout Judge:** Runs `opencode run -m opencode/nemotron-3.5-super-vl-free` on the captured Desktop and Mobile PNG screenshots to verify visual alignment, mobile responsiveness, and styling.

4. **Stage 4 — Logging Detected Issues & Proofs to Linear (`linear_linear` MCP)**
   - Every run writes `artifacts/proof-verify/latest-linear-payload.json` containing `team` (`IBS-MSAP`), inferred labels (`Validation`, plus `Bug` / `UIAlignment` / `Improvement`), `priority` (`2` High / `3` Medium), `relatedIssueId` (from `--linear-issue` or branch/commit `IBS-\d+`), `suggestedTitle`, `markdownBody`, and `screenshots` (`filename`, `path`, `size`, `contentType`).
   - If `--log-linear` is passed and `LINEAR_API_KEY` is set in the environment, the script posts directly via the Linear GraphQL API. Otherwise, follow the mandatory MCP handoff below when issues are detected or `--log-linear` is requested.

## Mandatory Agent Workflow for Linear Logging (`linear_linear` MCP)

When `latest-linear-payload.json` reports `hasDetectedIssues: true` (or when logging proof to an active Linear issue):

1. Read `artifacts/proof-verify/latest-linear-payload.json`.
2. If `screenshots` is non-empty, upload each relevant Desktop/Mobile PNG sequentially using `linear_linear` `prepare_file_upload` (`filename`, `contentType: "image/png"`, `size`), `PUT` the raw file bytes with the exact signed `uploadRequest.headers` (within 60s), and append the returned Markdown image links to `markdownBody`.
3. **If tied to an active issue (`relatedIssueId`, e.g., `IBS-126`):**
   - Post the proof report and embedded Desktop/Mobile screenshots using `linear_linear` `save_comment` (`issueId`, `body`).
   - If standalone defects were uncovered outside the active issue's scope, create a sub-issue or linked issue via `linear_linear` `save_issue` (`team: "IBS-MSAP"`, `parentId: relatedIssueId`, `title`, `description`, `labels`, `priority`).
4. **If not tied to an existing issue (standalone defects detected):**
   - Check `linear_linear` `list_issues` (`team: "IBS-MSAP"`, `query`) first to avoid creating duplicate issues.
   - Call `linear_linear` `save_issue` (`team: "IBS-MSAP"`, `title: suggestedTitle`, `description: markdownBody`, `labels`, `priority`) and report the created `IBS-XXX` identifier and URL.

## Usage Commands

```powershell
# 1. Standard full proof (auto-detects changed files/routes, starts port 5073 if down, runs Stage 1-3 + emits Linear payload)
node .agents/skills/proof-verify/scripts/proof-verify.mjs

# 2. Verify specific routes and bind Linear payload to an active issue
node .agents/skills/proof-verify/scripts/proof-verify.mjs --urls /MSAP/Billing/Index --linear-issue IBS-126

# 3. Direct API logging when LINEAR_API_KEY is configured in the environment
node .agents/skills/proof-verify/scripts/proof-verify.mjs --log-linear --linear-team IBS-MSAP

# 4. Fast deterministic proof only (Stage 1 + Stage 2 browser capture, 0 LLM tokens)
node .agents/skills/proof-verify/scripts/proof-verify.mjs --skip-ai

# 5. Interactive one-time sign-in to cache auth cookies for protected routes
node .agents/skills/proof-verify/scripts/proof-verify.mjs --interactive-login --urls /MSAP/Billing/Index

# 6. Interactive form filling & multi-step clicks via local Ollama ("Guided Hands")
node .agents/skills/proof-verify/scripts/guided-hands.mjs --url /MSAP/VesselSchedule/Create --inspect
node .agents/skills/proof-verify/scripts/guided-hands.mjs --steps-file plan.json --no-autostart
node .agents/skills/proof-verify/scripts/guided-hands-bridge.mjs   # start once for zero-prompt Chrome extension mode
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --steps-file plan.json              # live cyan tab group
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --background --steps-file plan.json # quiet background tab
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --playbook msap-schedule-create     # instant cached replay (0ms for unchanged fields)
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --playbook msap-full-to-billing     # end-to-end Schedule -> Rates -> COS -> Tickets -> Billing
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --playbook msap-billing-to-cleared --var cosId=9  # Billing -> Review -> Approval -> Collection -> Deposit -> Cleared
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --playbook msap-full-lifecycle      # complete 9-stage Schedule -> Cleared chain
node .agents/skills/proof-verify/scripts/guided-hands.mjs --extension --playbook msap-schedule-create --override PortId=SUBIC --var tomorrow=2026-11-26
```

For unattended login against protected routes without `--interactive-login`, set `IBS_VERIFY_USER` and `IBS_VERIFY_PASS` in your shell environment (or pass `--user` and `--pass`).

## Interactive Form Automation ("Guided Hands" — Local Ollama + CDP / Extension)

When a change requires multi-step UI interactions (opening modals, clicking dynamic row buttons, triggering cascading AJAX dropdowns like `Port -> Terminal`, filling `ModernSelect` / Select2 controls with realistic mock data, and asserting the saved result), use `.agents/skills/proof-verify/scripts/guided-hands.mjs` before escalating to paid/heavy browser agents:

- **Orchestrator–Executor Split:** The primary coding agent (Brain) supplies a concise step plan (`--steps`, `--steps-file`, or repeatable `--step` flags) and pins exact business requirements via `overrides` (matched by field key/label and option display text). The local worker executes clicks deterministically and calls local Ollama (`qwen2.5:1.5b` by default, schema-locked via JSON `format`, `keep_alive: "2m"`) only for realistic mock field generation or fuzzy button matching.
- **Self-Updating Playbooks (`--playbook` / `--save-playbook`):** Playbooks saved under `.agents/skills/proof-verify/playbooks/*.json` store each step's `knownFields` fingerprint + last known-good values + dynamic URL captures (`capture_url` → `{scheduleId}`, `{cosId}`, `{billingId}`, `{collectionId}`) + template variables (`{today}`, `{tomorrow}`, `{rand}`, `--var`). Replays reuse unchanged fields in **0ms (0 Ollama calls)**, invoke Ollama **only on newly added or drifted fields**, enforce deterministic Philippine date/time guards, run `fill_and_submit` self-healing retries on validation errors, and auto-update the playbook file on PASS (disable with `--no-update-playbook`).
- **Zero-Prompt Chrome Extension Mode (`--extension`):** Load `.agents/skills/proof-verify/extension` once via `chrome://extensions` (*Load unpacked*). `guided-hands.mjs --extension` auto-starts `guided-hands-bridge.mjs` if needed, opens/reuses a cyan **`Guided Hands`** tab group with a pulsing border + live HUD pill (recovering cleanly from dirty `beforeunload` pages), and never triggers Chrome's *"Allow remote debugging?"* prompt. Add `--background` to run quietly without stealing focus.
- **Per-User Auth Profiles (Headless CDP):** Passing `--user <name>` (or `IBS_VERIFY_USER`) isolates cookies under `artifacts/proof-verify/.browser-profile-<user>` (verified gitignored) so test accounts like `OPERATIONS` never overwrite the default profile.
- **Standalone Checks:** `--inspect` dumps visible buttons & editable fields as JSON (`0` LLM calls); `--benchmark` tests local Ollama latency and JSON schema accuracy without starting the app.

## Model Escalation Ladder

1. **Tier 1 (0 tokens):** Stage 1 static checks + Stage 2 headless Desktop/Mobile DOM & telemetry proof.
2. **Tier 2 (Free OpenCode models):** `opencode/glm-5.3-flash-free` (fallback: `opencode/deepseek-v4.1-flash-free`, `opencode/mimo-v2.6-flash-free`) for diff review; `opencode/nemotron-3.5-super-vl-free` for Desktop & Mobile screenshots.
3. **Tier 2.5 (Local Ollama — 0 cloud tokens):** `.agents/skills/proof-verify/scripts/guided-hands.mjs` (`qwen2.5:1.5b` + native CDP) for multi-step form filling, realistic mock test data, modal flows, and cascading dropdown verification.
4. **Tier 3 (Lightweight subagent):** Invoke an Antigravity `flash_lite` / `flash` subagent if deeper multi-file codebase tracing is needed after a Stage 1–3 failure.
5. **Tier 4 (Paid / Computer-use, e.g. `jev` or Chrome DevTools MCP):** Escalate only when a bug requires open-ended visual exploration, deep CSS/network/performance profiling, or complex drag/canvas interactions that guided step plans cannot prove.

## Proof Artifacts

Each run writes timestamped proof files and updates the latest summary under gitignored `artifacts/proof-verify/`:

- `artifacts/proof-verify/latest-report.md` — Human-readable pass/fail report across Stages 1–3.
- `artifacts/proof-verify/latest-report.json` — Machine-readable telemetry, DOM audit, and model verdicts.
- `artifacts/proof-verify/latest-linear-payload.json` — Structured Linear issue/comment payload (`IBS-MSAP`) + screenshot upload metadata.
- `artifacts/proof-verify/latest-guided-hands.json` — Latest interactive `guided-hands.mjs` step trace, Ollama latency stats, telemetry, and screenshot paths.
- `artifacts/proof-verify/<timestamp>/*-desktop.png` & `*-mobile.png` — Visual proof screenshots.
