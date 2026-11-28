namespace PawsNClaws.Application.Features.Products;

public sealed class RetailTax(bool vatRegistered)
{
    public const decimal StandardVatRate = 0.12m;
    public decimal Rate => vatRegistered ? StandardVatRate : 0m;
    public string PriceLabel => vatRegistered ? "VAT-inclusive selling price" : "Selling price (non-VAT)";

    public decimal VatIncludedIn(decimal gross) =>
        decimal.Round(gross * Rate / (1m + Rate), 2, MidpointRounding.AwayFromZero);
}
