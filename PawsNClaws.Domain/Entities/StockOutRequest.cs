using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Domain.Entities;

public sealed class StockOutRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public Guid? BatchId { get; set; }
    public StockOutReason ReasonCategory { get; set; }
    public string Reason { get; set; } = string.Empty;
    public StockOutRequestStatus Status { get; set; } = StockOutRequestStatus.Pending;
    public Guid SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid SubmissionOperationId { get; set; } = Guid.NewGuid();
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionComment { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public InventoryMovement? Movement { get; set; }
    public ICollection<StockOutRequestEvidence> Evidence { get; set; } = new List<StockOutRequestEvidence>();
}
