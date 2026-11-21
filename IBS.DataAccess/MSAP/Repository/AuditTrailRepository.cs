using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository
{
    public class AuditTrailRepository(MsapDbContext db): Repository<AuditTrail>(db), IAuditTrailRepository
    {
        private readonly MsapDbContext _db = db;

        private IQueryable<AuditTrail> NamedAudits()
        {
            return _db.AuditTrails.AsNoTracking().Select(audit => new AuditTrail("", "", "")
            {
                Id = audit.Id, Username = audit.Username, Activity = audit.Activity, DocumentType = audit.DocumentType,
                RecordId = audit.RecordId, ReferenceNumber = audit.ReferenceNumber,
                Date = audit.Date, MachineName = audit.MachineName, UserId = audit.UserId,
                ActorName = audit.ActorName ?? _db.Users.Where(user => audit.UserId != null ? user.Id == audit.UserId : user.UserName == audit.Username)
                    .Select(user => user.Name).FirstOrDefault()
            });
        }

        public override async Task<IEnumerable<AuditTrail>> GetAllAsync(Expression<Func<AuditTrail, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<AuditTrail> query = NamedAudits();
            if (filter != null)
            {
                query = query.Where(filter);
            }
            return await query.ToListAsync(cancellationToken);
        }

        public async Task<(IEnumerable<AuditTrail> Data, int RecordsFiltered, int TotalRecords)> GetPagedAuditTrailsAsync(DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            IQueryable<AuditTrail> query = NamedAudits();

            var globalSearch = parameters?.Search?.Value;
            if (!string.IsNullOrEmpty(globalSearch))
            {
                var search = globalSearch.ToLower();
                query = query.Where(a =>
                    (a.ActorName != null && a.ActorName.ToLower().Contains(search))
                    || a.Activity.ToLower().Contains(search)
                    || a.DocumentType.ToLower().Contains(search)
                    || (a.ReferenceNumber != null && a.ReferenceNumber.ToLower().Contains(search)));
            }

            // Column-specific search
            if (parameters?.Columns != null)
            {
                foreach (var column in parameters.Columns)
                {
                    if (column?.Search?.Value is { Length: > 0 } searchValue)
                    {
                        var s = searchValue.ToLower();
                        if (column.Data == "documentType")
                        {
                            query = query.Where(a => a.DocumentType.ToLower() == s);
                        }
                        else if (column.Data == "date")
                        {
                            if (DateTime.TryParse(searchValue, out var parsedDate))
                            {
                                var dateOnly = parsedDate.Date;
                                query = query.Where(a => a.Date.Date == dateOnly);
                            }
                        }
                    }
                }
            }

            var totalRecords = await _db.AuditTrails.CountAsync(cancellationToken);
            var recordsFiltered = await query.CountAsync(cancellationToken);

            // Sorting
            var order = parameters?.Order?.FirstOrDefault();
            if (order != null
                && parameters?.Columns != null
                && order.Column >= 0
                && order.Column < parameters.Columns.Count)
            {
                var column = parameters.Columns[order.Column]?.Data?.ToLowerInvariant();
                var isAscending = string.Equals(order.Dir, "asc", StringComparison.OrdinalIgnoreCase);

                query = column switch
                {
                    "date" => isAscending ? query.OrderBy(a => a.Date) : query.OrderByDescending(a => a.Date),
                    "actorname" => isAscending ? query.OrderBy(a => a.ActorName) : query.OrderByDescending(a => a.ActorName),
                    "documenttype" => isAscending ? query.OrderBy(a => a.DocumentType) : query.OrderByDescending(a => a.DocumentType),
                    "referencenumber" => isAscending ? query.OrderBy(a => a.ReferenceNumber) : query.OrderByDescending(a => a.ReferenceNumber),
                    "activity" => isAscending ? query.OrderBy(a => a.Activity) : query.OrderByDescending(a => a.Activity),
                    "machinename" => isAscending ? query.OrderBy(a => a.MachineName) : query.OrderByDescending(a => a.MachineName),
                    _ => query.OrderByDescending(a => a.Date)
                };
            }
            else
            {
                query = query.OrderByDescending(a => a.Date);
            }

            var start = Math.Max(0, parameters?.Start ?? 0);
            var length = parameters?.Length >= 0 ? parameters.Length : 10;
            var data = await query
                .Skip(start)
                .Take(length)
                .ToListAsync(cancellationToken);

            return (data, RecordsFiltered: recordsFiltered, TotalRecords: totalRecords);
        }
    }
}
