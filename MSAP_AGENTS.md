# MSAP_AGENTS.md

## Purpose

This document defines the mandatory frontend and UI design system standards for the **MSAP (Marine Services & Accounting Platform)** module in IBSWeb (`IBSWeb/Areas/MSAP` and related shared MSAP assets).

Whenever a task touches MSAP views (`.cshtml`), client scripts (`.js`), or stylesheets (`.css`), agents must strictly adhere to the guidelines and house components defined here.

---

## 1. Ponytail & UI Component Precedence

> **Rule: House components strictly beat native widgets and generic libraries.**

* Ponytail's rule to achieve the *"shortest working diff"* or *"least new code"* **never** justifies bypassing MSAP's established UI utilities.
* Using `window.alert()`, `window.confirm()`, unstyled `<select>`, raw `<table>`, or bare Bootstrap classes instead of MSAP's house utilities is an instruction violation.
* Minimal code means using the existing house utilities cleanly—not replacing them with primitive or ad-hoc browser defaults.

---

## 2. Dialogs & Notifications (`ModernAlert`)

**File:** `IBSWeb/wwwroot/msap/js/modern-alert.js`  
**Layout Inclusion:** Automatically included in `_Layout.cshtml`.

All alerts, user confirmations, prompts, and modal loading spinners in MSAP **must** use `ModernAlert`.

### Available Methods

| Method | Purpose | Usage Example |
| :--- | :--- | :--- |
| `ModernAlert.success(message, title?)` | Success notification | `ModernAlert.success('Billing saved successfully.');` |
| `ModernAlert.error(message, title?)` | Error notification | `ModernAlert.error('Failed to process request.');` |
| `ModernAlert.warning(message, title?)` | Warning notification | `ModernAlert.warning('Customer has pending balances.');` |
| `ModernAlert.info(message, title?)` | Informational popup (auto-dismisses) | `ModernAlert.info('No changes were made.');` |
| `ModernAlert.confirm({ ... })` | User confirmation modal (returns Promise) | See below |
| `ModernAlert.prompt({ ... })` | Input prompt modal with validation | See below |
| `ModernAlert.showLoading(title?)` | Displays loading spinner overlay | `ModernAlert.showLoading('Saving billing...');` |
| `ModernAlert.close()` | Closes the current modal/loader | `ModernAlert.close();` |

### Confirmation Example
```javascript
const result = await ModernAlert.confirm({
    title: 'Discard draft billing?',
    text: 'This action cannot be undone. Are you sure you want to proceed?',
    confirmText: 'Discard',
    cancelText: 'Keep editing',
    icon: 'warning' // 'question', 'warning', 'error', 'info', 'success'
});

if (result.isConfirmed) {
    // proceed
}
```

### Prompt Example
```javascript
const result = await ModernAlert.prompt({
    title: 'Reason for Voiding',
    text: 'Please specify why this record is being voided:',
    placeholder: 'Enter remarks...',
    confirmText: 'Void Record',
    validator: (value) => !value?.trim() ? 'Reason is required.' : null
});

if (result.isConfirmed) {
    const reason = result.value;
}
```

### Prohibited
- ❌ Do NOT use `alert(...)` or `confirm(...)`.
- ❌ Do NOT call `Swal.fire(...)` with custom or default unstyled configurations.
- ❌ Do NOT use raw Toastr (`toastr.success`, etc.) without MSAP styling.

---

## 3. Data Tables (`ModernTable`)

**File:** `IBSWeb/wwwroot/msap/js/modern-table.js`  
**Layout Inclusion:** Automatically included in `_Layout.cshtml`.

All standard tabular listings and DataTables in MSAP must be configured via `ModernTable.config(...)`.

### HTML Structure
```html
<div class="modern-card ui-card">
    <div class="modern-card-header-action flex justify-between items-center w-full ui-card-header">
        <div class="flex items-center gap-2">
            <span class="material-symbols-outlined text-primary">table_chart</span>
            <h2 class="modern-headline-md ui-section-title">Record List</h2>
        </div>
    </div>
    <div class="modern-card-body ui-card-body">
        <table class="modern-table ui-table" id="myTable">
            <thead>
                <tr>
                    <th>Code</th>
                    <th>Date</th>
                    <th>Amount</th>
                    <th>Status</th>
                    <th class="text-center">Actions</th>
                </tr>
            </thead>
        </table>
    </div>
</div>
```

### JavaScript Initialization
```javascript
$('#myTable').DataTable(ModernTable.config({
    serverSide: true, // or false for client-side
    ajax: ModernTable.ajax('/MSAP/MyController/GetRecords', function(d) {
        // Optional: append custom filter parameters to request payload
        d.customFilter = $('#filterInput').val();
        return d;
    }),
    columns: [
        { data: 'code', render: ModernTable.render.text },
        { data: 'date', render: ModernTable.render.date },
        { data: 'amount', render: ModernTable.render.currency },
        {
            data: 'status',
            render: (data) => ModernTable.render.badge(data, 'status-open')
        },
        {
            data: null,
            orderable: false,
            className: 'text-center',
            render: (data, type, row) => ModernTable.render.dropdown([
                { label: 'View Details', icon: 'visibility', url: `/MSAP/MyController/Details/${row.id}` },
                { label: 'Edit', icon: 'edit', url: `/MSAP/MyController/Edit/${row.id}` },
                'divider',
                { label: 'Void', icon: 'cancel', isError: true, onclick: `voidRecord(${row.id})` }
            ])
        }
    ]
}));
```

### Built-in Renderers (`ModernTable.render`)
- `text(data)`: XSS-safe text rendering.
- `date(data)`: Formats date to `MMM dd, yyyy` (e.g. `Nov 12, 2026`).
- `dateTime(datePart, timePart)`: Formats date with time.
- `currency(data)`: Formats decimal to Philippine Peso (e.g. `₱1,250.00`).
- `badge(data, statusClass)`: Generates `<span class="modern-badge-sm {statusClass}">...</span>`.
- `ellipsis(data, maxWidth)`: Truncated text with full tooltip.
- `dropdown(items)`: Standardized floating action dropdown menu with keyboard/scroll handling.

### Prohibited
- ❌ Do NOT instantiate raw `$('#table').DataTable({...})` without `ModernTable.config(...)`.
- ❌ Do NOT construct manual action dropdown menus in table cells that break responsive clipping.

---

## 4. Form Selects (`ModernSelect`)

**File:** `IBSWeb/wwwroot/msap/js/modern-select.js`  
**Layout Inclusion:** Automatically included in `_Layout.cshtml`.

All enhanced/searchable select dropdowns in MSAP forms must use `ModernSelect`.

### HTML Structure
```html
<select asp-for="CustomerId" asp-items="Model.Customers"
        class="js-modern-select"
        data-placeholder="Select customer..."
        data-searchable="true">
    <option value="">Select customer...</option>
</select>
```

### Rules & Behaviors
1. **Auto-Initialization:** Any `<select>` with class `js-modern-select` is automatically initialized on DOM ready.
2. **Dynamic Re-Population:** When options are added, removed, or changed dynamically via AJAX or JavaScript, call:
   ```javascript
   window.refreshModernSelect($('#mySelect'));
   ```
3. **Validation Integration:** Integrates with HTML5 and jQuery unobtrusive validation automatically.

### Prohibited
- ❌ Do NOT introduce unstyled `<select class="form-select">` where searchable dropdowns are required.
- ❌ Do NOT manually instantiate raw `$.fn.select2()` directly unless wrapping a legacy integration that explicitly requires it.

---

## 5. UI Layout and CSS Utilities (`modern-ui.css` & `ui-system.css`)

**Files:** `IBSWeb/wwwroot/msap/css/modern-ui.css` & `IBSWeb/wwwroot/css/ui-system.css`

MSAP follows a unified modern UI styling system with dark/light mode support.

### Page Shell Hierarchy
Every MSAP view should follow this top-level container structure:
```html
@{
    ViewData["Title"] = "Page Title";
    ViewData["IsFullWidth"] = true; // Use full width container
}

<div class="modern-layout app-ui">
    <main class="modern-container ui-page">
        <!-- Page Header Toolbar -->
        <div class="flex-stack ui-page-toolbar">
            <div>
                <nav aria-label="Breadcrumb">
                    <ol class="ui-breadcrumb">
                        <li>MSAP</li>
                        <li><a asp-action="Index">Section</a></li>
                        <li aria-current="page">Current Page</li>
                    </ol>
                </nav>
                <h1 class="modern-headline-lg ui-page-title">@ViewData["Title"]</h1>
            </div>
            <div class="flex gap-2 ui-actions">
                <button type="submit" class="modern-btn-primary ui-page-action">
                    <span class="material-symbols-outlined">save</span> Save
                </button>
                <a asp-action="Index" class="modern-btn-secondary ui-page-action">
                    <span class="material-symbols-outlined">cancel</span> Cancel
                </a>
            </div>
        </div>

        <!-- Content Cards -->
        ...
    </main>
</div>
```

### Component Class Reference

| Element | Modern UI Classes | Notes |
| :--- | :--- | :--- |
| **Card Container** | `.modern-card.ui-card` | Standard content card |
| **Card Header (Action)** | `.modern-card-header-action.ui-card-header` | Flex header with title and actions |
| **Card Header (Info)** | `.modern-card-header-info.ui-card-header` | Header with icon and section title |
| **Card Body** | `.modern-card-body.ui-card-body` | Padding and layout inside card |
| **Page Title** | `.modern-headline-lg.ui-page-title` | Top-level H1 page heading |
| **Section Title** | `.modern-headline-md.ui-section-title` | H2 section title inside cards |
| **Form Label** | `.modern-label.ui-label` | Standard label for inputs |
| **Form Input** | `.modern-input.ui-input` (or `.form-control.ui-input`) | Standard text/number/date input |
| **Readonly Value** | `.modern-value-lg`, `.modern-value-md` | Emphasized read-only display values |
| **Primary Button** | `.modern-btn-primary` | Main call-to-action button |
| **Secondary Button**| `.modern-btn-secondary` | Neutral or secondary button (can have `.active`) |
| **Error Button** | `.modern-btn-error` | Destructive action button |
| **Grid Layout** | `.modern-grid` with `.modern-col-4`, `.modern-col-6`, `.modern-col-8`, `.modern-col-12` | 12-column responsive grid |
| **Status Badge** | `.modern-badge-sm` + status modifier | See Status Badges below |

### Status Badges
Always pair `.modern-badge-sm` with the appropriate status class:
- `.status-open` (Green / Open)
- `.status-billing` (Blue / Under Billing)
- `.status-confirmed` (Light Green / Confirmed)
- `.status-tariff` (Amber / Tariff Applied)
- `.status-approval` (Orange / Awaiting Approval)
- `.status-closed` (Gray / Closed)
- `.status-error` (Red / Voided or Error)

### Icons & Font Settings
- MSAP uses Google **Material Symbols Outlined**:
  ```html
  <span class="material-symbols-outlined">icon_name</span>
  ```
- CSS custom properties must be used for theming:
  - Backgrounds: `var(--surface)`, `var(--surface-container-lowest)`
  - Text: `var(--on-surface)`, `var(--on-surface-variant)`
  - Outlines: `var(--outline-variant)`
  - Primary color: `var(--primary)`
  - Error color: `var(--error)`

---

## 6. Assistance & Research Tools (Linear & Graphify)

To ensure accurate understanding of business logic, architectural connections, and requirements, agents should utilize the following available tools:

### Linear Integration (MCP)
* **Purpose:** Issue context, acceptance criteria, requirement tracking, and team notes.
* **Available MCP Tools:** `linear_linear` server tools (`get_issue`, `list_issues`, `list_comments`, `save_comment`, `list_projects`, etc.).
* **Usage:** When a task refers to a ticket or issue key (e.g., `MMSI-123`), use `get_issue` or `list_issues` to inspect acceptance criteria, discussions, and requirements before implementing changes.

### Graphify Knowledge Graph (Local Only)
* **Purpose:** Architectural context, dependency tracing, symbol references, and caller/callee subgraphs.
* **Location:** `graphify-out/`
* **Local Boundary:** Graphify operates strictly locally on your workspace machine. Never attempt to send or leak graph artifacts externally.
* **Focused Queries:** For targeted questions about code structure, relationships, or affected call paths, run:
  ```powershell
  graphify query "<your question>"
  ```
  This returns a focused, scoped subgraph that is significantly smaller and more reliable than unstructured grepping across raw files.
* **Broad Architecture Context:** Inspect `graphify-out/GRAPH_REPORT.md` for high-level architectural summaries and component relationships.

---

## 7. Pre-Completion UI Checklist for Agents

Before completing any task touching MSAP views:

- [ ] All alerts, confirms, and prompts use `ModernAlert` (no `window.alert()` or `window.confirm()`).
- [ ] All tables use `<table class="modern-table ui-table">` and `ModernTable.config(...)`.
- [ ] Searchable form dropdowns use `class="js-modern-select"` and `window.refreshModernSelect($select)` if dynamic.
- [ ] View containers use `.modern-layout.app-ui` and `.modern-container.ui-page`.
- [ ] Cards, headers, inputs, and buttons use MSAP classes (`.modern-card`, `.modern-btn-primary`, etc.).
- [ ] Material icons use `<span class="material-symbols-outlined">...</span>`.
- [ ] Dark and light theme compatibility is preserved by avoiding hardcoded hex colors where theme variables apply.

---

## 8. MSAP Editor Workflow, Hooks & Check Map

* **Implementation Skill:** Use `.agents/skills/msap-editor/SKILL.md` for structured MSAP edits (context → brief → minimal edit → targeted checks + `proof-verify`), either directly or via a fresh-context implementer subagent.
* **Check & Playbook Routing Map:** Consult `.agents/msap-check-map.json` to map modified MSAP modules (`Billing`, `Collection`, `VesselSchedule`, `CustomerOrderSlip`, `TariffRate`, `MasterFile`, `MaritimeReport`, `AuditTrail`, `TugboatWorkspace`, etc.) to their exact `Checks/MSAP` commands, `proof-verify` routes, and `guided-hands.mjs` playbooks.
* **Deterministic Lifecycle Hooks:** `.agents/hooks.json` automatically guards MSAP UI edits (`IBSWeb/Areas/MSAP/**` and `IBSWeb/wwwroot/msap/**`) against prohibited native/raw widget calls (`PreToolUse`) and reminds agents to run `proof-verify` Stage 1 before finishing unverified MSAP code changes (`Stop`). Edits outside MSAP are not blocked.
