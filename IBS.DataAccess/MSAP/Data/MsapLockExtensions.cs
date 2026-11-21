using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IBS.DataAccess.MSAP.Data
{
    public static class MsapLockExtensions
    {
        public const long CustomerOrderSlipNumberSeries = 20771001L;
        public const long BillingNumberSeries = 20771002L;
        public const long CommissionVoucherSeries = 20771010L;
        public const long MasterFileImport = 20771011L;
        public const long RateTableActivation = 20771012L;
        public const string CustomerCodeScope = "msap_customer_code";

        public static Task AcquireAdvisoryXactLockAsync(this DbContext db, long lockKey, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(db);
            return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        }

        public static Task AcquireSharedAdvisoryXactLockAsync(this DbContext db, long lockKey, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(db);
            return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({lockKey})", ct);
        }

        public static Task AcquireAdvisoryXactLockAsync(this DbContext db, string scope, string key, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(db);
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({scope}), hashtext({key}))",
                ct);
        }

        public static IQueryable<TEntity> QueryForUpdate<TEntity, TKey>(this DbContext db, TKey id)
            where TEntity : class
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(db);
            var sql = BuildRowLockSql<TEntity>(db, "FOR UPDATE");
            return db.Set<TEntity>().FromSqlRaw(sql, id);
        }

        public static IQueryable<TEntity> QueryForShare<TEntity, TKey>(this DbContext db, TKey id)
            where TEntity : class
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(db);
            var sql = BuildRowLockSql<TEntity>(db, "FOR SHARE");
            return db.Set<TEntity>().FromSqlRaw(sql, id);
        }

        public static async Task<TEntity?> FindForUpdateAsync<TEntity, TKey>(
            this DbContext db,
            TKey id,
            bool reload = true,
            CancellationToken ct = default)
            where TEntity : class
            where TKey : notnull
        {
            var record = await db.QueryForUpdate<TEntity, TKey>(id).FirstOrDefaultAsync(ct);
            if (record != null && reload)
            {
                await db.Entry(record).ReloadAsync(ct);
            }

            return record;
        }

        public static Task LockRowForUpdateAsync<TEntity, TKey>(
            this DbContext db,
            TKey id,
            CancellationToken ct = default)
            where TEntity : class
            where TKey : notnull
        {
            return db.QueryForUpdate<TEntity, TKey>(id).LoadAsync(ct);
        }

        public static Task LockRowForShareAsync<TEntity, TKey>(
            this DbContext db,
            TKey id,
            CancellationToken ct = default)
            where TEntity : class
            where TKey : notnull
        {
            return db.QueryForShare<TEntity, TKey>(id).LoadAsync(ct);
        }

        private static string BuildRowLockSql<TEntity>(DbContext db, string lockClause)
            where TEntity : class
        {
            var entityType = db.Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not part of the model for '{db.GetType().Name}'.");
            var tableName = entityType.GetTableName()
                ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not mapped to a database table.");
            var schema = entityType.GetSchema();
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey == null || primaryKey.Properties.Count != 1)
            {
                throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' must have a single-column primary key for row locking.");
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, schema);
            var keyColumn = primaryKey.Properties[0].GetColumnName(storeObject)
                ?? throw new InvalidOperationException($"Primary key column could not be resolved for '{typeof(TEntity).Name}'.");

            var qualifiedTable = string.IsNullOrEmpty(schema)
                ? QuoteIdentifier(tableName)
                : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";

            return $"SELECT * FROM {qualifiedTable} WHERE {QuoteIdentifier(keyColumn)} = {{0}} {lockClause}";
        }

        private static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }
    }
}
