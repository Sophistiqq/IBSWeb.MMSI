# MSAP User Manual

**MSAP** (MMSI Sales and Accounting Program) is a maritime service workflow system covering **Job Order â†’ Dispatch Ticket â†’ Billing â†’ Collection**.

## Workflow Overview

```mermaid
graph LR
    DT[Dispatch Ticket] --> BL[Billing]
    BL --> CL[Collection]
    JO[Job Order] -.->|groups| DT

    classDef primary fill:#e3f2fd,stroke:#1565c0,stroke-width:2px;
    classDef support fill:#fff3e0,stroke:#e65100,stroke-width:2px,stroke-dasharray:5 5;
    class DT,BL,CL primary;
    class JO support;
```

| Step | Description |
|------|-------------|
| **Dispatch Ticket** | Actual service delivery record |
| **Billing** | Invoice generation from delivered tickets |
| **Collection** | Payment recording against billings |
| **Job Order** | Groups dispatch tickets under a planned operation |

## Manual Sections

| #  | Module | File |
|----|--------|------|
| 1  | User Login & Navigation | [User Login](user-login) |
| 2  | Job Order Management | [Job Order](job-order) |
| 3  | Dispatch Ticket Operations | [Dispatch Ticket](dispatch-ticket) |
| 4  | Billing & Invoicing | [Billing](billing) |
| 5  | Collection & Payment | [Collection](collection) |
| 6  | Master Files | [Master Files](master-files) |
| 7  | Administration (Roles & Access) | [Administration](admin) |
| 8  | Import & Export | [Import & Export](import-export) |
| 9  | Reports | [Reports](reports) |
