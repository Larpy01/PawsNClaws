namespace PawsNClaws.Domain.Entities;

public sealed class InventoryStock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public decimal QuantityOnHand { get; set; }

    public decimal ReorderLevel { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public byte[] RowVersion { get; set; }
        = Array.Empty<byte>();
}
