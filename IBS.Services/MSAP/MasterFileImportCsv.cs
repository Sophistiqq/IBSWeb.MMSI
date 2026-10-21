using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;

namespace IBS.Services.MSAP
{
    public static class MasterFileImportCsv
    {
        public static string? Kind(string fileName)
        {
            return Path.GetFileName(fileName).ToLowerInvariant() switch
            {
                "bankacct.csv" => "bankacct",
                "customer.csv" => "customer",
                "port.csv" => "port",
                "service.csv" or "services.csv" => "services",
                "tugboatowner.csv" => "tugboatowner",
                "tugmaster.csv" or "master.csv" => "tugmaster",
                "vessel.csv" => "vessel",
                "terminal.csv" => "terminal",
                "tugboat.csv" => "tugboat",
                "principal.csv" => "principal",
                "tariff.csv" => "tariff",
                _ => null
            };
        }

        public static async Task<List<Row>> ReadAsync(IFormFile file, CancellationToken ct)
        {
            string kind = Kind(file.FileName) ?? throw new InvalidDataException("Unsupported master-file name.");
            if (file.Length is <= 0 or > 5 * 1024 * 1024)
            {
                throw new InvalidDataException("Each CSV must contain data and be no larger than 5 MB.");
            }
            using var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(false, true), true);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args => args.Header.Trim().ToUpperInvariant()
            });
            if (!await csv.ReadAsync())
            {
                throw new InvalidDataException("CSV header is missing.");
            }
            csv.ReadHeader();
            var columns = MasterFileImportColumns.For(kind);
            string[] headers = csv.HeaderRecord!.Select(header => header.Trim().Equals("VAT registered", StringComparison.OrdinalIgnoreCase) ? "VATABLE"
                : columns.FirstOrDefault(column => column.Label.Equals(header.Trim(), StringComparison.OrdinalIgnoreCase))?.Key ?? header.Trim().ToUpperInvariant()).ToArray();
            string[] required = kind switch
            {
                "bankacct" => ["CODE", "BANK", "BANKNAME", "ACCOUNTNO", "ACCOUNTNAM"],
                "tariff" => ["RECID", "DATE", "CUSTNO", "PORT", "TERMINAL", "SERVICE", "DISPATCH", "BAF"],
                "customer" => ["RECID", "NUMBER", "NAME", "ADDRESS1", "ADDRESS2", "ADDRESS3", "TIN", "TERMS", "VATABLE", "ACTIVE"],
                "principal" => ["RECID", "AGENT", "NUMBER", "NAME", "ADDRESS1", "ADDRESS2", "ADDRESS3", "TIN", "TERMS", "VATABLE", "ACTIVE"],
                "tugboat" => ["RECID", "NUMBER", "NAME", "COMPANYOWN", "OWNER"],
                "tugmaster" => ["RECID", "EMPNO", "NAME", "ACTIVE"],
                "services" => ["RECID", "NUMBER", "DESC"],
                "vessel" => ["RECID", "NUMBER", "NAME", "TYPE"],
                _ => ["RECID", "NUMBER", "NAME"]
            };
            string[] missing = required.Where(column => column != "RECID"
                && !(kind == "customer" && column == "VATABLE" && headers.Contains("VATTYPE"))).Except(headers).ToArray();
            if (missing.Length > 0 || headers.Distinct().Count() != headers.Length)
            {
                throw new InvalidDataException(missing.Length > 0
                    ? $"Missing columns: {string.Join(", ", missing)}." : "Duplicate column names are not allowed.");
            }
            var rows = new List<Row>();
            while (await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                if (rows.Count >= 10000 || csv.Parser.Count != headers.Length)
                {
                    throw new InvalidDataException($"Row {csv.Parser.Row}: invalid column count or more than 10,000 rows.");
                }
                rows.Add(new Row(csv.Parser.Row, headers.Select((header, index) => (header, value: csv.GetField(index)?.Trim() ?? string.Empty))
                    .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase)));
            }
            if (rows.Count == 0)
            {
                throw new InvalidDataException("CSV has no data rows.");
            }
            return rows;
        }

        public sealed record Row(int Number, IReadOnlyDictionary<string, string> Fields)
        {
            public string Text(string column)
            {
                string value = Fields.GetValueOrDefault(column, string.Empty);
                return value.All(character => character == '-') ? string.Empty : value;
            }

            public string Required(string column)
            {
                string value = Text(column);
                return value.Length > 0 ? value : throw new InvalidDataException($"{column} is missing.");
            }

            public string Code(string column, int width)
            {
                string value = Required(column);
                if (value.Length > width || value.Any(character => character is < '0' or > '9'))
                {
                    throw new InvalidDataException($"{column} must be a numeric code of up to {width} characters.");
                }
                return value.PadLeft(width, '0');
            }

            public bool Flag(string column)
            {
                return Required(column).ToUpperInvariant() switch
                {
                    "TRUE" or "T" or "1" => true,
                    "FALSE" or "F" or "0" => false,
                    _ => throw new InvalidDataException($"{column} must be True or False; blank is unknown.")
                };
            }

            public string PaymentTerms()
            {
                string value = Required("TERMS");
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days is < 0 or > 9999)
                {
                    throw new InvalidDataException("TERMS must contain a nonnegative number of days.");
                }
                return days == 0 ? "COD" : $"{days}D";
            }

            public (string Port, string Terminal) TerminalCode()
            {
                string[] parts = Required("NUMBER").Split('-');
                if (parts.Length != 2 || parts.Any(part => part.Length != 3 || part.Any(character => character is < '0' or > '9')))
                {
                    throw new InvalidDataException("Terminal NUMBER must use port-terminal format, for example 001-002.");
                }
                return (parts[0], parts[1]);
            }
        }
    }
}
