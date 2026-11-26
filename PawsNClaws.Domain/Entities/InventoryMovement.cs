using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Domain.Entities;

public sealed class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public InventoryMovementType MovementType { get; set; }

    public StockOutReason? StockOutReason { get; set; }

    public decimal QuantityChange { get; set; }

    public decimal QuantityBefore { get; set; }

    public decimal QuantityAfter { get; set; }

    public Guid? ReversesMovementId { get; set; }
    public Guid? UsageEstimateId { get; set; }
    public ICollection<InventoryMovementAllocation> Allocations { get; set; } = new List<InventoryMovementAllocation>();

    public string? Reason { get; set; }

    public Guid PerformedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public Guid OperationId { get; set; }
        = Guid.NewGuid();

    public Guid? StockOutRequestId { get; set; }

    public StockOutRequest? StockOutRequest { get; set; }

    public ICollection<InventoryMovementEvidence> Evidence
    { get; set; } = new List<InventoryMovementEvidence>();
}
