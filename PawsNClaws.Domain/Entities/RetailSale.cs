using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Domain.Entities;

public sealed class RetailSale
{
    public Guid Id { get; set; }
    public Guid RecordedByEmployeeId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public decimal Total { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal Tendered { get; set; }
    public RetailPaymentMethod PaymentMethod { get; set; }
    public string? OnlinePaymentReference { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByEmployeeId { get; set; }
    public string? VoidReason { get; set; }
    public bool ReturnsQuarantined { get; set; }
    public ICollection<RetailSaleLine> Lines { get; set; } = [];
}

public sealed class RetailSaleLine
{
    public Guid SaleId { get; set; }
    public Guid ProductId { get; set; }
    public Guid InventoryMovementId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
