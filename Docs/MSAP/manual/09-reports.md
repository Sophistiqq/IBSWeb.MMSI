# 9. Reports

Reports are generated as **Excel files** using EPPlus. Available under **MSAP > Reports** (requires `ViewMaritimeReport` access).

## Available Reports

### Dispatch for Billing

![Dispatch for Billing report](/msap/docs-images/reports/dispatch-billing.png)

- **Purpose:** List of dispatch tickets ready for billing
- **Parameters:** Date range (From â€” To)
- **Output:** 22 columns covering dispatch and billing details
- Includes: Dispatch Rate, BAF Rate, Bill Amount, Discount, Net Amount
- Use: Pre-billing audit

### Dispatch Ticket Summary

![Dispatch Ticket Summary report](/msap/docs-images/reports/dispatch-summary.png)

- **Purpose:** Comprehensive summary of dispatched services
- **Parameters:** Date range (From â€” To)
- **Output:** 35 columns with color-coded sections
- Sections: Dispatch Info (peach), Billing Info (cyan), Collection Info (lavender)
- Includes SUM formulas for totals
- Use: Operational overview

### Sales Summary (AR Monitoring)

![Sales Summary report](/msap/docs-images/reports/sales-summary.png)

- **Purpose:** Accounts Receivable monitoring report
- **Parameters:** Month, Year
- **Output:** Dynamic columns based on tugboats, owners, and customers
- **7 color-coded sections:**
  1. Trip Details
  2. PNL Use
  3. AP Ledger
  4. AR Ledger
  5. Number of Assists (IOC/Outside Ã— Local/Foreign)
  6. Number of Tending
  7. Tending Hours
- Per-row data: billing, collection, deposit info, EWT, VAT, balances
- Use: Monthly AR monitoring and management reporting

## Tips

- All reports download as `.xlsx` files
- Date format: Philippine Time (PHT)
- Reports respect user access permissions â€” only accessible with `ViewMaritimeReport`
- Large date ranges may take longer to generate
