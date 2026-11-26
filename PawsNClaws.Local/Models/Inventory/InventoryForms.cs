using System.ComponentModel.DataAnnotations;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Local.Models.Inventory;

public sealed class ReceivingForm
{
    public Guid OperationId { get; set; }
    public Guid ProductId { get; set; }
    [Required, StringLength(150)] public string Supplier { get; set; } = "";
    [StringLength(100)] public string? Reference { get; set; }
    [StringLength(100)] public string? Lot { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Delivered { get; set; }
    public decimal Accepted { get; set; }
    public decimal Rejected { get; set; }
    public decimal? UnitCost { get; set; }
    [StringLength(500)] public string? RejectionReason { get; set; }
    [StringLength(16)] public string? InputUnit { get; set; }
    public List<IFormFile> Photos { get; set; } = [];
}

public sealed class MovementForm
{
    public Guid OperationId { get; set; }
    public InventoryAction Action { get; set; }
    public Guid ProductId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? MovementId { get; set; }
    public decimal Quantity { get; set; }
    public StockOutReason? Category { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    [StringLength(16)] public string? InputUnit { get; set; }
    public List<IFormFile> Photos { get; set; } = [];
}

public sealed class CatalogForm
{
    public Guid ProductId { get; set; }
    [Required, StringLength(16)] public string Unit { get; set; } = "Each";
    public bool Sellable { get; set; }
    public bool Consumable { get; set; }
    public bool TracksExpiry { get; set; }
    public bool Online { get; set; }
    [StringLength(80)] public string? Category { get; set; }
    [StringLength(64)] public string? Barcode { get; set; }
    [StringLength(500)] public string? Description { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal ReorderLevel { get; set; }
}
