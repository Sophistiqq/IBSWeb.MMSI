using IBS.Models.MSAP.ViewModels;

namespace IBS.Services.MSAP
{
    public static class MasterFileImportColumns
    {
        public static string[] Kinds => ["bankacct", "customer", "port", "services", "tugboatowner", "tugmaster", "vessel", "terminal", "tugboat", "principal", "tariff"];

        public static List<MasterFileImportColumn> For(string kind)
        {
            string[] columns = kind switch
            {
                "bankacct" => ["CODE|Bank account code", "BANK|Bank", "BANKNAME|Branch", "ACCOUNTNO|Account number", "ACCOUNTNAM|Account name"],
                "tariff" => ["DATE|Effective date", "CUSTNO|Customer code", "PORT|Port code", "TERMINAL|Port-terminal code", "SERVICE|Service code", "DISPATCH|Tariff amount", "BAF|Final BAF amount", "TYPE|Legacy vessel type", "VESSEL|Legacy vessel code"],
                "customer" => ["NUMBER|Customer code", "NAME|Customer name", "ADDRESS1|Address line 1", "ADDRESS2|Address line 2", "ADDRESS3|Address line 3", "ZIP|ZIP code", "TIN|TIN", "BUSINESS|Business style", "TERMS|Payment term days", "VATTYPE|VAT type", "VATABLE|Legacy VAT registered", "WHT|Withholding tax (2307)", "WVAT|Withholding VAT (2306)", "COSDISCOUNT|Default COS discount (%)", "ACTIVE|Active"],
                "principal" => ["AGENT|Customer code", "NUMBER|Principal code", "NAME|Principal name", "ADDRESS1|Address line 1", "ADDRESS2|Address line 2", "ADDRESS3|Address line 3", "TIN|TIN", "BUSINESS|Business style", "TERMS|Payment term days", "VATABLE|VAT registered", "ACTIVE|Active", "LANDLINE1|Landline 1", "LANDLINE2|Landline 2", "MOBILE1|Mobile 1", "MOBILE2|Mobile 2"],
                "tugmaster" => ["EMPNO|Captain code", "NAME|Captain name", "ACTIVE|Active"],
                "services" => ["NUMBER|Service code", "DESC|Service name"],
                "vessel" => ["NUMBER|Vessel code", "NAME|Vessel name", "TYPE|Vessel type"],
                "terminal" => ["NUMBER|Port-terminal code", "NAME|Terminal name", "IOC|IOC terminal"],
                "tugboat" => ["NUMBER|Tugboat code", "NAME|Tugboat name", "COMPANYOWN|Company owned", "OWNER|Owner code", "PORT|Home port code"],
                "port" => ["NUMBER|Port code", "NAME|Port name"],
                "tugboatowner" => ["NUMBER|Owner code", "NAME|Owner name"],
                _ => throw new InvalidDataException("Unsupported master-file kind.")
            };
            return columns.Append("RECID|Legacy source ID (optional)").Select(value =>
            {
                string[] parts = value.Split('|');
                return new MasterFileImportColumn
                {
                    Key = parts[0], Label = parts[1], Choices = parts[0] is "ACTIVE" or "VATABLE" or "COMPANYOWN" or "WHT" or "WVAT" or "IOC" ? ["True", "False"]
                        : parts[0] == "VATTYPE" ? ["Vatable", "Exempt", "Zero-Rated"] : kind == "vessel" && parts[0] == "TYPE" ? ["L", "F"] : [],
                    Required = parts[0] is "NUMBER" or "NAME" or "CODE" or "DESC" or "EMPNO" or "ACTIVE" or "COMPANYOWN" or "TERMS" or "AGENT"
                        or "WHT" or "WVAT" or "COSDISCOUNT" or "IOC" or "CUSTNO" or "TERMINAL" or "SERVICE" or "DATE" or "DISPATCH" or "BAF"
                        or "BANK" or "ACCOUNTNO" or "ACCOUNTNAM" || kind == "customer" && parts[0] is "TIN" or "ZIP" || kind == "vessel" && parts[0] == "TYPE"
                        || kind == "tariff" && parts[0] == "PORT",
                    Note = Guidance(kind, parts[0])
                };
            }).ToList();
        }

        private static string Guidance(string kind, string key)
        {
            return key switch
            {
                "WHT" => "Confirm True or False for withholding tax (2307). Do not infer this from VAT registration; do not assume all customers withhold.",
                "WVAT" => "Confirm True or False for withholding VAT (2306), independently of withholding tax and VAT type.",
                "VATTYPE" => "Confirm Vatable, Exempt or Zero-Rated per customer. Legacy VATABLE=False does not distinguish Exempt from Zero-Rated.",
                "VATABLE" when kind == "customer" => "Legacy indicator only. Complete VAT type for the confirmed classification; withholding is supplied separately.",
                "VATABLE" or "ACTIVE" or "COMPANYOWN" or "IOC" => "Confirm True or False. Blank means unknown, not False.",
                "COSDISCOUNT" => "Confirm the default COS discount from 0 to 100 percent. Enter 0 explicitly for no discount.",
                "ZIP" => "Supply the real ZIP code. Do not enter invented zeros to fill a missing value.",
                "TIN" => "Confirm the registered TIN/branch as supplied by Accounting. Preserve leading zeros; do not invent a replacement TIN. Existing Customer-form format differences need review.",
                "ADDRESS1" or "ADDRESS2" or "ADDRESS3" => "Supply the billing address. At least one address line is needed; leave unused extra lines blank.",
                "TERMS" => "Payment term days: 0 means COD. Confirm missing terms; the corresponding shared IBS payment term must exist.",
                "OWNER" => "Use the owner master code, not a database ID. External tugboats require an owner; 000 is not an external provider.",
                "PORT" => "Supply the confirmed port master code. Tugboat home port must not be inferred from a sales-report transaction port.",
                "NUMBER" when kind == "terminal" => "Port-terminal code, for example 001-002. The prefix must match an existing port code.",
                "AGENT" or "CUSTNO" => "Use the service customer master code, preserving leading zeros.",
                "DISPATCH" or "BAF" => "Supply a nonnegative amount with at most four decimal places. Blank is missing; 0 is explicit. Imported amounts are staged for rate approval.",
                "DATE" => "Effective date in yyyy-MM-dd format. Do not replace historical dates merely to clear validation.",
                "RECID" => "Optional legacy source ID for traceability. Never substitute this for a business code or current database ID.",
                _ => "Confirm this master value. Keep business codes as text and preserve leading zeros."
            };
        }

        public static bool Missing(MasterFileImportColumn column, MasterFileImportCsv.Row row, string kind)
        {
            if (row.Text(column.Key).Length > 0)
            {
                return false;
            }
            return column.Required || column.Key == "VATTYPE" && row.Text("VATABLE").ToUpperInvariant() is not ("TRUE" or "T" or "1")
                || column.Key == "VATABLE" && kind == "principal" || column.Key == "PORT" && kind == "tugboat"
                || column.Key == "ADDRESS1" && kind is "customer" or "principal" && new[] { "ADDRESS1", "ADDRESS2", "ADDRESS3" }.All(key => row.Text(key).Length == 0)
                || column.Key == "OWNER" && row.Text("COMPANYOWN").ToUpperInvariant() is "FALSE" or "F" or "0";
        }
    }
}
