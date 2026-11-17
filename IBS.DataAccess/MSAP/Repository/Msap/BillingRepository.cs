using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.Books;
using IBS.Models.MSAP;
using IBS.Utility.Helpers;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class BillingRepository(MsapDbContext db): Repository<Billing>(db), IBillingRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<Billing?> GetForUpdateAsync(int id, CancellationToken ct = default)
        {
            var record = await _db.MsapBillings
                .FromSqlInterpolated($"SELECT * FROM msap.mmsi_billings WHERE \"RECID\" = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (record != null)
            {
                await _db.Entry(record).ReloadAsync(ct);
            }
            return record;
        }

        public async Task AddSalesBookAsync(IBS.Models.MSAP.Books.SalesBook entry, CancellationToken ct)
        {
            var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == entry.ApprovedByUserId && u.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                throw new InvalidOperationException("The approver needs an active account with a full name.");
            }
            entry.ApprovedByName = actor.Name.Trim();
            await _db.MsapSalesBooks.AddAsync(entry, ct);
        }

        public async Task VoidSalesBookAsync(int billingId, int revision, string username, string reason, CancellationToken ct)
        {
            var entries = await _db.MsapSalesBooks
                .Where(b => b.DocumentId == billingId && b.BillingRevision == revision && b.VoidedAt == null)
                .ToListAsync(ct);
            var now = IBS.Utility.MSAP.Helpers.DateTimeHelper.GetCurrentPhilippineTime();
            foreach (var entry in entries)
            {
                entry.VoidedAt = now;
                entry.VoidedBy = username;
                entry.VoidReason = reason;
            }
        }

        public async Task<List<int>> GetBillingIdsWithSalesBookAsync(List<int> billingIds, CancellationToken ct)
        {
            return await _db.MsapSalesBooks.Where(b => b.DocumentId.HasValue && billingIds.Contains(b.DocumentId.Value))
                .Select(b => b.DocumentId!.Value).Distinct().ToListAsync(ct);
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public override async Task<IEnumerable<Billing>> GetAllAsync(Expression<Func<Billing, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<Billing> query = dbSet
                .Include(a => a.Terminal)
                .Include(a => a.Vessel)
                .Include(a => a.Customer)
                .Include(a => a.BasicBilling)
                .Include(a => a.BafBilling)
                .Include(a => a.Port);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public override async Task<Billing?> GetAsync(Expression<Func<Billing, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<Billing> query = dbSet
                .Include(b => b.Terminal)
                .ThenInclude(t => t.Port)
                .Include(b => b.Vessel)
                .Include(b => b.Customer)
                .Include(b => b.Principal)
                .Include(b => b.ReviewedByUser)
                .Include(b => b.RejectedByUser)
                .Include(b => b.ApprovedByUser)
                .Include(b => b.BasicBilling)
                .Include(b => b.BafBilling)
                .Include(b => b.CustomerOrderSlip);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<List<string>?> GetToBillDispatchTicketListAsync(int billingId, CancellationToken cancellationToken = default)
        {
            return await _db.MsapDispatchTickets.Where(t => t.BillingId == billingId)
                .Select(d => d.DispatchTicketId.ToString()).ToListAsync(cancellationToken);
        }

        public async Task<List<string>?> GetUniqueTugboatsListAsync(int billingId, CancellationToken cancellationToken = default)
        {
            int sourceId = await _db.MsapBillings.Where(b => b.MsapBillingId == billingId)
                .Select(b => b.BasicBillingId ?? b.MsapBillingId).SingleAsync(cancellationToken);
            return await _db.MsapDispatchTickets
                .Where(dt => dt.BillingId == sourceId)
                .Select(dt => dt.Tugboat.TugboatName.ToString())
                .Distinct() // Ensures unique values
                .ToListAsync(cancellationToken);
        }

        public async Task<List<DispatchTicket>?> GetPaidDispatchTicketsAsync(int billingId, CancellationToken cancellationToken = default)
        {
            int sourceId = await _db.MsapBillings.Where(b => b.MsapBillingId == billingId)
                .Select(b => b.BasicBillingId ?? b.MsapBillingId).SingleAsync(cancellationToken);
            return await _db.MsapDispatchTickets.AsNoTracking()
                .Where(dt => dt.BillingId == sourceId)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat)
                .Include(a => a.OtherTugChargeLine)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel)
                .OrderBy(dt => dt.DateLeft).ThenBy(dt => dt.TimeLeft)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapTerminalsByPortId(int portId, CancellationToken cancellationToken)
        {
            var terminals = await _db
                .MsapTerminals
                .Where(t => t.PortId == portId)
                .OrderBy(t => t.TerminalName)
                .ToListAsync(cancellationToken);

            var terminalsList = terminals.Select(t => new SelectListItem
            {
                Value = t.TerminalId.ToString(),
                Text = t.TerminalName
            }).ToList();

            return terminalsList;
        }

        public async Task<List<SelectListItem>?> GetMsapCustomersWithBillablesSelectList(int? currentCustomerId, string type, CancellationToken cancellationToken = default)
        {
            return await _db.Customers.AsNoTracking()
                .Where(c => _db.MsapDispatchTickets.Any(t =>
                                ((t.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling && t.BillingId == null)
                                 || (currentCustomerId.GetValueOrDefault() != 0 && t.CustomerId == currentCustomerId))
                                && t.CustomerId == c.CustomerId) &&
                            (string.IsNullOrEmpty(type) || c.Type == type) &&
                            c.IsActive)
                .OrderBy(s => s.CustomerName)
                .Select(s => new SelectListItem
                {
                    Value = s.CustomerId.ToString(),
                    Text = s.CustomerName
                }).ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapUnbilledTicketsById(string type, CancellationToken cancellationToken = default)
        {
            var dispatchTicketList = await _db.MsapDispatchTickets
                .Where(dt => dt.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling && dt.BillingId == null)
                .OrderBy(dt => dt.DispatchNumber)
                .Select(s => new SelectListItem
                {
                    Value = s.DispatchTicketId.ToString(),
                    Text = s.DispatchNumber
                }).ToListAsync(cancellationToken);

            return dispatchTicketList;
        }

        public async Task<List<SelectListItem>?> GetMsapUnbilledTicketsByCustomer(int? customerId, CancellationToken cancellationToken)
        {
            var tickets = await _db
                .MsapDispatchTickets
                .Where(b => b.CustomerId == customerId && b.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling && b.BillingId == null)
                .Include(b => b.Customer)
                .OrderBy(b => b.DispatchNumber)
                .ToListAsync(cancellationToken);

            var ticketsList = tickets.Select(b => new SelectListItem
            {
                Value = b.DispatchTicketId.ToString(),
                Text = b.DispatchNumber
            }).ToList();

            return ticketsList;
        }

        public async Task<List<SelectListItem>> GetMsapBilledTicketsById(int id, CancellationToken cancellationToken = default)
        {
            var dispatchTicketList = await _db.MsapDispatchTickets
                .Where(dt => dt.BillingId == id)
                .OrderBy(dt => dt.DispatchNumber).Select(s => new SelectListItem
                {
                    Value = s.DispatchTicketId.ToString(),
                    Text = s.DispatchNumber
                }).ToListAsync(cancellationToken);

            return dispatchTicketList;
        }

        public async Task<string> GenerateBillingNumber(int year, CancellationToken cancellationToken = default)
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20771002)", cancellationToken);
            var lastRecord = await _db.MsapBillings
                .Where(b => b.Year == year)
                .Where(b => !string.IsNullOrEmpty(b.MsapBillingNumber) && b.MsapBillingNumber.StartsWith("B"))
                .OrderByDescending(b => b.MsapBillingNumber.Length)
                .ThenByDescending(b => b.MsapBillingNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastRecord == null)
            {
                return "B000001";
            }

            var lastSeries = lastRecord.MsapBillingNumber.Substring(1);
            if (int.TryParse(lastSeries, out int lastNumber))
            {
                return "B" + ((lastNumber + 1).ToString("D6"));
            }

            return "B000001";
        }

        public async Task<string> GetSuggestedBirBillingNumberAsync(string company, int year, CancellationToken cancellationToken = default)
        {
            var numbers = await _db.MsapBillings
                .AsNoTracking()
                .Where(b => b.Company == company && b.Year == year && !b.IsUndocumented && !string.IsNullOrEmpty(b.MsapBillingNumber))
                .Select(b => b.MsapBillingNumber)
                .ToListAsync(cancellationToken);

            var bestValue = -1;
            string? bestRaw = null;
            foreach (var raw in numbers)
            {
                if (int.TryParse(raw.Trim(), out var parsed) && parsed >= 0 && parsed > bestValue)
                {
                    bestValue = parsed;
                    bestRaw = raw.Trim();
                }
            }

            return bestRaw == null ? "1" : IncrementBillingSeries(bestRaw);
        }

        public string IncrementBillingSeries(string currentNumber)
        {
            var trimmed = (currentNumber ?? "").Trim();
            if (int.TryParse(trimmed, out var parsed) && parsed >= 0)
            {
                var next = parsed + 1;
                var hasLeadingZero = trimmed.Length > 1 && trimmed[0] == '0';
                return hasLeadingZero ? next.ToString($"D{trimmed.Length}") : next.ToString();
            }

            return "1";
        }

        public Billing ProcessAddress(Billing model, CancellationToken cancellationToken = default)
        {
            // ... (rest of ProcessAddress implementation)
            var words = model.PrincipalId != null
                ? model.Principal?.Address.Split(' ')
                : model.Customer?.CustomerAddress.Split(' ');
            var resultStrings = new List<string>();
            var currentString = "";

            if (words == null)
            {
                model.AddressLine1 = string.Empty;
                model.AddressLine2 = string.Empty;
                model.AddressLine3 = string.Empty;
                model.AddressLine4 = string.Empty;

                return model;
            }
            foreach (var word in words)
            {
                if (currentString.Length + word.Length + (currentString.Length > 0 ? 1 : 0) > 40)
                {
                    if (currentString.Length > 0)
                    {
                        resultStrings.Add(currentString.Trim());
                    }
                    currentString = word;
                }
                else
                {
                    currentString += (currentString.Length > 0 ? " " : "") + word;
                }
            }

            if (currentString.Length > 0)
            {
                resultStrings.Add(currentString.Trim());
            }
            var firstString = resultStrings.Count > 0 ? resultStrings[0] : "";
            var secondString = resultStrings.Count > 1 ? resultStrings[1] : "";
            var thirdString = resultStrings.Count > 2 ? resultStrings[2] : "";
            var fourthString = resultStrings.Count > 3 ? resultStrings[3] : "";

            model.AddressLine1 = firstString;
            model.AddressLine2 = secondString;
            model.AddressLine3 = thirdString;
            model.AddressLine4 = fourthString;

            return model;
        }

        public async Task<(IEnumerable<Billing> Data, int RecordsFiltered, int TotalRecords)> GetPagedBillingsAsync(DataTablesParameters parameters, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            IQueryable<Billing> query = dbSet.AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Terminal).ThenInclude(b => b.Port)
                .Include(b => b.Vessel)
                .Include(b => b.BasicBilling)
                .Include(b => b.BafBilling);

            if (customerOrderSlipId.HasValue)
            {
                query = query.Where(b => b.CustomerOrderSlipId == customerOrderSlipId.Value || _db.Set<DispatchTicket>()
                    .Any(t => t.CustomerOrderSlipId == customerOrderSlipId.Value && t.BillingId == b.MsapBillingId));
            }
            int totalRecords = await query.CountAsync(cancellationToken);

            if (!string.IsNullOrEmpty(parameters.Search.Value))
            {
                var s = parameters.Search.Value.ToLower();
                var matchingBillingIds = await _db.Set<DispatchTicket>()
                    .Where(dt => dt.BillingId != null && dt.DispatchNumber.ToLower().Contains(s))
                    .Select(dt => dt.BillingId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                query = query.Where(dt =>
                    dt.MsapBillingNumber.ToLower().Contains(s) ||
                    dt.Customer.CustomerName.ToLower().Contains(s) ||
                    dt.Vessel.VesselName.ToLower().Contains(s) ||
                    dt.Status.ToLower().Contains(s) ||
                    matchingBillingIds.Contains(dt.MsapBillingId)
                );
            }

            // Column-specific search
            if (parameters.Columns != null)
            {
                foreach (var column in parameters.Columns)
                {
                    if (column.Search?.Value is { Length: > 0 } searchValue)
                    {
                        if (column.Data == "status")
                        {
                            query = query.Where(b => b.Status.ToLower() == searchValue.ToLower());
                        }
                        else if (column.Data == "date" || column.Data == "Date")
                        {
                            if (DateOnly.TryParse(searchValue, out var parsedDate))
                            {
                                query = query.Where(b => b.Date == parsedDate);
                            }
                        }
                    }
                }
            }

            var recordsFiltered = await query.CountAsync(cancellationToken);

            if (parameters.Order?.Count > 0 && parameters.Columns != null)
            {
                var col = parameters.Columns[parameters.Order[0].Column].Data;
                var dir = parameters.Order[0].Dir.ToLower() == "asc" ? "ascending" : "descending";
                query = query.OrderBy($"{col} {dir}");
            }

            var data = await query
                .Skip(parameters.Start)
                .Take(parameters.Length)
                .ToListAsync(cancellationToken);

            var billingIds = data.Select(b => b.MsapBillingId).ToList();
            var ticketGroups = await _db.Set<DispatchTicket>()
                .Where(dt => dt.BillingId != null && billingIds.Contains(dt.BillingId.Value))
                .GroupBy(dt => dt.BillingId!.Value)
                .Select(g => new { BillingId = g.Key, Numbers = g.Select(dt => dt.DispatchNumber) })
                .ToListAsync(cancellationToken);
            var ticketMap = ticketGroups.ToDictionary(g => g.BillingId, g => string.Join(", ", g.Numbers));
            foreach (var b in data)
            {
                if (ticketMap.TryGetValue(b.MsapBillingId, out var numbers))
                    b.DispatchTicketNumbers = numbers;
            }

            return (data, recordsFiltered, totalRecords);
        }

        public async Task<List<Billing>> GetBillingsByCollectionIdAsync(int collectionId, CancellationToken cancellationToken)
        {
            return await _db.MsapBillings
                .Where(b => b.CollectionId == collectionId)
                .ToListAsync(cancellationToken);
        }

        public async Task RecognizeCommissionLiabilityAsync(Billing billing, string actor, CancellationToken ct = default)
        {
            var items = new List<(int RepresentativeId, string Category, string CategoryLabel, decimal Amount)>();
            if (billing.AgencyIncentiveRepresentativeId.HasValue && billing.AgencyIncentiveAmount > 0m)
            {
                items.Add((
                    billing.AgencyIncentiveRepresentativeId.Value,
                    MsapConstants.RepresentativeCategory.AgencyIncentive,
                    "Agency Incentive",
                    billing.AgencyIncentiveAmount));
            }
            if (billing.AgentCommissionRepresentativeId.HasValue && billing.AgentCommissionAmount > 0m)
            {
                items.Add((
                    billing.AgentCommissionRepresentativeId.Value,
                    MsapConstants.RepresentativeCategory.AgentCommission,
                    "Agent Commission",
                    billing.AgentCommissionAmount));
            }

            if (items.Count == 0)
            {
                return;
            }

            await using var accounting = await OpenAccountingContextAsync(ct);
            var work = new IBS.DataAccess.Repository.UnitOfWork(accounting);
            var glRef = billing.MsapBillingNumber.Length <= 20
                ? billing.MsapBillingNumber
                : billing.MsapBillingNumber[..20];

            var recognitionDate = billing.Date;
            var closedPeriodAdjustment = false;
            if (await work.IsPeriodPostedAsync(Module.ReceivingReport, recognitionDate, ct))
            {
                closedPeriodAdjustment = true;
                var today = DateOnly.FromDateTime(IBS.Utility.MSAP.Helpers.DateTimeHelper.GetCurrentPhilippineTime());
                var minOpenDate = DateOnly.FromDateTime(await work.GetMinimumPeriodBasedOnThePostedPeriods(Module.ReceivingReport, ct));
                recognitionDate = today >= minOpenDate ? today : minOpenDate;
            }

            var expenseAccount = await accounting.FilprideChartOfAccounts
                .SingleOrDefaultAsync(a => a.AccountNumber == MsapConstants.CommissionExpenseAccount && !a.IsMain && !a.IsHidden, ct)
                ?? throw new InvalidOperationException("Required posting account is unavailable: " + MsapConstants.CommissionExpenseAccount);
            var apAccount = await accounting.FilprideChartOfAccounts
                .SingleOrDefaultAsync(a => a.AccountNumber == MsapConstants.CommissionPayableAccount && !a.IsMain && !a.IsHidden, ct)
                ?? throw new InvalidOperationException("Required posting account is unavailable: " + MsapConstants.CommissionPayableAccount);
            IBS.Models.Filpride.MasterFile.FilprideChartOfAccount? vatAccount = null;

            var repIds = items.Select(i => i.RepresentativeId).Distinct().ToArray();
            var reps = await _db.MsapRepresentatives.AsNoTracking()
                .Where(r => repIds.Contains(r.RepresentativeId))
                .ToDictionaryAsync(r => r.RepresentativeId, ct);

            var now = IBS.Utility.MSAP.Helpers.DateTimeHelper.GetCurrentPhilippineTime();
            var ledgers = new List<FilprideGeneralLedgerBook>();

            foreach (var item in items)
            {
                if (!reps.TryGetValue(item.RepresentativeId, out var rep))
                {
                    throw new InvalidOperationException("Assigned MSAP representative was not found.");
                }

                var profileError = PaymentProfileHelper.Validate(
                    rep,
                    rep.Address,
                    rep.Tin,
                    rep.VatType,
                    rep.TaxType,
                    rep.WithholdingTaxPercent,
                    required: true,
                    "representative");
                if (profileError != null)
                {
                    throw new InvalidOperationException(profileError);
                }

                var marker = $"({billing.MsapBillingNumber} - {item.CategoryLabel})";
                var alreadyRecognized = await accounting.FilprideGeneralLedgerBooks
                    .IgnoreQueryFilters(["TugboatWorkspace"])
                    .AnyAsync(g => g.WorkspaceTugboatId == null
                        && g.Reference == glRef
                        && g.CounterpartyType == CounterpartyType.Representative
                        && g.CounterpartyId == rep.RepresentativeId
                        && g.Description.Contains(marker), ct);
                if (alreadyRecognized)
                {
                    continue;
                }

                var (gross, net, inputVat, _, _) = PaymentProfileHelper.ComputePayableBreakdown(
                    rep.VatType!,
                    rep.TaxType!,
                    rep.WithholdingTaxPercent ?? 0m,
                    item.Amount);

                var particulars = closedPeriodAdjustment
                    ? $"ADJUSTMENT ON SET UP A/P {item.CategoryLabel.ToUpperInvariant()} - {rep.RepresentativeName} {marker} (BILLING DATE {billing.Date:MMM dd, yyyy})"
                    : $"SET UP A/P {item.CategoryLabel.ToUpperInvariant()} - {rep.RepresentativeName} {marker}";

                ledgers.Add(new FilprideGeneralLedgerBook
                {
                    WorkspaceTugboatId = null,
                    Date = recognitionDate,
                    Reference = glRef,
                    Description = particulars,
                    AccountId = expenseAccount.AccountId,
                    AccountNo = expenseAccount.AccountNumber!,
                    AccountTitle = expenseAccount.AccountName,
                    Debit = net,
                    Credit = 0m,
                    CreatedBy = actor,
                    CreatedDate = now,
                    IsPosted = true,
                    ModuleType = nameof(ModuleType.Purchase),
                    CounterpartyType = CounterpartyType.Representative,
                    CounterpartyId = rep.RepresentativeId,
                    CounterpartyName = rep.RepresentativeName
                });

                if (inputVat > 0m)
                {
                    vatAccount ??= await accounting.FilprideChartOfAccounts
                        .SingleOrDefaultAsync(a => a.AccountNumber == MsapConstants.InputVatAccount && !a.IsMain && !a.IsHidden, ct)
                        ?? throw new InvalidOperationException("Required posting account is unavailable: " + MsapConstants.InputVatAccount);
                    ledgers.Add(new FilprideGeneralLedgerBook
                    {
                        WorkspaceTugboatId = null,
                        Date = recognitionDate,
                        Reference = glRef,
                        Description = particulars,
                        AccountId = vatAccount.AccountId,
                        AccountNo = vatAccount.AccountNumber!,
                        AccountTitle = vatAccount.AccountName,
                        Debit = inputVat,
                        Credit = 0m,
                        CreatedBy = actor,
                        CreatedDate = now,
                        IsPosted = true,
                        ModuleType = nameof(ModuleType.Purchase),
                        CounterpartyType = CounterpartyType.Representative,
                        CounterpartyId = rep.RepresentativeId,
                        CounterpartyName = rep.RepresentativeName
                    });
                }

                ledgers.Add(new FilprideGeneralLedgerBook
                {
                    WorkspaceTugboatId = null,
                    Date = recognitionDate,
                    Reference = glRef,
                    Description = particulars,
                    AccountId = apAccount.AccountId,
                    AccountNo = apAccount.AccountNumber!,
                    AccountTitle = apAccount.AccountName,
                    Debit = 0m,
                    Credit = gross,
                    CreatedBy = actor,
                    CreatedDate = now,
                    IsPosted = true,
                    ModuleType = nameof(ModuleType.Purchase),
                    CounterpartyType = CounterpartyType.Representative,
                    CounterpartyId = rep.RepresentativeId,
                    CounterpartyName = rep.RepresentativeName
                });
            }

            if (ledgers.Count > 0)
            {
                await accounting.FilprideGeneralLedgerBooks.AddRangeAsync(ledgers, ct);
                await accounting.FilprideAuditTrails.AddAsync(
                    new IBS.Models.Filpride.Books.FilprideAuditTrail(
                        actor,
                        $"Posted MSAP representative commission/incentive GL entries for {billing.MsapBillingNumber}",
                        "Billing"),
                    ct);
                await accounting.SaveChangesAsync(ct);
            }
        }

        public async Task<string?> GetActiveCommissionVoucherNumberAsync(int billingId, CancellationToken ct = default)
        {
            await using var accounting = await OpenAccountingContextAsync(ct);
            return await accounting.FilprideMsapCommissionPayments
                .IgnoreQueryFilters(["TugboatWorkspace"])
                .Include(p => p.Voucher)
                .Where(p => p.MsapBillingId == billingId
                    && p.ReleasedAt == null
                    && p.Voucher.Status != nameof(CheckVoucherPaymentStatus.Voided)
                    && p.Voucher.Status != nameof(CheckVoucherPaymentStatus.Canceled))
                .Select(p => p.Voucher.CheckVoucherHeaderNo)
                .FirstOrDefaultAsync(ct);
        }

        public async Task ReverseCommissionLiabilityAsync(Billing billing, CancellationToken ct = default)
        {
            var glRef = billing.MsapBillingNumber.Length <= 20
                ? billing.MsapBillingNumber
                : billing.MsapBillingNumber[..20];

            await using var accounting = await OpenAccountingContextAsync(ct);
            var glEntries = await accounting.FilprideGeneralLedgerBooks
                .IgnoreQueryFilters(["TugboatWorkspace"])
                .Where(g => g.WorkspaceTugboatId == null
                    && g.Reference == glRef
                    && g.CounterpartyType == CounterpartyType.Representative)
                .ToListAsync(ct);

            if (glEntries.Count > 0)
            {
                accounting.FilprideGeneralLedgerBooks.RemoveRange(glEntries);
                await accounting.SaveChangesAsync(ct);
            }
        }

        private async Task<ApplicationDbContext> OpenAccountingContextAsync(CancellationToken ct)
        {
            var accounting = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_db.Database.GetDbConnection()).Options);
            if (_db.Database.CurrentTransaction != null)
            {
                await accounting.Database.UseTransactionAsync(_db.Database.CurrentTransaction.GetDbTransaction(), ct);
            }
            return accounting;
        }
    }
}
