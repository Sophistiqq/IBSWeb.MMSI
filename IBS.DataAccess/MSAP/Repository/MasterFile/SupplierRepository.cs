using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.MasterFile
{
    public class SupplierRepository(MsapDbContext db): Repository<Supplier>(db), ISupplierRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<string> GenerateCodeAsync(CancellationToken cancellationToken = default)
        {
            var lastSupplier = await _db
                .Suppliers
                .OrderByDescending(s => s.SupplierId)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastSupplier == null)
            {
                return "S000001";
            }

            var lastCode = lastSupplier.SupplierCode!;
            var numericPart = lastCode.Substring(1);

            // Parse the numeric part and increment it by one
            var incrementedNumber = int.Parse(numericPart) + 1;

            // Format the incremented number with leading zeros and concatenate with the letter part
            return $"{lastCode[0]}{incrementedNumber:D6}"; //e.g S000002
        }

        public async Task<bool> IsSupplierExistAsync(string supplierName, string category, string company, CancellationToken cancellationToken = default)
        {
            return await _db.Suppliers
                .AnyAsync(s => s.Company == company && s.SupplierName == supplierName && s.Category == category, cancellationToken);
        }

        public async Task<bool> IsTinNoExistAsync(string tin, string branch, string category, string company, CancellationToken cancellationToken = default)
        {
            if (tin == "000-000-000-00000")
            {
                return false;
            }

            return await _db.Suppliers
                .AnyAsync(s =>
                    s.Company == company &&
                    s.SupplierTin == tin &&
                    s.Branch == branch &&
                    s.Category == category,
                    cancellationToken);
        }

        public async Task<string> SaveProofOfRegistration(IFormFile file, string localPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(localPath))
            {
                Directory.CreateDirectory(localPath);
            }

            var fileName = Path.GetFileName(file.FileName);
            var fileSavePath = Path.Combine(localPath, fileName);

            await using FileStream stream = new(fileSavePath, FileMode.Create);
            await file.CopyToAsync(stream, cancellationToken);

            return fileSavePath;
        }

        public async Task UpdateAsync(Supplier model, CancellationToken cancellationToken = default)
        {
            var existingSupplier = await _db.Suppliers
                .FirstOrDefaultAsync(x => x.SupplierId == model.SupplierId, cancellationToken)
                                   ?? throw new InvalidOperationException($"Supplier with id '{model.SupplierId}' not found.");

            existingSupplier.Category = model.Category;
            existingSupplier.SupplierName = model.SupplierName;
            existingSupplier.SupplierAddress = model.SupplierAddress;
            existingSupplier.SupplierTin = model.SupplierTin;
            existingSupplier.Branch = model.Branch;
            existingSupplier.SupplierTerms = model.SupplierTerms;
            existingSupplier.VatType = model.VatType;
            existingSupplier.TaxType = model.TaxType;
            existingSupplier.WithholdingTaxPercent = model.WithholdingTaxPercent;
            existingSupplier.ZipCode = model.ZipCode;
            existingSupplier.RequiresPriceAdjustment = model.RequiresPriceAdjustment;
            existingSupplier.TradeName = model.TradeName;

            if (model.ProofOfRegistrationFilePath != null && existingSupplier.ProofOfRegistrationFilePath != model.ProofOfRegistrationFilePath)
            {
                existingSupplier.ProofOfRegistrationFilePath = model.ProofOfRegistrationFilePath;
            }

            if (model.ProofOfExemptionFilePath != null && existingSupplier.ProofOfExemptionFilePath != model.ProofOfExemptionFilePath)
            {
                existingSupplier.ProofOfExemptionFilePath = model.ProofOfExemptionFilePath;
            }

            if (_db.ChangeTracker.HasChanges())
            {
                existingSupplier.EditedBy = model.EditedBy;
                existingSupplier.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("No data changes!");
            }
        }

        public async Task<List<SelectListItem>> GetTradeSupplierListAsyncById(string company, CancellationToken cancellationToken = default)
        {
            return await _db.Suppliers
                .OrderBy(s => s.SupplierCode)
                .Where(s => s.IsActive && s.Category == "Trade")
                .Select(s => new SelectListItem
                {
                    Value = s.SupplierId.ToString(),
                    Text = s.SupplierCode + " " + s.SupplierName
                })
                .ToListAsync(cancellationToken);
        }
    }
}
