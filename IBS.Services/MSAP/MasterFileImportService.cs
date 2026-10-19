using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.Services.MSAP
{
    public class MasterFileImportService(MsapDbContext db, IUnitOfWork work, RateUploadService rates)
    {
        public async Task PopulateAsync(MasterFileImportViewModel model, CancellationToken ct)
        {
            var ports = await db.MsapPorts.AsNoTracking().OrderBy(port => port.PortName)
                .Select(port => new SelectListItem { Value = port.PortNumber, Text = port.PortNumber + " - " + port.PortName }).ToListAsync(ct);
            model.Ports = model.Ports.Concat(ports).DistinctBy(port => port.Value).OrderBy(port => port.Text).ToList();
        }

        public async Task ProcessAsync(MasterFileImportViewModel model, string userId, bool import, CancellationToken ct)
        {
            string? reviewed = model.ReviewedFingerprint;
            model.ReviewedFingerprint = null;
            var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 100)
            {
                model.Errors.Add("An active account with a full name of up to 100 characters is required.");
                return;
            }
            if (model.Files.Count > 50 || model.Files.Sum(file => file.Length) > 20 * 1024 * 1024)
            {
                model.Errors.Add("Select no more than 50 files totaling 20 MB.");
                return;
            }
            var sources = new Dictionary<string, (string File, List<MasterFileImportCsv.Row> Rows)>();
            foreach (var file in model.Files)
            {
                string name = Path.GetFileName(file.FileName);
                if (Path.GetExtension(name).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        foreach (var document in await MasterFileImportWorkbook.ReadAsync(file, ct))
                        {
                            if (!sources.TryAdd(document.Kind, (name + " / " + document.Kind, document.Rows)))
                            {
                                model.Errors.Add($"{name}: more than one file/sheet was supplied for {document.Kind}.");
                            }
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or CsvHelperException or IOException)
                    {
                        model.Errors.Add($"{name}: {(ex is InvalidDataException ? ex.Message : "The workbook could not be read. Use an unencrypted .xlsx with the supplied headers.")}");
                    }
                    continue;
                }
                string? kind = MasterFileImportCsv.Kind(name);
                if (kind == null)
                {
                    model.IgnoredFiles.Add(name);
                    continue;
                }
                if (sources.ContainsKey(kind))
                {
                    model.Errors.Add($"{name}: more than one file was supplied for {kind}.");
                    continue;
                }
                try
                {
                    sources.Add(kind, (name, await MasterFileImportCsv.ReadAsync(file, ct)));
                }
                catch (Exception ex) when (ex is InvalidDataException or CsvHelperException or DecoderFallbackException)
                {
                    model.Errors.Add($"{name}: {(ex is InvalidDataException ? ex.Message : "Invalid UTF-8 CSV data. Check quoting and column counts.")}");
                }
            }
            if (sources.Count == 0)
            {
                model.Errors.Add("Select at least one supported master CSV or populated master-file workbook.");
            }
            foreach (var source in sources)
            {
                model.Documents.Add(new MasterFileImportDocument
                {
                    Kind = source.Key, FileName = source.Value.File, Columns = MasterFileImportColumns.For(source.Key),
                    Rows = source.Value.Rows.Select(row => new MasterFileImportRow
                    {
                        Row = row.Number, Fields = row.Fields.ToDictionary(pair => pair.Key, pair => pair.Value)
                    }).ToList()
                });
            }
            if (model.Errors.Count > 0)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(model.EditedFilesJson))
            {
                try
                {
                    var edits = JsonSerializer.Deserialize<List<MasterFileImportDocument>>(model.EditedFilesJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("The editor data is missing.");
                    ApplyEdits(model.Documents, edits);
                    foreach (var document in model.Documents)
                    {
                        sources[document.Kind] = (document.FileName, document.Rows.Select(row => new MasterFileImportCsv.Row(row.Row, row.Fields)).ToList());
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    model.Errors.Add(ex is InvalidDataException ? ex.Message : "The editor data is invalid. Reload the source files and validate again.");
                    return;
                }
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Serialize this import path; the fingerprint also rechecks ordinary master-file edits.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE msap.customers, msap.mmsi_ports, msap.mmsi_services, msap.mmsi_tugboat_owners, msap.mmsi_tug_masters, msap.mmsi_vessels, msap.mmsi_terminals, msap.mmsi_tugboats, msap.mmsi_principals, msap.bank_accounts, msap.mmsi_rate_uploads IN SHARE ROW EXCLUSIVE MODE", ct);
            try
            {
                var customers = await db.Customers.Where(item => item.Company == MsapConstants.Company_MMSI).ToListAsync(ct);
                var banks = await db.BankAccounts.Where(item => item.Company == MsapConstants.Company_MMSI).ToListAsync(ct);
                var ports = await db.MsapPorts.ToListAsync(ct);
                var services = await db.MsapServices.ToListAsync(ct);
                var owners = await db.MsapTugboatOwners.ToListAsync(ct);
                var masters = await db.MsapTugMasters.ToListAsync(ct);
                var vessels = await db.MsapVessels.ToListAsync(ct);
                var terminals = await db.MsapTerminals.Include(item => item.Port).ToListAsync(ct);
                var tugs = await db.MsapTugboats.Include(item => item.TugboatOwner).Include(item => item.Port).ToListAsync(ct);
                var principals = await db.MsapPrincipals.Include(item => item.Customer).ToListAsync(ct);
                var terms = (await work.Terms.GetAllAsync(null, ct)).ToList();
                var storedCounts = new Dictionary<string, int>
                {
                    ["bankacct"] = banks.Count, ["customer"] = customers.Count, ["port"] = ports.Count, ["services"] = services.Count,
                    ["tugboatowner"] = owners.Count, ["tugmaster"] = masters.Count, ["vessel"] = vessels.Count,
                    ["terminal"] = terminals.Count, ["tugboat"] = tugs.Count, ["principal"] = principals.Count,
                    ["tariff"] = await db.MsapTariffRates.CountAsync(ct)
                };
                model.Availability = MasterFileImportColumns.Kinds.Select(kind => new MasterFileImportAvailability
                {
                    File = kind + ".csv", SuppliedRows = sources.TryGetValue(kind, out var source) ? source.Rows.Count : 0, StoredRows = storedCounts[kind]
                }).ToList();
                int rateDraftCount = await db.MsapRateUploads.CountAsync(update => update.Company == MsapConstants.Company_MMSI, ct);
                string baseline = JsonSerializer.Serialize(db.ChangeTracker.Entries().OrderBy(entry => entry.Metadata.Name)
                    .ThenBy(entry => JsonSerializer.Serialize(entry.Properties.Where(property => property.Metadata.IsPrimaryKey()).Select(property => property.CurrentValue)))
                    .Select(entry => new { Type = entry.Metadata.Name, Values = entry.Properties.OrderBy(property => property.Metadata.Name)
                        .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue) }));
                string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    baseline, rateDraftCount, Terms = terms.OrderBy(term => term.TermsCode).Select(term => new { term.TermsCode, term.NumberOfDays, term.NumberOfMonths }), model.UpdateExisting, model.NewTugboatPortCode, model.NonVatableTaxType,
                    Sources = sources.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, pair.Value.File, pair.Value.Rows })
                }))));
                var seenRecIds = new HashSet<string>();
                var seenCodes = new HashSet<string>();
                var stagedRates = new List<(MasterFileImportCsv.Row Row, Customer Customer, Port Port, Terminal Terminal, Service Service, DateOnly Date, decimal Tariff, decimal Baf)>();
                foreach (string kind in MasterFileImportColumns.Kinds)
                {
                    if (!sources.TryGetValue(kind, out var source))
                    {
                        continue;
                    }
                    foreach (var row in source.Rows)
                    {
                        var item = new MasterFileImportItem { FileName = source.File, Row = row.Number };
                        model.Items.Add(item);
                        try
                        {
                            string legacyId = row.Text("RECID");
                            if (legacyId.Length > 10 || legacyId.Length > 0 && !seenRecIds.Add(kind + "|" + legacyId))
                            {
                                throw new InvalidDataException("RECID is too long or repeated in this file.");
                            }
                            switch (kind)
                            {
                                case "tariff":
                                    string customerCode = row.Code("CUSTNO", 4);
                                    string portCode = row.Code("PORT", 3);
                                    string serviceCode = row.Code("SERVICE", 3);
                                    var terminalParts = new MasterFileImportCsv.Row(row.Number, new Dictionary<string, string> { ["NUMBER"] = row.Required("TERMINAL") }).TerminalCode();
                                    if (terminalParts.Port != portCode)
                                    {
                                        throw new InvalidDataException("TERMINAL does not belong to PORT.");
                                    }
                                    var rateCustomer = customers.SingleOrDefault(value => value.CustomerCode == customerCode) ?? throw new InvalidDataException($"CUSTNO {customerCode} was not found.");
                                    var ratePort = ports.SingleOrDefault(value => value.PortNumber == portCode) ?? throw new InvalidDataException($"PORT {portCode} was not found.");
                                    var rateTerminal = terminals.SingleOrDefault(value => value.Port.PortNumber == portCode && value.TerminalNumber == terminalParts.Terminal)
                                        ?? throw new InvalidDataException($"TERMINAL {row.Text("TERMINAL")} was not found.");
                                    var rateService = services.SingleOrDefault(value => value.ServiceNumber == serviceCode) ?? throw new InvalidDataException($"SERVICE {serviceCode} was not found.");
                                    if (!DateOnly.TryParseExact(row.Required("DATE"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveDate))
                                    {
                                        throw new InvalidDataException("DATE must use yyyy-MM-dd.");
                                    }
                                    if (row.Text("VESSEL").Length > 0)
                                    {
                                        throw new InvalidDataException("VESSEL-specific rates require a supported rate key; do not import them as general rates.");
                                    }
                                    decimal tariff = ReadAmount(row, "DISPATCH");
                                    decimal baf = ReadAmount(row, "BAF");
                                    foreach (decimal amount in new[] { tariff, baf })
                                    {
                                        var errors = RateUploadService.ValidateRows([new RateUploadRowViewModel
                                        {
                                            CustomerId = 1, PortId = 1, TerminalId = 1, ServiceId = 1, EffectiveDate = effectiveDate, Amount = amount
                                        }]);
                                        if (errors.Count > 0)
                                        {
                                            throw new InvalidDataException(string.Join(" ", errors));
                                        }
                                    }
                                    item.Code = $"{customerCode}/{row.Text("TERMINAL")}/{serviceCode}/{effectiveDate:yyyy-MM-dd}";
                                    if (!seenCodes.Add("tariff|" + item.Code))
                                    {
                                        throw new InvalidDataException("Duplicate dated rate key. Resolve any vessel-type differences before staging.");
                                    }
                                    item.Name = rateCustomer.CustomerName + " / " + rateService.ServiceName;
                                    item.Action = "Stage rates";
                                    item.Changes = $"Tariff {tariff:N4}; final BAF {baf:N4}. Two independent drafts; live rates remain unchanged until approval.";
                                    item.Warning = row.Text("TYPE").Length > 0 ? $"Legacy vessel type {row.Text("TYPE")} retained as provenance. Current lookup is customer/terminal/service/date; review this scope before approval." : null;
                                    stagedRates.Add((row, rateCustomer, ratePort, rateTerminal, rateService, effectiveDate, tariff, baf));
                                    break;
                                case "bankacct":
                                    item.Code = row.Required("CODE");
                                    item.Name = row.Required("ACCOUNTNAM");
                                    Apply(banks, bank => bank.BankAccountCode, new BankAccount
                                    {
                                        BankAccountCode = item.Code, Bank = row.Required("BANK"), Branch = row.Text("BANKNAME"),
                                        AccountNo = row.Required("ACCOUNTNO"), AccountName = item.Name, Company = MsapConstants.Company_MMSI,
                                        CreatedBy = actor.Name.Trim(), CreatedDate = DateTimeHelper.GetCurrentPhilippineTime()
                                    }, "Bank,Branch,AccountNo,AccountName", model, item, seenCodes);
                                    break;
                                case "customer":
                                    item.Code = row.Code("NUMBER", 4);
                                    item.Name = row.Required("NAME");
                                    var existingCustomer = customers.FirstOrDefault(customer => customer.CustomerCode == item.Code);
                                    string vatType = row.Text("VATTYPE");
                                    if (vatType.Length == 0)
                                    {
                                        bool vatable = row.Flag("VATABLE");
                                        if (!vatable && model.NonVatableTaxType is not (MsapConstants.VatType_Exempt or MsapConstants.VatType_ZeroRated))
                                        {
                                            throw new InvalidDataException("Supply VATTYPE per customer, or choose how VATABLE=False maps to VAT type. This does not specify withholding.");
                                        }
                                        vatType = vatable ? MsapConstants.VatType_Vatable : model.NonVatableTaxType!;
                                    }
                                    if (vatType is not (MsapConstants.VatType_Vatable or MsapConstants.VatType_Exempt or MsapConstants.VatType_ZeroRated))
                                    {
                                        throw new InvalidDataException("VATTYPE must be Vatable, Exempt or Zero-Rated.");
                                    }
                                    string address = string.Join(" ", new[] { row.Text("ADDRESS1"), row.Text("ADDRESS2"), row.Text("ADDRESS3") }.Where(value => value.Length > 0));
                                    string customerTerms = ResolveTerms(row.PaymentTerms(), terms);
                                    Apply(customers, customer => customer.CustomerCode, new Customer
                                    {
                                        CustomerCode = item.Code, CustomerName = item.Name, Address1 = row.Text("ADDRESS1"), Address2 = row.Text("ADDRESS2"),
                                        Address3 = row.Text("ADDRESS3"), CustomerAddress = address, CustomerTin = row.Text("TIN"), BusinessStyle = row.Text("BUSINESS"),
                                        CustomerTerms = customerTerms, VatType = vatType,
                                        WithHoldingTax = FlagOrExisting(row, "WHT", existingCustomer?.WithHoldingTax),
                                        WithHoldingVat = FlagOrExisting(row, "WVAT", existingCustomer?.WithHoldingVat),
                                        DefaultCosDiscountPercent = DiscountOrExisting(row, existingCustomer?.DefaultCosDiscountPercent),
                                        IsActive = row.Flag("ACTIVE"), Company = MsapConstants.Company_MMSI, CustomerType = "Industrial", Type = "Documented",
                                        ZipCode = row.Text("ZIP").Length > 0 ? row.Text("ZIP") : existingCustomer?.ZipCode ?? string.Empty,
                                        CreatedBy = actor.Name.Trim(), CreatedDate = DateTimeHelper.GetCurrentPhilippineTime(), MsapRecId = legacyId
                                    }, "CustomerName,Address1,Address2,Address3,CustomerAddress,CustomerTin,ZipCode,BusinessStyle,CustomerTerms,VatType,WithHoldingTax,WithHoldingVat,DefaultCosDiscountPercent,IsActive,MsapRecId", model, item, seenCodes);
                                    if (address.Length == 0 || !Regex.IsMatch(row.Text("TIN"), @"^\d{3}-\d{3}-\d{3}-\d{5}$"))
                                    {
                                        item.Warning = "Address/TIN is missing or the TIN differs from the current Customer form format. Source text is retained without a generated identity; review the Customer master before financial use.";
                                    }
                                    break;
                                case "port":
                                    item.Code = row.Code("NUMBER", 3);
                                    item.Name = row.Required("NAME");
                                    Apply(ports, port => port.PortNumber, new Port { PortNumber = item.Code, PortName = item.Name,
                                        MsapRecId = legacyId },
                                        "PortName,MsapRecId", model, item, seenCodes);
                                    break;
                                case "services":
                                    item.Code = row.Code("NUMBER", 3);
                                    item.Name = row.Required("DESC");
                                    Apply(services, service => service.ServiceNumber, new Service { ServiceNumber = item.Code, ServiceName = item.Name, MsapRecId = legacyId },
                                        "ServiceName,MsapRecId", model, item, seenCodes);
                                    break;
                                case "tugboatowner":
                                    item.Code = row.Code("NUMBER", 3);
                                    item.Name = row.Required("NAME");
                                    Apply(owners, owner => owner.TugboatOwnerNumber, new TugboatOwner { TugboatOwnerNumber = item.Code, TugboatOwnerName = item.Name, MsapRecId = legacyId },
                                        "TugboatOwnerName,MsapRecId", model, item, seenCodes);
                                    break;
                                case "tugmaster":
                                    item.Code = row.Required("EMPNO");
                                    item.Name = row.Required("NAME");
                                    Apply(masters, master => master.TugMasterNumber, new TugMaster { TugMasterNumber = item.Code, TugMasterName = item.Name, IsActive = row.Flag("ACTIVE"), MsapRecId = legacyId },
                                        "TugMasterName,IsActive,MsapRecId", model, item, seenCodes);
                                    break;
                                case "vessel":
                                    item.Code = row.Code("NUMBER", 4);
                                    item.Name = row.Required("NAME");
                                    string vesselType = row.Required("TYPE").ToUpperInvariant() switch
                                    {
                                        "L" or "LOCAL" => "LOCAL",
                                        "F" or "FOREIGN" => "FOREIGN",
                                        _ => throw new InvalidDataException("Vessel TYPE must be L/LOCAL or F/FOREIGN.")
                                    };
                                    Apply(vessels, vessel => vessel.VesselNumber, new Vessel { VesselNumber = item.Code, VesselName = item.Name, VesselType = vesselType, MsapRecId = legacyId },
                                        "VesselName,VesselType,MsapRecId", model, item, seenCodes);
                                    break;
                                case "terminal":
                                    var terminalCode = row.TerminalCode();
                                    var terminalPort = ports.SingleOrDefault(port => port.PortNumber == terminalCode.Port)
                                        ?? throw new InvalidDataException($"Port {terminalCode.Port} was not found. Include port.csv or import it first.");
                                    item.Code = row.Required("NUMBER");
                                    item.Name = row.Required("NAME");
                                    Apply(terminals, terminal => terminal.Port.PortNumber + "-" + terminal.TerminalNumber,
                                        new Terminal { TerminalNumber = terminalCode.Terminal, TerminalName = item.Name, Port = terminalPort, PortId = terminalPort.PortId,
                                            IsIoc = FlagOrExisting(row, "IOC", terminals.FirstOrDefault(terminal => terminal.Port.PortNumber == terminalCode.Port && terminal.TerminalNumber == terminalCode.Terminal)?.IsIoc), MsapRecId = legacyId },
                                        "TerminalName,IsIoc,MsapRecId", model, item, seenCodes);
                                    break;
                                case "tugboat":
                                    item.Code = row.Code("NUMBER", 3);
                                    item.Name = row.Required("NAME");
                                    bool owned = row.Flag("COMPANYOWN");
                                    string ownerCode = row.Text("OWNER");
                                    var owner = ownerCode is "" or "000" ? null : owners.SingleOrDefault(value => value.TugboatOwnerNumber == ownerCode.PadLeft(3, '0'))
                                        ?? throw new InvalidDataException($"Owner {ownerCode} was not found. Include tugboatowner.csv or correct the owner code.");
                                    if (!owned && owner == null)
                                    {
                                        throw new InvalidDataException("An external tugboat requires a real owner code; 000 is not an external provider.");
                                    }
                                    var existingTug = tugs.SingleOrDefault(tug => tug.TugboatNumber == item.Code);
                                    var homePort = existingTug?.Port ?? (row.Text("PORT").Length > 0
                                        ? ports.SingleOrDefault(port => port.PortNumber == row.Code("PORT", 3))
                                        : ports.SingleOrDefault(port => port.PortNumber == model.NewTugboatPortCode))
                                        ?? throw new InvalidDataException("Select a home port for new tugboats, or provide a PORT code column. Existing home ports are preserved.");
                                    Apply(tugs, tug => tug.TugboatNumber, new Tugboat
                                    {
                                        TugboatNumber = item.Code, TugboatName = item.Name, IsCompanyOwned = owned, TugboatOwner = owner,
                                        TugboatOwnerId = owner?.TugboatOwnerId, Port = homePort, PortId = homePort.PortId, MsapRecId = legacyId
                                    }, "TugboatName,IsCompanyOwned,TugboatOwner,MsapRecId", model, item, seenCodes);
                                    break;
                                case "principal":
                                    string agent = row.Code("AGENT", 4);
                                    var customer = customers.SingleOrDefault(value => value.CustomerCode == agent)
                                        ?? throw new InvalidDataException($"Customer/agent {agent} was not found. Include customer.csv or import it first.");
                                    string principalCode = row.Code("NUMBER", 4);
                                    item.Code = agent + "/" + principalCode;
                                    item.Name = row.Required("NAME");
                                    string principalTerms = ResolveTerms(row.PaymentTerms(), terms);
                                    Apply(principals, principal => principal.Customer.CustomerCode + "/" + principal.PrincipalNumber, new Principal
                                    {
                                        PrincipalNumber = principalCode, PrincipalName = item.Name, Customer = customer, CustomerId = customer.CustomerId,
                                        Agent = agent, Address1 = row.Text("ADDRESS1"), Address2 = row.Text("ADDRESS2"), Address3 = row.Text("ADDRESS3"),
                                        TIN = row.Text("TIN"), BusinessType = row.Text("BUSINESS"), Terms = principalTerms,
                                        Landline1 = row.Text("LANDLINE1"), Landline2 = row.Text("LANDLINE2"), Mobile1 = row.Text("MOBILE1"), Mobile2 = row.Text("MOBILE2"),
                                        IsActive = row.Flag("ACTIVE"), IsVatable = row.Flag("VATABLE"), MsapRecId = legacyId
                                    }, "PrincipalName,Agent,Address1,Address2,Address3,TIN,BusinessType,Terms,Landline1,Landline2,Mobile1,Mobile2,IsActive,IsVatable,MsapRecId", model, item, seenCodes);
                                    break;
                            }
                        }
                        catch (InvalidDataException ex)
                        {
                            item.Action = "Invalid";
                            item.Changes = ex.Message;
                            model.Errors.Add($"{source.File}, row {row.Number}: {ex.Message}");
                        }
                    }
                }
                model.Ports = ports.OrderBy(port => port.PortName)
                    .Select(port => new SelectListItem { Value = port.PortNumber, Text = port.PortNumber + " - " + port.PortName }).ToList();
                if (model.Errors.Count > 0)
                {
                    return;
                }
                model.ReviewedFingerprint = fingerprint;
                if (!import)
                {
                    return;
                }
                if (reviewed != fingerprint)
                {
                    model.ReviewedFingerprint = null;
                    model.Errors.Add("Files, options or stored master data changed. Validate and review the current comparison before importing.");
                    return;
                }
                foreach (var item in model.Items.Where(item => item.Action is "Add" or "Update"))
                {
                    db.AuditTrails.Add(new AuditTrail(actor.UserName ?? userId,
                        $"Master-file import {item.Action}: {item.FileName} row {item.Row}, {item.Code}: {item.Changes}", "Master File Import", referenceNumber: item.Code)
                    { UserId = userId, ActorName = actor.Name.Trim() });
                }
                await db.SaveChangesAsync(ct);
                if (stagedRates.Count > 0)
                {
                    foreach (var kind in new[] { RateTableKind.Tariff, RateTableKind.BAF })
                    {
                        var draft = new RateEditorViewModel
                        {
                            Kind = kind, Rows = stagedRates.Select((entry, index) => new RateUploadRowViewModel
                            {
                                RowNumber = index + 1, CustomerId = entry.Customer.CustomerId, PortId = entry.Port.PortId,
                                TerminalId = entry.Terminal.TerminalId, ServiceId = entry.Service.ServiceId,
                                EffectiveDate = entry.Date, Amount = kind == RateTableKind.Tariff ? entry.Tariff : entry.Baf,
                                LegacySourceId = entry.Row.Text("RECID"), LegacyVesselType = entry.Row.Text("TYPE")
                            }).ToList()
                        };
                        var saved = await rates.SaveDraftAsync(draft, userId, ct);
                        if (!saved.IsSuccess)
                        {
                            throw new InvalidDataException(saved.Message ?? "Rate draft could not be staged. The batch was not committed.");
                        }
                        model.RateDraftIds.Add(saved.Data);
                    }
                }
                await transaction.CommitAsync(ct);
                model.Imported = true;
                model.ReviewedFingerprint = null;
            }
            finally
            {
                // Preview and failed validation must not leave tracked changes available to a later save.
                db.ChangeTracker.Clear();
            }
        }

        private static string ResolveTerms(string code, List<FilprideTerms> terms)
        {
            int days = code == "COD" ? 0 : int.Parse(code[..^1]);
            var term = terms.SingleOrDefault(term => term.TermsCode == code);
            if (term == null || term.NumberOfDays != days || term.NumberOfMonths != 0)
            {
                throw new InvalidDataException($"Shared payment term {code} is missing or differs from the source's {days} days. Review IBS Payment Terms first; no local fallback is created.");
            }
            return term.TermsCode;
        }

        private static decimal ReadAmount(MasterFileImportCsv.Row row, string column)
        {
            if (!decimal.TryParse(row.Required(column), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out decimal amount))
            {
                throw new InvalidDataException($"{column} must be a decimal amount; blank is not zero.");
            }
            return amount;
        }

        private static bool FlagOrExisting(MasterFileImportCsv.Row row, string column, bool? existing)
        {
            return row.Text(column).Length > 0 ? row.Flag(column) : existing ?? row.Flag(column);
        }

        private static decimal DiscountOrExisting(MasterFileImportCsv.Row row, decimal? existing)
        {
            if (row.Text("COSDISCOUNT").Length == 0 && existing.HasValue)
            {
                return existing.Value;
            }
            decimal discount = ReadAmount(row, "COSDISCOUNT");
            if (discount is < 0 or > 100 || decimal.Round(discount, 4) != discount)
            {
                throw new InvalidDataException("COSDISCOUNT must be between 0 and 100 with at most four decimal places. Enter 0 explicitly for no discount.");
            }
            return discount;
        }

        public static void ApplyEdits(List<MasterFileImportDocument> sources, List<MasterFileImportDocument> edits)
        {
            if (edits.Count != sources.Count || edits.Select(document => document.Kind).Distinct().Count() != edits.Count)
            {
                throw new InvalidDataException("The selected files changed. Load and validate them again before editing.");
            }
            foreach (var source in sources)
            {
                var edited = edits.SingleOrDefault(document => document.Kind == source.Kind);
                if (edited == null || edited.FileName != source.FileName || edited.Rows.Count != source.Rows.Count
                    || edited.Rows.Select(row => row.Row).Distinct().Count() != edited.Rows.Count)
                {
                    throw new InvalidDataException("Editor rows must match their original file and row. Rows cannot be added or removed through corrections.");
                }
                var rows = edited.Rows.ToDictionary(row => row.Row);
                var allowed = source.Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var original in source.Rows)
                {
                    if (!rows.TryGetValue(original.Row, out var row) || row.Fields == null
                        || row.Fields.Keys.Except(original.Fields.Keys).Any(key => !allowed.Contains(key))
                        || original.Fields.Keys.Any(key => !row.Fields.ContainsKey(key)))
                    {
                        throw new InvalidDataException("Editor columns or source rows changed. Reload the files and validate again.");
                    }
                    foreach (var field in row.Fields)
                    {
                        if (field.Value == null || field.Value.Length > 1000
                            || !allowed.Contains(field.Key) && original.Fields.GetValueOrDefault(field.Key) != field.Value)
                        {
                            throw new InvalidDataException("Only displayed master fields can be corrected, with up to 1,000 characters per cell.");
                        }
                    }
                    original.Fields = row.Fields.ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private void Apply<T>(List<T> records, Func<T, string> key, T candidate, string fields,
            MasterFileImportViewModel model, MasterFileImportItem item, HashSet<string> seen) where T : class
        {
            if (!seen.Add(typeof(T).Name + "|" + item.Code))
            {
                throw new InvalidDataException("Duplicate business code in this file. Names alone are not unique identifiers.");
            }
            var proposed = db.Entry(candidate);
            foreach (var property in proposed.Properties)
            {
                if (property.CurrentValue is string value && property.Metadata.GetMaxLength() is int max && value.Length > max)
                {
                    throw new InvalidDataException($"{property.Metadata.Name} exceeds {max} characters; the import will not truncate it.");
                }
            }
            var matches = records.Where(record => key(record) == item.Code).ToList();
            if (matches.Count > 1)
            {
                throw new InvalidDataException("Stored business code is ambiguous. Clean duplicate master records before importing.");
            }
            T? existing = matches.SingleOrDefault();
            string? legacy = proposed.Metadata.FindProperty("MsapRecId") != null ? proposed.Property("MsapRecId").CurrentValue as string : null;
            if (!string.IsNullOrEmpty(legacy) && records.Any(record => !ReferenceEquals(record, existing)
                && db.Entry(record).Property("MsapRecId").CurrentValue as string == legacy))
            {
                throw new InvalidDataException("RECID already belongs to another business code. Resolve the identity conflict explicitly.");
            }
            if (existing == null)
            {
                item.Action = "Add";
                item.Changes = string.Join("; ", fields.Split(',').Select(field => Label(field) + ": "
                    + (proposed.Metadata.FindNavigation(field) != null ? ReferenceCode(proposed.Reference(field).CurrentValue)
                        : JsonSerializer.Serialize(proposed.Property(field).CurrentValue))));
                if (candidate is Tugboat tug)
                {
                    item.Changes += "; Home port: " + tug.Port.PortNumber;
                }
                records.Add(candidate);
                db.Add(candidate);
                return;
            }
            var current = db.Entry(existing);
            string? storedLegacy = legacy != null ? current.Property("MsapRecId").CurrentValue as string : null;
            if (!string.IsNullOrEmpty(legacy) && !string.IsNullOrEmpty(storedLegacy) && storedLegacy != legacy)
            {
                throw new InvalidDataException("Business code belongs to another RECID. Resolve the identity conflict explicitly.");
            }
            var changes = new List<string>();
            foreach (string field in fields.Split(','))
            {
                if (proposed.Metadata.FindNavigation(field) != null)
                {
                    object? before = current.Reference(field).CurrentValue;
                    object? after = proposed.Reference(field).CurrentValue;
                    if (!ReferenceEquals(before, after))
                    {
                        changes.Add($"{Label(field)}: {ReferenceCode(before)} -> {ReferenceCode(after)}");
                        if (model.UpdateExisting)
                        {
                            current.Reference(field).CurrentValue = after;
                        }
                    }
                    continue;
                }
                object? oldValue = current.Property(field).CurrentValue;
                object? newValue = proposed.Property(field).CurrentValue;
                if (Equals(oldValue, newValue) || field == "MsapRecId" && string.IsNullOrEmpty(newValue as string))
                {
                    continue;
                }
                changes.Add($"{Label(field)}: {JsonSerializer.Serialize(oldValue)} -> {JsonSerializer.Serialize(newValue)}");
                if (model.UpdateExisting)
                {
                    current.Property(field).CurrentValue = newValue;
                }
            }
            item.Action = changes.Count == 0 ? "Unchanged" : model.UpdateExisting ? "Update" : "Skip existing";
            item.Changes = changes.Count == 0 ? "No source-field changes." : string.Join("; ", changes);
        }

        private static string ReferenceCode(object? value)
        {
            return value switch { TugboatOwner owner => owner.TugboatOwnerNumber, null => "(none)", _ => throw new InvalidOperationException("Unsupported import reference.") };
        }

        private static string Label(string property)
        {
            return property == "MsapRecId" ? "Source record ID" : Regex.Replace(property, "([a-z])([A-Z])", "$1 $2");
        }
    }
}
