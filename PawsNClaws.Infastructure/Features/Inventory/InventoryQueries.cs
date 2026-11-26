using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed partial class InventoryService
{
    public async Task<InventoryOverview> GetOverviewAsync(ClaimsPrincipal actor, int requestPage, int movementPage,
        int receiptPage, CancellationToken ct)
    {
        var actorId = InventoryAccess.Demand(actor, Permissions.InventoryView);
        var management = InventoryAccess.IsManagement(actor);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var inventoryBatches = db.Set<InventoryBatch>().AsNoTracking();
        var stock = await (from product in db.Products.AsNoTracking()
            join balance in db.InventoryStocks on product.Id equals balance.ProductId into balances
            from balance in balances.DefaultIfEmpty()
            orderby product.Name, product.Id
            select new StockLine(product.Id, product.SKU, product.Name, product.Unit,
                balance == null ? 0 : balance.QuantityOnHand,
                inventoryBatches.Where(x => x.ProductId == product.Id && (x.ExpiryDate == null || x.ExpiryDate >= today))
                    .Sum(x => x.OnHand - x.Quarantined),
                inventoryBatches.Where(x => x.ProductId == product.Id).Sum(x => x.Quarantined),
                balance == null ? 0 : balance.ReorderLevel)).ToListAsync(ct);
        var batches = await inventoryBatches.Where(x => x.OnHand > 0)
            .OrderBy(x => x.ExpiryDate).ThenBy(x => x.ReceivedAtUtc).ThenBy(x => x.Id)
            .Select(x => new BatchLine(x.Id, x.ProductId, x.Lot, x.ExpiryDate, x.OnHand, x.Quarantined)).ToListAsync(ct);
        var requests = await db.StockOutRequests.AsNoTracking().Where(x => management || x.SubmittedByUserId == actorId)
            .OrderByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Id).Skip(PageOffset(requestPage)).Take(25)
            .Select(x => new RequestLine(x.Id, x.ProductId, x.Product.Name, x.Quantity, x.ReasonCategory, x.Reason,
                x.Status, x.SubmittedByUserId, x.SubmittedAtUtc, x.DecidedByUserId, x.DecidedAtUtc, x.DecisionComment,
                x.Evidence.Select(photo => new EvidenceLink(photo.Id, "request")).ToList())).ToListAsync(ct);
        var movements = await db.InventoryMovements.AsNoTracking().Where(x => management || x.PerformedByUserId == actorId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(PageOffset(movementPage)).Take(25)
            .Select(x => new MovementLine(x.Id, x.ProductId, x.Product.Name, x.MovementType, x.QuantityChange,
                x.QuantityAfter, x.CreatedAtUtc, x.Reason, x.StockOutRequestId,
                x.Evidence.Select(photo => new EvidenceLink(photo.Id, "movement")).ToList())).ToListAsync(ct);
        var receipts = await (from receipt in db.Set<StockReceipt>().AsNoTracking()
            join product in db.Products on receipt.ProductId equals product.Id
            where management || receipt.ActorId == actorId
            orderby receipt.ReceivedAtUtc descending, receipt.Id
            select new ReceiptLine(receipt.Id, product.Name, receipt.Supplier, receipt.Reference,
                receipt.Delivered, receipt.Accepted, receipt.Rejected, receipt.ReceivedAtUtc,
                receipt.Evidence.Select(photo => new EvidenceLink(photo.Id, "receipt")).ToList()))
            .Skip(PageOffset(receiptPage)).Take(25).ToListAsync(ct);
        var estimates = await (from estimate in db.Set<GroomingUsageEstimate>().AsNoTracking()
            join product in db.Products on estimate.ProductId equals product.Id
            where management || estimate.ActorId == actorId
            orderby estimate.CreatedAtUtc descending, estimate.Id
            select new UsageEstimateLine(estimate.Id, product.Id, product.Name, estimate.JobReference,
                estimate.Quantity, product.Unit, db.InventoryMovements.Any(x => x.UsageEstimateId == estimate.Id)))
            .Take(25).ToListAsync(ct);
        return new(stock, batches, requests, movements, receipts, estimates);
    }

    public async Task<RequestLine?> GetRequestAsync(ClaimsPrincipal actor, Guid id, CancellationToken ct)
    {
        var actorId = InventoryAccess.Demand(actor, Permissions.InventoryView);
        var management = InventoryAccess.IsManagement(actor);
        return await db.StockOutRequests.AsNoTracking()
            .Where(x => x.Id == id && (management || x.SubmittedByUserId == actorId))
            .Select(x => new RequestLine(x.Id, x.ProductId, x.Product.Name, x.Quantity, x.ReasonCategory, x.Reason,
                x.Status, x.SubmittedByUserId, x.SubmittedAtUtc, x.DecidedByUserId, x.DecidedAtUtc, x.DecisionComment,
                x.Evidence.Select(photo => new EvidenceLink(photo.Id, "request")).ToList())).SingleOrDefaultAsync(ct);
    }

    public async Task<CatalogSettings?> GetCatalogAsync(ClaimsPrincipal actor, Guid productId, CancellationToken ct)
    {
        InventoryAccess.Demand(actor, Permissions.InventoryAdjust, management: true);
        return await (from product in db.Products.AsNoTracking()
            join stock in db.InventoryStocks on product.Id equals stock.ProductId into balances
            from stock in balances.DefaultIfEmpty()
            where product.Id == productId
            select new CatalogSettings(product.Id, product.Unit, product.IsSellable, product.IsConsumable,
                product.TracksExpiry, product.IsVisibleOnline, product.Category, product.Barcode,
                product.Description, product.UnitCost, stock == null ? 0 : stock.ReorderLevel))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<EvidenceDownload?> OpenEvidenceAsync(ClaimsPrincipal actor, string source, Guid id, CancellationToken ct)
    {
        var actorId = InventoryAccess.Demand(actor, Permissions.InventoryView);
        var management = InventoryAccess.IsManagement(actor);
        var details = source switch
        {
            "request" => await db.StockOutRequestEvidence.AsNoTracking()
                .Where(x => x.Id == id && (management || x.StockOutRequest.SubmittedByUserId == actorId))
                .Select(x => new { x.StorageKey, x.ContentType }).SingleOrDefaultAsync(ct),
            "movement" => await db.InventoryMovementEvidence.AsNoTracking()
                .Where(x => x.Id == id && (management || x.InventoryMovement.PerformedByUserId == actorId))
                .Select(x => new { x.StorageKey, x.ContentType }).SingleOrDefaultAsync(ct),
            "receipt" => await (from photo in db.Set<StockReceiptEvidence>().AsNoTracking()
                join receipt in db.Set<StockReceipt>() on photo.StockReceiptId equals receipt.Id
                where photo.Id == id && (management || receipt.ActorId == actorId)
                select new { photo.StorageKey, photo.ContentType }).SingleOrDefaultAsync(ct),
            _ => null
        };
        if (details is null || details.ContentType is not ("image/jpeg" or "image/png"))
            return null;
        var stream = evidence.Open(details.StorageKey);
        return stream is null ? null : new(stream, details.ContentType,
            details.ContentType == "image/png" ? "evidence.png" : "evidence.jpg");
    }

    private static int PageOffset(int page) => (Math.Clamp(page, 1, 10_000) - 1) * 25;
}
