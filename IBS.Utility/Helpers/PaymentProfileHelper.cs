using System.ComponentModel.DataAnnotations;
using IBS.Utility.MSAP.Constants;

namespace IBS.Utility.Helpers
{
    public static class PaymentProfileHelper
    {
        public static string? Validate(
            object model,
            string? address,
            string? tin,
            string? vatType,
            string? taxType,
            decimal? withholdingTaxPercent,
            bool required,
            string subjectLabel)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), results, true))
            {
                return results[0].ErrorMessage;
            }

            if (!required && string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(tin)
                && string.IsNullOrWhiteSpace(vatType) && string.IsNullOrWhiteSpace(taxType)
                && !withholdingTaxPercent.HasValue)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(tin))
            {
                return $"Complete the {subjectLabel}'s payee address and TIN before payment.";
            }

            if (vatType is not (MsapConstants.VatType_Vatable or MsapConstants.VatType_ZeroRated or MsapConstants.VatType_Exempt)
                || taxType is not (MsapConstants.TaxType_WithTax or MsapConstants.TaxType_Exempt))
            {
                return $"Select valid VAT and withholding classifications for the {subjectLabel}.";
            }

            if (taxType == MsapConstants.TaxType_WithTax
                && (!withholdingTaxPercent.HasValue || WithholdingTaxHelper.GetAccountNumberByPercent(withholdingTaxPercent.Value) == null))
            {
                return $"Select a supported withholding rate for the {subjectLabel}.";
            }

            return null;
        }

        public static (string? Address, string? Tin, decimal? WithholdingTaxPercent) Normalize(
            string? address,
            string? tin,
            string? taxType,
            decimal? withholdingTaxPercent)
        {
            var normalizedPercent = taxType == MsapConstants.TaxType_Exempt ? 0m : withholdingTaxPercent;
            return (address?.Trim(), tin?.Trim(), normalizedPercent);
        }

        public static (decimal Gross, decimal BaseAmount, decimal InputVat, decimal Withholding, decimal Net) ComputePayableBreakdown(
            string vatType,
            string taxType,
            decimal withholdingRate,
            decimal grossAmount)
        {
            var gross = Math.Round(grossAmount, 2, MidpointRounding.AwayFromZero);
            var vatable = vatType == MsapConstants.VatType_Vatable;
            var baseAmount = vatable ? Math.Round(gross / 1.12m, 2, MidpointRounding.AwayFromZero) : gross;
            var inputVat = gross - baseAmount;
            var withholding = taxType == MsapConstants.TaxType_WithTax
                ? Math.Round(baseAmount * withholdingRate, 2, MidpointRounding.AwayFromZero)
                : 0m;
            if (gross <= 0m || gross > 99999999999999.99m || withholding >= gross)
            {
                throw new InvalidOperationException("The payable item must have a positive, valid payable amount.");
            }
            return (gross, baseAmount, inputVat, withholding, gross - withholding);
        }
    }
}
