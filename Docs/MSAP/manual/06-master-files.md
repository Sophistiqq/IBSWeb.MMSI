# 6. Master Files

Master files are the reference data that powers all workflows. They must be set up before transactions can be created.

## Maritime Master Files

These are managed under **MSAP** and require `ManageMaritimeMasterFile` access.

### Vessels

![Vessels list](/msap/docs-images/master-files/vessels.png)

- Register seagoing vessels used in service operations
- **Fields:** Vessel #, Vessel Name, Vessel Type
- **Table columns:** Vessel #, Vessel Name, Vessel Type, Actions

### Ports
- Define ports where services are performed
- Used in Job Orders and Dispatch Tickets

### Principals
- Define principals (shipping lines / client companies)
- Linked to Customers in billing workflows

### Tugboats
- Register tugboat fleet
- **Fields:** Tugboat name, type, owner, status
- Used in ticket assignment and monitoring

### Tug Masters
- Register tugboat captains/operators
- Assigned to Dispatch Tickets

### Tugboat Owners
- Register tugboat ownership entities
- Used in financial reporting (AP Ledger)

### Terminals
- Specific berths/docks within a Port
- **Cascading:** Depends on selected Port
- Used in Job Orders and Dispatch Tickets

### Maritime Services (Activity/Service)
- Types of services offered (e.g., berthing, unberthing, shifting, escort)
- Used in Dispatch Ticket creation

## Accounting Master Files

### Customers
- Client companies who receive services
- **Features:** Activate/Deactivate, TIN validation
- Linked to Vessels, Ports, and Payment Terms
- **Status:** Active / Inactive

### Suppliers
- Vendor companies
- **Features:** Registration document upload, Activate/Deactivate
- Linked to Payment Terms

### Payment Terms
- Read-only list of payment schedules maintained in Filpride (e.g., 30 days, 60 days)
- Shared by MSAP Customers, Suppliers and billing due-date calculations

### Bank Accounts
- Company bank accounts for payment processing
- **Restriction:** MSAP Admin or SuperAdmin only

### Companies
- Internal company entities within the organization
- **Features:** Activate/Deactivate

### Employees
- Staff records linked to user accounts
- Used for attribution and reporting

### Tariff Rates

![Tariff Rates list](/msap/docs-images/master-files/tariff-rates.png)

- Pre-defined rate cards: Dispatch Rate, BAF Rate per customer/port/terminal
- **Upsert behavior:** Creates or updates matching records
- Used by **Set Tariff** in Dispatch Tickets

## Common Actions (per Master File)

| Action | Description |
|--------|-------------|
| Index | List all records (server-side DataTable) |
| Create | New record |
| Edit | Modify existing record |
| Delete | Remove record |
| Activate/Deactivate | Toggle active status (Customer, Supplier, Company) |

## Tips

- Set up **Maritime Master Files** first (Vessels, Ports, Terminals) before creating Job Orders
- Set up **Customers** and **Tariff Rates** before billing
- Cascading dropdowns: Port â†’ Terminal
- Use the **Export** feature on Customer/Supplier lists to download as Excel
