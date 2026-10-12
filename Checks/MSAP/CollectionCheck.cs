using System.Linq.Expressions;
using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class CollectionCheck
    {
        public static async Task RunAsync()
        {
            var today = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
            var receipt = new Collection { MsapCollectionId = 1, MsapCollectionNumber = "CHECK", Date = today.AddDays(-3), CheckDate = today.AddDays(-2), CheckAmount = 100, Amount = 100 };
            var bank = new BankAccount { BankAccountId = 1, Company = MsapConstants.Company_MMSI, Bank = "Bank", AccountName = "MMSI", AccountNo = "123" };
            bool closed = false;
            int audits = 0;
            var collections = Proxy<ICollectionRepository>((method, args) => method.Name == "GetForUpdateAsync" ? Task.FromResult<Collection?>(receipt) : throw new NotSupportedException(method.Name));
            var periods = Proxy<IPostedPeriodRepository>((method, args) => Task.FromResult(closed));
            var banks = Proxy<IBankAccountRepository>((method, args) =>
            {
                var filter = (Expression<Func<BankAccount, bool>>)args![0]!;
                return Task.FromResult(filter.Compile()(bank) ? bank : null);
            });
            var audit = Proxy<IAuditTrailRepository>((method, args) => { audits++; return Task.CompletedTask; });
            var work = Proxy<IUnitOfWork>((method, args) => method.Name switch
            {
                "get_Collection" => collections,
                "get_PostedPeriod" => periods,
                "get_BankAccount" => banks,
                "get_AuditTrail" => audit,
                "ExecuteInTransactionAsync" => ((Func<Task>)args![0]!)(),
                "SaveAsync" => Task.CompletedTask,
                _ => throw new NotSupportedException(method.Name)
            });
            var service = new CollectionService(work, NullLogger<CollectionService>.Instance);
            async Task Expect(CollectionBankingAction action, DateOnly date, bool success, int? bankId = 1)
            {
                int before = audits;
                var result = await service.UpdateBankingAsync(1, action, date, bankId, "check", default);
                Check(result.IsSuccess == success, $"Unexpected {action} result: {result.Message}");
                Check(audits == before + (success ? 1 : 0), "Only successful transitions should record an audit.");
            }
            await Expect(CollectionBankingAction.Clear, today, false);
            await Expect(CollectionBankingAction.ReturnCheck, today, false);
            await Expect(CollectionBankingAction.Deposit, receipt.Date, false);
            await Expect(CollectionBankingAction.Deposit, today.AddDays(1), false);
            await Expect(CollectionBankingAction.Deposit, today, false, null);
            await Expect(CollectionBankingAction.Deposit, today, false, 99);
            bank.Company = "Filpride";
            await Expect(CollectionBankingAction.Deposit, today, false);
            bank.Company = MsapConstants.Company_MMSI;
            closed = true;
            await Expect(CollectionBankingAction.Deposit, today, false);
            closed = false;
            await Expect(CollectionBankingAction.Deposit, today.AddDays(-1), true);
            Check(receipt.PaymentStatus == "Deposited" && receipt.BankId == 1, "Deposit was not recorded.");
            await Expect(CollectionBankingAction.Deposit, today, false);
            await Expect(CollectionBankingAction.ReturnCheck, today.AddDays(-2), false);
            await Expect(CollectionBankingAction.ReturnCheck, today, true);
            Check(receipt.PaymentStatus == "Returned" && receipt.DepositDate == null && receipt.Amount == 100, "Return must retain the receipt and require redeposit.");
            await Expect(CollectionBankingAction.Clear, today, false);
            await Expect(CollectionBankingAction.Deposit, today.AddDays(-1), false);
            await Expect(CollectionBankingAction.Deposit, today, true);
            Check(receipt.PaymentStatus == "Redeposited", "Redeposit was not recorded.");
            await Expect(CollectionBankingAction.Clear, today.AddDays(-1), false);
            await Expect(CollectionBankingAction.Clear, today, true);
            Check(receipt.PaymentStatus == "Cleared", "Clearing was not recorded.");
            await Expect(CollectionBankingAction.ReturnCheck, today, false);
            await Expect(CollectionBankingAction.Clear, today, false);
            receipt.ClearedDate = null;
            receipt.CheckAmount = 0;
            await Expect(CollectionBankingAction.ReturnCheck, today, false);
            await Expect((CollectionBankingAction)99, today, false);
            Console.WriteLine("PASS: collection deposit, return, redeposit, clearing, date guards, bank ownership, closed periods and audits.");
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CheckProxy>();
            ((CheckProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }

        public class CheckProxy : DispatchProxy
        {
            public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return Handler(targetMethod!, args);
            }
        }
    }
}
