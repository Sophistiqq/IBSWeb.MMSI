using IBS.Models;
using IBS.Models.Filpride;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.AccountsReceivable;
using IBS.Models.Filpride.Books;
using IBS.Models.Filpride.Integrated;
using IBS.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IBS.DataAccess.Data
{
    public partial class ApplicationDbContext
    {
        private readonly TugboatWorkspace _workspace;

        public int? WorkspaceTugboatId => _workspace.TugboatId;

        private void ConfigureWorkspaces(ModelBuilder builder)
        {
            ConfigureWorkspace<FilprideCheckVoucherHeader>(builder);
            ConfigureWorkspace<FilprideJournalVoucherHeader>(builder);
            ConfigureWorkspace<FilpridePurchaseOrder>(builder);
            ConfigureWorkspace<FilprideReceivingReport>(builder);
            ConfigureWorkspace<FilprideCustomerOrderSlip>(builder);
            ConfigureWorkspace<FilprideDeliveryReceipt>(builder);
            ConfigureWorkspace<FilprideServiceInvoice>(builder);
            ConfigureWorkspace<FilprideSalesInvoice>(builder);
            ConfigureWorkspace<FilprideCollectionReceipt>(builder);
            ConfigureWorkspace<FilprideProvisionalReceipt>(builder);
            ConfigureWorkspace<FilprideDebitMemo>(builder);
            ConfigureWorkspace<FilprideCreditMemo>(builder);

            builder.Entity<FilprideGeneralLedgerBook>().HasIndex(x => new { x.WorkspaceTugboatId, x.Date });
            builder.Entity<FilprideGeneralLedgerBook>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideGLPeriodBalance>().HasQueryFilter(TugboatWorkspace.BalanceFilterName,
                x => x.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideGLSubAccountBalance>().HasQueryFilter(TugboatWorkspace.BalanceFilterName,
                x => x.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideMonthlyNibit>().HasQueryFilter(TugboatWorkspace.BalanceFilterName,
                x => x.WorkspaceTugboatId == WorkspaceTugboatId);

            builder.Entity<FilprideCheckVoucherDetail>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CheckVoucherHeader!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideJournalVoucherDetail>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.JournalVoucherHeader!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideTugboatPayment>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.Voucher.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideTugboatPaymentAllocation>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.Payment.Voucher.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideCVTradePayment>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CV.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideMultipleCheckVoucherPayment>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CheckVoucherHeaderPayment!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideCollectionReceiptDetail>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.FilprideCollectionReceipt!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideBookAtlDetail>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CustomerOrderSlip!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideAuthorityToLoad>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CustomerOrderSlip!.WorkspaceTugboatId == WorkspaceTugboatId
                    || x.Details.Any(d => d.CustomerOrderSlip!.WorkspaceTugboatId == WorkspaceTugboatId));
            builder.Entity<FilprideCOSAppointedSupplier>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CustomerOrderSlip!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilprideDeliveryReceiptDetail>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.CustomerOrderSlip!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<FilpridePOActualPrice>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.PurchaseOrder!.WorkspaceTugboatId == WorkspaceTugboatId);
            builder.Entity<JvAmortizationSetting>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || x.JvHeader.WorkspaceTugboatId == WorkspaceTugboatId);
        }

        private void ConfigureWorkspace<TEntity>(ModelBuilder builder) where TEntity : class
        {
            builder.Entity<TEntity>().HasIndex(nameof(BaseEntity.WorkspaceTugboatId));
            builder.Entity<TEntity>().HasQueryFilter(TugboatWorkspace.FilterName,
                x => !WorkspaceTugboatId.HasValue || EF.Property<int?>(x, nameof(BaseEntity.WorkspaceTugboatId)) == WorkspaceTugboatId);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            if (ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && (x.Metadata.FindProperty(nameof(BaseEntity.WorkspaceTugboatId)) != null
                    || x.Metadata.GetForeignKeys().Any(f => f.PrincipalEntityType.FindProperty(nameof(BaseEntity.WorkspaceTugboatId)) != null))))
            {
                throw new InvalidOperationException("Use SaveChangesAsync for workspace-aware accounting changes.");
            }

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ChangeTracker.DetectChanges();
            foreach (EntityEntry entry in ChangeTracker.Entries().Where(x => x.Entity is BaseEntity or FilprideCustomerOrderSlip).ToList())
            {
                PropertyEntry ownership = entry.Property(nameof(BaseEntity.WorkspaceTugboatId));
                if (entry.State == EntityState.Added)
                {
                    // Background copies retain their source ownership in the consolidated scope.
                    ownership.CurrentValue = WorkspaceTugboatId ?? ownership.CurrentValue;
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    PropertyValues stored = await entry.GetDatabaseValuesAsync(cancellationToken)
                        ?? throw new InvalidOperationException("The transaction no longer exists.");
                    int? storedWorkspace = stored.GetValue<int?>(nameof(BaseEntity.WorkspaceTugboatId));
                    if (WorkspaceTugboatId.HasValue && storedWorkspace != WorkspaceTugboatId)
                    {
                        throw new InvalidOperationException("The transaction belongs to another tugboat workspace.");
                    }

                    ownership.CurrentValue = storedWorkspace;
                    ownership.IsModified = false;
                }
            }

            await ValidateWorkspaceReferencesAsync(cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private async Task ValidateWorkspaceReferencesAsync(CancellationToken ct)
        {
            var storedWorkspaces = new Dictionary<object, int?>(ReferenceEqualityComparer.Instance);
            async Task<int?> GetOwnershipAsync(object entity)
            {
                if (storedWorkspaces.TryGetValue(entity, out int? ownership))
                {
                    return ownership;
                }

                EntityEntry parentEntry = Entry(entity);
                ownership = parentEntry.State == EntityState.Added
                    ? (int?)parentEntry.Property(nameof(BaseEntity.WorkspaceTugboatId)).CurrentValue
                    : (await parentEntry.GetDatabaseValuesAsync(ct)
                        ?? throw new InvalidOperationException("A related transaction no longer exists."))
                        .GetValue<int?>(nameof(BaseEntity.WorkspaceTugboatId));
                storedWorkspaces.Add(entity, ownership);
                return ownership;
            }

            foreach (EntityEntry entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
            {
                bool hasOwnership = entry.Metadata.FindProperty(nameof(BaseEntity.WorkspaceTugboatId)) != null;
                int? expected = hasOwnership ? (int?)entry.Property(nameof(BaseEntity.WorkspaceTugboatId)).CurrentValue : WorkspaceTugboatId;
                if (expected is <= 0)
                {
                    throw new InvalidOperationException("Select a valid company-owned tugboat workspace.");
                }
                bool hasExpected = hasOwnership || WorkspaceTugboatId.HasValue;
                if (entry.Entity is FilprideGeneralLedgerBook
                    && (WorkspaceTugboatId.HasValue && expected != WorkspaceTugboatId
                        || entry.State != EntityState.Added && expected != await GetOwnershipAsync(entry.Entity)))
                {
                    throw new InvalidOperationException("Ledger entries must retain their source workspace.");
                }
                foreach (IForeignKey foreignKey in entry.Metadata.GetForeignKeys()
                    .Where(x => x.PrincipalEntityType.FindProperty(nameof(BaseEntity.WorkspaceTugboatId)) != null))
                {
                    object?[] keys = foreignKey.Properties.Select(x => entry.Property(x.Name).CurrentValue).ToArray();
                    if (keys.Any(x => x == null))
                    {
                        continue;
                    }

                    object? parent = foreignKey.DependentToPrincipal == null ? null : entry.Reference(foreignKey.DependentToPrincipal.Name).CurrentValue;
                    parent ??= await FindAsync(foreignKey.PrincipalEntityType.ClrType, keys, ct);
                    if (parent == null)
                    {
                        throw new InvalidOperationException("A related transaction is unavailable.");
                    }

                    int? parentWorkspace = await GetOwnershipAsync(parent);
                    if (hasExpected && expected != parentWorkspace)
                    {
                        throw new InvalidOperationException("Related transactions must belong to the same workspace.");
                    }

                    expected = parentWorkspace;
                    hasExpected = true;
                }

                if (entry.Entity is FilprideCVTradePayment payment)
                {
                    FilprideCheckVoucherHeader voucher = payment.CV ?? await FilprideCheckVoucherHeaders.FindAsync([payment.CheckVoucherId], ct)
                        ?? throw new InvalidOperationException("The payment voucher is unavailable.");
                    BaseEntity? source = payment.DocumentType switch
                    {
                        "RR" => await FilprideReceivingReports.FindAsync([payment.DocumentId], ct),
                        "DR" => await FilprideDeliveryReceipts.FindAsync([payment.DocumentId], ct),
                        _ => null
                    };
                    if (source == null || await GetOwnershipAsync(source) != await GetOwnershipAsync(voucher))
                    {
                        throw new InvalidOperationException("Payment sources must belong to the voucher's workspace.");
                    }
                }
            }
        }
    }
}
