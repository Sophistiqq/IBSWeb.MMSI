using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.Models.MSAP.MasterFile;

namespace IBS.DataAccess.MSAP.Repository.MasterFile
{
    public class EmployeeRepository(MsapDbContext db): Repository<Employee>(db), IEmployeeRepository
    {
        private readonly MsapDbContext _db = db;
    }
}
