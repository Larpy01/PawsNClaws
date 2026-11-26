namespace PawsNClaws.Domain.Entities;

public sealed class InventoryBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string? Lot { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public decimal OnHand { get; set; }
    public decimal Quarantined { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class InventoryMovementAllocation
{
    public Guid MovementId { get; set; }
    public Guid BatchId { get; set; }
    public decimal QuantityChange { get; set; }
}

public sealed class InventoryQuarantineEvent
{
    public Guid OperationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid ActorId { get; set; }
    public decimal QuantityChange { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
