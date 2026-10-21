# 3. Billing & Invoicing

**Billing** generates invoices from approved Dispatch Tickets. It handles rate application, adjustments, and undocumented vessel billing.

## Workflow State

```mermaid
graph LR
    ForPosting[For Posting] -->|post to GL| ForCollection[For Collection]
    ForCollection -->|full payment| Collected
    ForCollection -->|reverse| ForPosting

    classDef pending fill:#e3f2fd,stroke:#1565c0,stroke-width:2px;
    classDef done fill:#e8f5e9,stroke:#2e7d32,stroke-width:2px;
    class ForPosting,ForCollection pending;
    class Collected done;
```

### State Descriptions

| State | Meaning |
|-------|---------|
| **For Posting** | Created but not yet posted to accounting |
| **For Collection** | Posted, awaiting payment |
| **Collected** | Payment received |

## Pages

### Billing List (Index)

![Billing list](/msap/docs-images/billing/list.png)

- **Path:** MSAP > Billing
- **Filters:** Status buttons (ALL, FOR POSTING, FOR COLLECTION, COLLECTED) + Date range
- **Table columns:** Date, Billing #, Amount, Customer, Port - Terminal, Vessel, Status, Actions

### Create Billing

![Create Billing form](/msap/docs-images/billing/create.png)

- **Form layout:** Two-column grid with AJAX submission
- **Left column:**
  - Customer selection
  - Job Order selection (auto-filters available tickets)
  - Dispatch Ticket selection (shows unbilled tickets for the selected customer/order)
  - Auto-calculated amounts from selected tickets
- **Right column:**
  - Billing summary
  - Total amounts
- **Toggle:** Undocumented vessel badge for non-registered vessels

### Edit Billing

- **Restriction:** Only **For Posting** billings can be edited
- Can add/remove dispatch tickets
- Adjust amounts

### Preview

![Billing preview](/msap/docs-images/billing/preview.png)

- Read-only view of billing with all associated tickets and tugboats
- Shows computed VAT, withholding tax, and net amounts

### Print

- Generates a dot-matrix formatted Excel file
- Includes: VAT computation, Withholding Tax (WHT), line items per ticket

### Post Billing

- Posts billing to Sales Book
- Moves billing to **For Collection** status
- This is a one-way operation (cannot be undone)

## Key Actions

| Action | Description |
|--------|-------------|
| Create | New billing from unbilled tickets |
| Edit | Modify For Posting billing |
| Post | Post to accounting (For Posting â†’ For Collection) |
| Delete | Remove a billing (For Posting only) |
| Preview | View full billing details |
| Print | Export billing as formatted Excel |

## Tips

- Only **For Billing** status tickets appear in the ticket selection
- **Undocumented vessels** can be billed without a registered vessel record
- Posting is irreversible â€” verify all amounts before posting
- The Print output is formatted for dot-matrix printers (legacy format)
