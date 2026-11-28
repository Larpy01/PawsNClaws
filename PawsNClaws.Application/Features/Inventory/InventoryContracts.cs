using System.Security.Claims;
using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Application.Features.Inventory;

public enum InventoryAction
{
    Submit, Dispose, Approve, Reject, Cancel, Adjust,
    Quarantine, Release, Sale, Use, ReverseSale, ReverseUsage, EstimateUsage
}

public sealed record EvidenceUpload(string Name, Stream Content);
public sealed record EvidenceFile(string Key, string ContentType, long Length, string Sha256);
public sealed record EvidenceDownload(Stream Content, string ContentType, string Name);

public sealed record CatalogSettings(Guid ProductId, string Unit, bool Sellable, bool Consumable,
    bool TracksExpiry, bool Online, string? Category, string? Barcode, string? Description,
    decimal? UnitCost, decimal ReorderLevel);

public sealed record ReceivingCommand(Guid OperationId, Guid ProductId, string Supplier, string? Reference,
    string? Lot, DateOnly? ExpiryDate, decimal Delivered, decimal Accepted, decimal Rejected,
    decimal? UnitCost, string? RejectionReason, string? InputUnit = null, decimal? SupplierInvoiceVat = null);

public sealed record InventoryCommand(Guid OperationId, InventoryAction Action, Guid ProductId,
    Guid? BatchId, Guid? RequestId, Guid? MovementId, decimal Quantity, StockOutReason? Category,
    string? Reason, string? InputUnit = null);

public sealed record StockLine(Guid ProductId, string Sku, string Name, string Unit,
    decimal OnHand, decimal Usable, decimal Quarantined, decimal ReorderLevel, decimal? RetailPrice);
public sealed record BatchLine(Guid Id, Guid ProductId, string? Lot, DateOnly? Expiry,
    decimal OnHand, decimal Quarantined);
public sealed record EvidenceLink(Guid Id, string Source);
public sealed record RequestLine(Guid Id, Guid ProductId, string Product, decimal Quantity,
    StockOutReason ReasonCategory, string Reason, StockOutRequestStatus Status,
    Guid SubmittedBy, DateTime SubmittedAtUtc, Guid? DecidedBy, DateTime? DecidedAtUtc, string? Decision,
    IReadOnlyList<EvidenceLink> Evidence);
public sealed record MovementLine(Guid Id, Guid ProductId, string Product, InventoryMovementType Type, decimal Change,
    decimal Balance, DateTime AtUtc, string? Reason, Guid? RequestId, IReadOnlyList<EvidenceLink> Evidence);
public sealed record ReceiptLine(Guid Id, string Product, string Supplier, string? Reference,
    decimal Delivered, decimal Accepted, decimal Rejected, DateTime AtUtc,
    IReadOnlyList<EvidenceLink> Evidence, decimal? SupplierInvoiceVat);
public sealed record UsageEstimateLine(Guid Id, Guid ProductId, string Product, string JobReference,
    decimal Quantity, string Unit, bool Posted);
public sealed record InventoryOverview(IReadOnlyList<StockLine> Stock, IReadOnlyList<BatchLine> Batches,
    IReadOnlyList<RequestLine> Requests, IReadOnlyList<MovementLine> Movements, IReadOnlyList<ReceiptLine> Receipts,
    IReadOnlyList<UsageEstimateLine> UsageEstimates);

public sealed record LowStockAlert(Guid ProductId, string Sku, string Name, string Unit,
    decimal OnHand, decimal Usable, decimal ReorderLevel);
public sealed record BatchExpiryAlert(string Sku, string Name, string Unit, string? Lot,
    DateOnly Expiry, decimal OnHand, decimal Quarantined);
public sealed record InventoryAlerts(DateOnly AsOf, DateOnly ExpiringThrough, IReadOnlyList<LowStockAlert> LowStock,
    IReadOnlyList<BatchExpiryAlert> ExpiringSoon, IReadOnlyList<BatchExpiryAlert> Expired);

public sealed record VisitSupply(Guid ProductId, string Name, string Unit, decimal Estimated, decimal? Recorded,
    bool Available, bool Reversed, string? UsageNote);
public sealed record VisitSupplies(Guid VisitId, Guid Revision, string Pet, bool PickedUp, IReadOnlyList<VisitSupply> Items);

public interface IInventoryService
{
    Task<VisitSupplies?> GetVisitSuppliesAsync(ClaimsPrincipal actor, Guid visitId, CancellationToken ct);
    Task PostVisitSupplyAsync(ClaimsPrincipal actor, Guid visitId, Guid revision, Guid productId, decimal quantity,
        string? reason, CancellationToken ct);
    Task<InventoryOverview> GetOverviewAsync(ClaimsPrincipal actor, int requestPage, int movementPage, int receiptPage,
        CancellationToken cancellationToken);
    Task<InventoryAlerts> GetAlertsAsync(ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<CatalogSettings?> GetCatalogAsync(ClaimsPrincipal actor, Guid productId, CancellationToken cancellationToken);
    Task<RequestLine?> GetRequestAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken);
    Task SetCatalogAsync(ClaimsPrincipal actor, CatalogSettings settings, CancellationToken cancellationToken);
    Task ReceiveAsync(ClaimsPrincipal actor, ReceivingCommand command, IReadOnlyList<EvidenceUpload> uploads,
        CancellationToken cancellationToken);
    Task ExecuteAsync(ClaimsPrincipal actor, InventoryCommand command, IReadOnlyList<EvidenceUpload> uploads,
        CancellationToken cancellationToken);
    Task<EvidenceDownload?> OpenEvidenceAsync(ClaimsPrincipal actor, string source, Guid id,
        CancellationToken cancellationToken);
}

public sealed class InventoryRuleException(string message) : Exception(message);
