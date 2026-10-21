namespace IBS.Models.MSAP.MasterFile
{
    public class BankAccountViewModel : BankAccount
    {
        public BankAccountViewModel() { }

        public BankAccountViewModel(BankAccount entity)
        {
            BankAccountId = entity.BankAccountId;
            BankAccountCode = entity.BankAccountCode;
            Bank = entity.Bank;
            Branch = entity.Branch;
            AccountNo = entity.AccountNo;
            AccountName = entity.AccountName;
            CreatedBy = entity.CreatedBy;
            CreatedDate = entity.CreatedDate;
            Company = entity.Company;
        }
    }
}
