# 2. Dispatch Ticket Operations

A **Dispatch Ticket** records actual maritime service delivery â€” tugboat assignments, service times, crew details, and media attachments.

## Workflow State

Dispatch Tickets are created directly under a Job Order.

```mermaid
graph LR
    %% Entry points
    Create[Create Dispatch Ticket] --> ForTariff[For Tariff]
    %% Main flow
    ForTariff -->|set tariff| ForApproval[For Approval]
    ForApproval -->|approve| ForBilling[For Billing]
    ForApproval -->|disapprove| Disapproved
    Disapproved -->|edit tariff| ForApproval
    ForBilling -->|post billing| Billed
    ForBilling -->|revoke approval| ForApproval
    Billed -->|reverse billing| ForBilling

    %% Edit resets tariff on critical change
    ForApproval -->|edit critical fields| ForTariff

    %% Soft delete / restore
    ForTariff -->|delete| Deleted
    Disapproved -->|delete| Deleted
    Deleted -->|restore| ForTariff

    classDef main fill:#e3f2fd,stroke:#1565c0,stroke-width:2px;
    classDef billed fill:#e8f5e9,stroke:#2e7d32,stroke-width:2px;
    classDef disapproved fill:#fff3e0,stroke:#e65100,stroke-width:2px;
    classDef deleted fill:#eceff1,stroke:#546e7a,stroke-width:2px;
    class ForTariff,ForApproval,ForBilling main;
    class Disapproved disapproved;
    class Billed billed;
    class Deleted deleted;
```

### State Descriptions

| State | Meaning |
|-------|---------|
| **For Tariff** | Ready for rate assignment (includes tickets with incomplete service times) |
| **For Approval** | Tariff set, waiting for approval |
| **Disapproved** | Tariff rejected, can be revised |
| **For Billing** | Approved, ready to be billed |
| **Billed** | Included in a billing statement |
| **Deleted** | Soft-deleted, can be restored |

## Pages

### Dispatch Tickets List (Index)

![Dispatch Ticket list](/msap/docs-images/dispatch-ticket/list.png)

- **Path:** MSAP > Dispatch Tickets
- **Filters:** Status filter buttons (ALL, FOR TARIFF, FOR APPROVAL, DISAPPROVED, FOR BILLING, BILLED, DELETED) + Date range
- **Table columns:** Date, COS #, Ticket #, Start, End, Activity/Service, Port - Terminal, Tugboat, Customer, Status, Actions

### Create Dispatch Ticket

![Create Dispatch Ticket form](/msap/docs-images/dispatch-ticket/create.png)

- **Form layout:** Two-column grid
- **Left column (col-8):**
  - Reference Information: Date, Job Order # (auto-populates customer/vessel/port/terminal)
  - Service Details: Activity/Service, Start Date/Time, End Date/Time, Tugboat
  - Attachments: File upload (image, video)
- **Right column (col-4):** Status, COS #

### Set Tariff / Edit Tariff

![Set Tariff form](/msap/docs-images/dispatch-ticket/tariff.png)

- For tickets in **For Tariff** status
- Fields: Dispatch Rate, BAF Rate, discounts, charge types
- Supports batch tariff setting from the list view

### Preview

- Read-only view of ticket details
- Media viewer for attached images/videos

## Key Actions

| Action | Description |
|--------|-------------|
| Create | New service ticket (from scratch or via Job Order) |
| Edit | Modify ticket details (status-dependent) |
| Set Tariff | Assign rates to a For Tariff ticket |
| Edit Tariff | Modify existing tariff rates |
| Approve Tariff | Approve a tariff (For Approval â†’ For Billing) |
| Disapprove Tariff | Reject tariff with reason (For Approval â†’ Disapproved) |
| Revoke Approval | Pull a For Billing ticket back to For Approval |
| Batch Approve | Approve multiple tariffs at once |
| Batch Set Tariff | Set rates for multiple tickets at once |
| Delete | Soft-delete a ticket (Pending / For Tariff / Disapproved only) |
| Restore | Restore a deleted ticket |
| Change Status | Generic status transition |

## Tips

- Tickets move through states in order â€” you cannot skip steps
- **Batch operations** save time when processing multiple tickets
- Media attachments show previews in the ticket detail view
- Use the filter buttons at the top to quickly find tickets by status
