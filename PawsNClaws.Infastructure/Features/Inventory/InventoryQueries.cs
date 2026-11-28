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
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var inventoryBatches = db.Set<InventoryBatch>().AsNoTracking();
        var usableBatches = UsableBatches(today);
        var stock = await (from product in db.Products.AsNoTracking()
            join balance in db.InventoryStocks on product.Id equals balance.ProductId into balances
            from balance in balances.DefaultIfEmpty()
            orderby product.Name, product.Id
            select new StockLine(product.Id, product.SKU, product.Name, product.Unit,
                balance == null ? 0 : balance.QuantityOnHand,
                usableBatches.Where(x => x.ProductId == product.Id)
                    .Sum(x => x.OnHand - x.Quarantined),
                inventoryBatches.Where(x => x.ProductId == product.Id).Sum(x => x.Quarantined),
                balance == null ? 0 : balance.ReorderLevel,
                product.IsActive && product.IsSellable && product.Unit == "Each" ? product.Price : (decimal?)null)).ToListAsync(ct);
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
                receipt.Evidence.Select(photo => new EvidenceLink(photo.Id, "receipt")).ToList(), receipt.SupplierInvoiceVat))
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

    public async Task<InventoryAlerts> GetAlertsAsync(ClaimsPrincipal actor, CancellationToken ct)
    {
        InventoryAccess.Demand(actor, Permissions.InventoryView, management: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var through = today.AddDays(30);
        var products = db.Products.AsNoTracking();
        var usableBatches = UsableBatches(today);
        var low = await (from balance in db.InventoryStocks.AsNoTracking()
                         join product in products on balance.ProductId equals product.Id
                         where product.IsActive && balance.ReorderLevel > 0
                         let usable = usableBatches.Where(x => x.ProductId == product.Id)
                             .Sum(x => x.OnHand - x.Quarantined)
                         where usable <= balance.ReorderLevel
                         orderby usable, product.Name, product.Id
                         select new LowStockAlert(product.Id, product.SKU, product.Name, product.Unit,
                             balance.QuantityOnHand, usable, balance.ReorderLevel)).ToListAsync(ct);
        var expiring = await (from batch in db.Set<InventoryBatch>().AsNoTracking()
                              join product in products on batch.ProductId equals product.Id
                              where batch.OnHand > 0 && batch.ExpiryDate.HasValue && batch.ExpiryDate <= through
                              orderby batch.ExpiryDate, product.Name, product.Id, batch.Id
                              select new BatchExpiryAlert(product.SKU, product.Name, product.Unit, batch.Lot,
                                  batch.ExpiryDate!.Value, batch.OnHand, batch.Quarantined)).ToListAsync(ct);

        return new(today, through, low, expiring.Where(x => x.Expiry >= today).ToArray(),
            expiring.Where(x => x.Expiry < today).ToArray());
    }

    private IQueryable<InventoryBatch> UsableBatches(DateOnly today) =>
        db.Set<InventoryBatch>().AsNoTracking().Where(x => x.ExpiryDate == null || x.ExpiryDate >= today);

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
