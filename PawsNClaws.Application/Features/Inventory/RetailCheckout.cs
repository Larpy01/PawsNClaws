using System.Security.Claims;
using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Application.Features.Inventory;

public sealed class RetailLineInput
{
    public Guid? ProductId { get; set; }
    public int? Quantity { get; set; }
}

public sealed record RetailProduct(Guid Id, string Name, string Sku, decimal Price, decimal Usable);
public sealed record RetailSoldLine(string Name, string Sku, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}

public sealed record RetailSoldLot(string Product, string Sku, Guid BatchId, string? Lot, DateOnly? Expiry, decimal Quantity);

public sealed record RetailSaleView(Guid Id, DateTime RecordedAtUtc, decimal Total, decimal Tendered,
    IReadOnlyList<RetailSoldLine> Lines, DateTime? VoidedAtUtc, string? VoidReason, bool ReturnsQuarantined,
    IReadOnlyList<RetailSoldLot> Lots, decimal? VatRate, decimal? VatAmount,
    RetailPaymentMethod PaymentMethod, string? OnlinePaymentReference)
{
    public decimal Change => Tendered - Total;
}

public sealed record RetailSaleSummary(Guid Id, DateTime RecordedAtUtc, decimal Total, DateTime? VoidedAtUtc,
    RetailPaymentMethod PaymentMethod);

public interface IRetailCheckout
{
    Task<IReadOnlyList<RetailProduct>> ProductsAsync(ClaimsPrincipal actor, CancellationToken token);
    Task<IReadOnlyList<RetailSaleSummary>> ListSalesAsync(ClaimsPrincipal actor, int page, CancellationToken token);
    Task VoidSaleAsync(ClaimsPrincipal actor, Guid id, string? reason, bool stockVerified, bool cashReturned,
        bool onlineReturned, bool quarantineReturned, CancellationToken token);
    Task<RetailSaleView?> FindSaleAsync(ClaimsPrincipal actor, Guid id, CancellationToken token);
    Task CheckoutAsync(ClaimsPrincipal actor, Guid operationId, IReadOnlyList<RetailLineInput> lines,
        decimal tendered, decimal? expectedTotal, RetailPaymentMethod paymentMethod, string? onlinePaymentReference,
        CancellationToken token);
}
