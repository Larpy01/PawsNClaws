using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Domain.Entities;

public sealed class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public InventoryMovementType MovementType { get; set; }

    public StockOutReason? StockOutReason { get; set; }

    public int QuantityChange { get; set; }

    public int QuantityBefore { get; set; }

    public int QuantityAfter { get; set; }

    public string? Reason { get; set; }

    public Guid PerformedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public Guid OperationId { get; set; }
        = Guid.NewGuid();

    public ICollection<InventoryMovementEvidence> Evidence
    { get; set; } = new List<InventoryMovementEvidence>();
}
