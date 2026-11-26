
namespace PawsNClaws.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SKU { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
    public string Unit { get; set; } = "Each";
    public bool IsSellable { get; set; } = true;
    public bool IsConsumable { get; set; }
    public bool TracksExpiry { get; set; }
    public bool IsVisibleOnline { get; set; }
    public string? Category { get; set; }
    public string? Barcode { get; set; }
    public string? Description { get; set; }
    public decimal? UnitCost { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
