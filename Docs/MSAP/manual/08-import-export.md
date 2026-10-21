# 8. Import & Export

## CSV Import (Legacy Data Migration)

**Path:** MSAP > Import (requires `ManageMsapImport` access)

![Import page](/msap/docs-images/import-export/import-page.png)

Imports data from CSV files into all MSAP tables. Used for initial data migration from legacy systems.

### Import Order
Data is imported in dependency order (5 levels):

| Level | Tables |
|-------|--------|
| **1** | Bank Accounts, Customers, Ports, Services (Maritime), Tugboat Owners, Tug Masters, Vessels |
| **2** | Terminals, Tugboats, Principals |
| **3** | Tariff Rates, Collections, Collection Billings |
| **4** | Billings |
| **5** | Dispatch Tickets |

### How to Import
1. Go to **MSAP > Import**
2. Upload CSV files for each entity (or use bulk file upload)
3. Click **Import**
4. Results show: rows imported, errors, and warnings

### Reset Data
- **DANGER:** The **Reset** button **truncates all MSAP tables** â€” irreversible
- Only use for full re-import scenarios

## Export to Excel

### Master File Exports
- **Customers, Suppliers** â€” Export from their respective list pages
- Select records or export all
- Generates password-protected Excel files

### Master File Excel Generator
**Path:** MSAP > Master Files > Excel

Generates Excel for: Customers, Suppliers, Bank Accounts, Employees

### Billing Print
**Path:** MSAP > Billing > Print

Dot-matrix formatted Excel with VAT/WHT calculations.

## Tips

- CSV files must match the expected column headers (legacy format)
- Import validates data â€” errors are reported per row
- Exported Excel files may be password-protected
- Use **Export** for periodic data backups
