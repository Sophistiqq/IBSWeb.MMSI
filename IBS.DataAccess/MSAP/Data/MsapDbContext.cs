using ApplicationUser = IBS.Models.ApplicationUser;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Data
{
    public class MsapDbContext(DbContextOptions<MsapDbContext> options): DbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSnakeCaseNamingConvention();
        }

        public DbSet<ApplicationUser> ApplicationUsers { get; set; }

        public DbSet<ApplicationUser> Users { get; set; }

        public DbSet<IdentityRole> Roles { get; set; }

        public DbSet<IdentityUserRole<string>> UserRoles { get; set; }

        public DbSet<AppSetting> AppSettings { get; set; }

        public DbSet<AuditTrail> AuditTrails { get; set; }

        #region--Master File

        public DbSet<Customer> Customers { get; set; }

        public DbSet<Supplier> Suppliers { get; set; }

        public DbSet<Employee> Employees { get; set; }

        public DbSet<Terms> Terms { get; set; }

        #endregion

        #region --MSAP
        public DbSet<IBS.Models.MSAP.Books.SalesBook> MsapSalesBooks { get; set; }

        public DbSet<Billing> MsapBillings { get; set; }
        public DbSet<Collection> MsapCollections { get; set; }
        public DbSet<DispatchTicket> MsapDispatchTickets { get; set; }
        public DbSet<CustomerOrderSlip> MsapCustomerOrderSlips { get; set; }
        public DbSet<VesselSchedule> MsapVesselSchedules { get; set; }
        public DbSet<VesselScheduleServiceEntry> MsapVesselScheduleServiceEntries { get; set; }
        public DbSet<TariffRate> MsapTariffRates { get; set; }

        public DbSet<BafRate> MsapBafRates { get; set; }
        public DbSet<RateUpload> MsapRateUploads { get; set; }

        public DbSet<OtherTugCharge> MsapOtherTugCharges { get; set; }
        public DbSet<OtherTugChargeLine> MsapOtherTugChargeLines { get; set; }

        public DbSet<CollectionBill> MsapCollectionBills { get; set; }

        #endregion

        #region --Master File Entity

        public DbSet<Service> MsapServices { get; set; }
        public DbSet<TugboatOwner> MsapTugboatOwners { get; set; }
        public DbSet<Port> MsapPorts { get; set; }
        public DbSet<Principal> MsapPrincipals { get; set; }
        public DbSet<Terminal> MsapTerminals { get; set; }
        public DbSet<Tugboat> MsapTugboats { get; set; }
        public DbSet<TugMaster> MsapTugMasters { get; set; }
        public DbSet<UserAccess> MsapUserAccesses { get; set; }
        public DbSet<Vessel> MsapVessels { get; set; }
        #endregion --Master File Entities

        public DbSet<Company> Companies { get; set; }

        public DbSet<BankAccount> BankAccounts { get; set; }

        public DbSet<MsapPostedPeriod> MsapPostedPeriods { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.HasDefaultSchema("msap");
            builder.Entity<CustomerOrderSlip>().ToTable("mmsi_customer_order_slips");
            builder.Entity<DispatchTicket>().ToTable("mmsi_dispatch_tickets");
            builder.Entity<DispatchTicket>().Property(t => t.AccountingSourceId).HasDefaultValueSql("gen_random_uuid()");
            builder.Entity<DispatchTicket>().HasIndex(t => t.AccountingSourceId).IsUnique();
            builder.Entity<IBS.Models.MSAP.Books.SalesBook>(book =>
            {
                book.ToTable("mmsi_sales_books");
                book.HasIndex(b => new { b.DocumentId, b.BillingRevision }).IsUnique();
                book.HasIndex(b => new { b.Company, b.TransactionDate });
                book.HasOne<Billing>().WithMany().HasForeignKey(b => b.DocumentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<Billing>().ToTable("mmsi_billings");
            builder.Entity<Billing>().HasOne(b => b.BasicBilling).WithOne(b => b.BafBilling)
                .HasForeignKey<Billing>(b => b.BasicBillingId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Collection>().ToTable("mmsi_collections");
            builder.Entity<OtherTugCharge>(charge =>
            {
                charge.ToTable("mmsi_other_tug_charges", table => table.HasCheckConstraint("ck_other_tug_charge_confirmation",
                    "revision >= 1 AND length(trim(provider_reference)) > 0 AND ((confirmed_at IS NULL AND confirmed_by_user_id IS NULL AND confirmed_by_name IS NULL) OR "
                    + "(confirmed_at IS NOT NULL AND confirmed_by_user_id IS NOT NULL AND confirmed_by_name IS NOT NULL))"));
                charge.HasIndex(x => new { x.TugboatOwnerId, x.ProviderReference }).IsUnique();
                charge.HasOne(x => x.TugboatOwner).WithMany().HasForeignKey(x => x.TugboatOwnerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OtherTugChargeLine>(line =>
            {
                line.ToTable("mmsi_other_tug_charge_lines", table => table.HasCheckConstraint("ck_other_tug_charge_line_amount", "amount >= 0 AND amount <= 99999999999999.99"));
                line.Property(x => x.Amount).HasPrecision(18, 2);
                line.HasIndex(x => x.DispatchTicketId).IsUnique();
                line.HasOne(x => x.Charge).WithMany(x => x.Lines).HasForeignKey(x => x.OtherTugChargeId).OnDelete(DeleteBehavior.Restrict);
                line.HasOne(x => x.Ticket).WithOne(x => x.OtherTugChargeLine).HasForeignKey<OtherTugChargeLine>(x => x.DispatchTicketId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<CollectionBill>().ToTable("mmsi_collection_bills");
            builder.Entity<Port>().ToTable("mmsi_ports");
            builder.Entity<Terminal>().ToTable("mmsi_terminals");
            builder.Entity<Vessel>().ToTable("mmsi_vessels");
            builder.Entity<Service>().ToTable("mmsi_services");
            builder.Entity<Principal>().ToTable("mmsi_principals");
            builder.Entity<TariffRate>().ToTable("mmsi_tariff_rates");
            builder.Entity<BafRate>().ToTable("mmsi_baf_rates");
            builder.Entity<BafRate>().HasIndex(rate => new { rate.CustomerId, rate.TerminalId, rate.ServiceId, rate.AsOfDate });
            builder.Entity<RateUpload>().ToTable("mmsi_rate_uploads");
            builder.Entity<RateUpload>().HasIndex(upload => new { upload.Company, upload.UploadedAt });
            builder.Entity<Tugboat>().ToTable("mmsi_tugboats");
            builder.Entity<TugMaster>().ToTable("mmsi_tug_masters");
            builder.Entity<TugboatOwner>().ToTable("mmsi_tugboat_owners");
            builder.Entity<VesselSchedule>().ToTable("mmsi_vessel_schedules");
            builder.Entity<VesselScheduleServiceEntry>().ToTable("mmsi_vessel_schedule_service_entries");
            builder.Entity<UserAccess>().ToTable("mmsi_user_accesses");
            builder.Entity<MsapPostedPeriod>().ToTable("mmsi_posted_periods");
            builder.Entity<ApplicationUser>().Ignore(u => u.UserNotifications)
                .ToTable("AspNetUsers", "public", table => table.ExcludeFromMigrations());
            builder.Entity<IdentityRole>()
                .ToTable("AspNetRoles", "public", table => table.ExcludeFromMigrations());
            builder.Entity<IdentityUserRole<string>>().HasKey(role => new { role.UserId, role.RoleId });
            builder.Entity<IdentityUserRole<string>>()
                .ToTable("AspNetUserRoles", "public", table => table.ExcludeFromMigrations());

            #region-- Master File

            // Company
            builder.Entity<Company>(c =>
            {
                c.HasIndex(c => c.CompanyCode).IsUnique();
                c.HasIndex(c => c.CompanyName).IsUnique();
            });

            #endregion

            #region-- Master File

            // Customer
            builder.Entity<Customer>(c =>
            {
                c.ToTable(table => table.HasCheckConstraint("ck_customers_default_cos_discount_percent",
                    "default_cos_discount_percent BETWEEN 0 AND 100"));
                c.Property(x => x.DefaultCosDiscountPercent).HasDefaultValue(0m);
                c.HasIndex(c => c.CustomerCode);
                c.HasIndex(c => c.CustomerName);
            });

            // Supplier
            builder.Entity<Supplier>(s =>
            {
                s.HasIndex(s => s.SupplierCode);
                s.HasIndex(s => s.SupplierName);
            });

            // Employee
            builder.Entity<Employee>(c =>
            {
                c.HasIndex(c => c.EmployeeNumber);
            });

            #endregion

            #region--AppSettings

            builder.Entity<AppSetting>(a =>
            {
                a.HasIndex(a => a.SettingKey).IsUnique();
            });

            #endregion

            #region --MSAP

            builder.Entity<Billing>(b =>
            {
                b.Property(x => x.Revision).HasDefaultValue(1);
                b.ToTable(table => table.HasCheckConstraint("ck_billings_approval_evidence",
                    "revision >= 1 AND ((approved_revision IS NULL AND approved_by_user_id IS NULL AND approved_at IS NULL)"
                    + " OR (approved_revision IS NOT NULL AND approved_revision BETWEEN 1 AND revision AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))"));
                b.HasOne(x => x.ApprovedByUser).WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.Year, x.MsapBillingNumber, x.Company }).IsUnique();
                b.HasIndex(x => x.Date);
                b.HasIndex(x => x.CustomerId);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CollectionId);

                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Vessel).WithMany().HasForeignKey(x => x.VesselId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Port).WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Principal).WithMany().HasForeignKey(x => x.PrincipalId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.CustomerOrderSlip).WithMany().HasForeignKey(x => x.CustomerOrderSlipId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Collection>(c =>
            {
                c.HasIndex(x => new { x.MsapCollectionNumber, x.Company }).IsUnique();
                c.HasIndex(x => x.Date);
                c.HasIndex(x => x.CustomerId);

                c.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                c.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankId).OnDelete(DeleteBehavior.Restrict);

                c.HasMany(x => x.PaidBills)
                    .WithOne(x => x.Collection)
                    .HasForeignKey(x => x.CollectionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<CustomerOrderSlip>(jo =>
            {
                jo.HasIndex(x => x.CustomerId);
                jo.HasIndex(x => x.Status);
                jo.HasIndex(x => x.CustomerOrderSlipNumber);

                jo.HasOne(j => j.Customer).WithMany().HasForeignKey(j => j.CustomerId).OnDelete(DeleteBehavior.Restrict);
                jo.HasOne(j => j.Vessel).WithMany().HasForeignKey(j => j.VesselId).OnDelete(DeleteBehavior.Restrict);
                jo.HasOne(j => j.Port).WithMany().HasForeignKey(j => j.PortId).OnDelete(DeleteBehavior.Restrict);
                jo.HasOne(j => j.Terminal).WithMany().HasForeignKey(j => j.TerminalId).OnDelete(DeleteBehavior.Restrict);
                jo.HasOne(j => j.PreferredTugboat)
                    .WithMany()
                    .HasForeignKey(j => j.PreferredTugboatId)
                    .OnDelete(DeleteBehavior.SetNull);

                jo.Property(j => j.PlannedStartTime).HasColumnType("timestamp without time zone");
                jo.Property(j => j.PlannedEndTime).HasColumnType("timestamp without time zone");
            });

            builder.Entity<VesselSchedule>(vs =>
            {
                vs.Property(x => x.Revision).HasDefaultValue(1);
                vs.ToTable(table =>
                {
                    table.HasCheckConstraint("ck_vessel_schedules_cos_discount_percent", "cos_discount_percent BETWEEN 0 AND 100");
                    table.HasCheckConstraint("ck_vessel_schedules_approval_evidence",
                        "revision >= 1 AND ((approved_revision IS NULL AND approved_by_user_id IS NULL AND approved_at IS NULL)"
                        + " OR (approved_revision IS NOT NULL AND approved_revision BETWEEN 1 AND revision AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");
                });
                vs.HasOne(x => x.ApprovedByUser).WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
                vs.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                vs.HasIndex(x => x.Status);
                vs.HasIndex(x => x.PlannedStart);
                vs.HasIndex(x => new { x.PortId, x.TerminalId });

                vs.HasOne(x => x.Vessel).WithMany().HasForeignKey(x => x.VesselId).OnDelete(DeleteBehavior.Restrict);
                vs.HasOne(x => x.Port).WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.Restrict);
                vs.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
                vs.HasOne(x => x.CustomerOrderSlip).WithMany().HasForeignKey(x => x.CustomerOrderSlipId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<VesselScheduleServiceEntry>(entry =>
            {
                entry.Property(x => x.TariffBasis).HasConversion<short>();
                entry.Property(x => x.BafMethod).HasConversion<short>();
                entry.ToTable(table =>
                {
                    table.HasCheckConstraint("ck_schedule_service_entries_cancellation",
                        "(cancelled_at IS NULL AND cancelled_by_user_id IS NULL AND cancellation_reason IS NULL) OR "
                        + "(cancelled_at IS NOT NULL AND cancelled_by_user_id IS NOT NULL AND length(trim(cancellation_reason)) > 0 AND cancellation_reason IS NOT NULL)");
                    table.HasCheckConstraint("ck_schedule_service_entries_tariff_basis", "tariff_basis IN (1, 2)");
                    table.HasCheckConstraint("ck_schedule_service_entries_baf_method", "baf_method IN (1, 2, 3, 4)");
                    table.HasCheckConstraint("ck_schedule_service_entries_manual_tariff_rate", "manual_tariff_rate >= 0");
                    table.HasCheckConstraint("ck_schedule_service_entries_manual_baf_rate", "manual_baf_rate >= 0");
                });
                entry.HasOne(x => x.VesselSchedule).WithMany(x => x.ServiceEntries)
                    .HasForeignKey(x => x.VesselScheduleId).OnDelete(DeleteBehavior.Restrict);
                entry.HasOne(x => x.Tugboat).WithMany()
                    .HasForeignKey(x => x.TugboatId).OnDelete(DeleteBehavior.Restrict);
                entry.HasOne(x => x.Service).WithMany()
                    .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DispatchTicket>(dt =>
            {
                dt.Property(x => x.ApOtherTugsIncludesVat).HasDefaultValue(true);
                dt.ToTable(table =>
                {
                    table.HasCheckConstraint("ck_dispatch_tickets_raw_duration",
                        "(raw_duration_hours IS NULL OR raw_duration_hours >= 0) AND (raw_duration_minutes IS NULL OR raw_duration_minutes >= 0)");
                });
                dt.HasIndex(x => x.Status);
                dt.HasIndex(x => x.CustomerId);
                dt.HasIndex(x => x.CustomerOrderSlipId);
                dt.HasIndex(x => x.BillingId);
                dt.HasIndex(x => x.VesselScheduleServiceEntryId);

                dt.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.Tugboat).WithMany().HasForeignKey(x => x.TugBoatId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.TugMaster).WithMany().HasForeignKey(x => x.TugMasterId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.Vessel).WithMany().HasForeignKey(x => x.VesselId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.Port).WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.CustomerOrderSlip).WithMany(j => j.DispatchTickets).HasForeignKey(x => x.CustomerOrderSlipId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.Billing).WithMany().HasForeignKey(x => x.BillingId).OnDelete(DeleteBehavior.Restrict);
                dt.HasOne(x => x.ExpectedServiceEntry)
                    .WithMany(e => e.DispatchTickets)
                    .HasForeignKey(x => x.VesselScheduleServiceEntryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Tugboat>(t =>
            {
                t.HasOne(x => x.Port)
                    .WithMany()
                    .HasForeignKey(x => x.PortId)
                    .OnDelete(DeleteBehavior.Restrict);
            });



            builder.Entity<CollectionBill>(cb =>
            {
                cb.HasOne(x => x.Collection).WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
                cb.HasOne(x => x.Billing).WithMany().HasForeignKey(x => x.BillingId).OnDelete(DeleteBehavior.Restrict);
                cb.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AuditTrail>(a =>
            {
                a.HasIndex(x => x.DocumentType);
                a.HasIndex(x => x.RecordId);
                a.HasIndex(x => x.ReferenceNumber);
                a.HasIndex(x => x.Date);
            });

            builder.Entity<MsapPostedPeriod>(p =>
            {
                p.HasIndex(x => new { x.Year, x.Month }).IsUnique();
            });

            #endregion
        }
    }
}
