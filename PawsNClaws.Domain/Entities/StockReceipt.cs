namespace PawsNClaws.Domain.Entities;

public sealed class StockReceipt
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ActorId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid SupplierId { get; set; }
    public string Supplier { get; set; } = "";
    public string? Reference { get; set; }
    public string? Lot { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Delivered { get; set; }
    public decimal Accepted { get; set; }
    public decimal Rejected { get; set; }
    public decimal? UnitCost { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public ICollection<StockReceiptEvidence> Evidence { get; set; } = new List<StockReceiptEvidence>();
}

public sealed class InventorySupplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
}

public sealed class GroomingUsageEstimate
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ActorId { get; set; }
    public string JobReference { get; set; } = "";
    public decimal Quantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class StockReceiptEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockReceiptId { get; set; }
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = "";
}
