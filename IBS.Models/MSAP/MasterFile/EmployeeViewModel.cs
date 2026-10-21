namespace IBS.Models.MSAP.MasterFile
{
    public class EmployeeViewModel : Employee
    {
        public EmployeeViewModel() { }

        public EmployeeViewModel(Employee entity)
        {
            EmployeeId = entity.EmployeeId;
            EmployeeNumber = entity.EmployeeNumber;
            Initial = entity.Initial;
            FirstName = entity.FirstName;
            MiddleName = entity.MiddleName;
            LastName = entity.LastName;
            Suffix = entity.Suffix;
            Address = entity.Address;
            BirthDate = entity.BirthDate;
            TelNo = entity.TelNo;
            SssNo = entity.SssNo;
            TinNo = entity.TinNo;
            PhilhealthNo = entity.PhilhealthNo;
            PagibigNo = entity.PagibigNo;
            Company = entity.Company;
            Department = entity.Department;
            DateHired = entity.DateHired;
            DateResigned = entity.DateResigned;
            Position = entity.Position;
            IsManagerial = entity.IsManagerial;
            Supervisor = entity.Supervisor;
            Status = entity.Status;
            Paygrade = entity.Paygrade;
            Salary = entity.Salary;
            IsActive = entity.IsActive;
        }
    }
}
