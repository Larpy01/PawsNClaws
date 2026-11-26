using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed partial class InventoryService(
    LocalDbContext db, InventoryEvidenceStore evidence, ILogger<InventoryService> logger) : IInventoryService
{
    public async Task SetCatalogAsync(ClaimsPrincipal actor, CatalogSettings settings, CancellationToken ct)
    {
        InventoryAccess.Demand(actor, Permissions.InventoryAdjust, management: true);
        await WriteAsync(async files =>
        {
            var product = await ProductAsync(settings.ProductId, ct, requireActive: false);
            Require(settings.Unit is "Each" or "mL" or "g", "Select a supported base unit.");
            Require(settings.Sellable || settings.Consumable, "Select at least one product purpose.");
            Require(!settings.Online || settings.Sellable, "Online products must be sellable.");
            Require(!settings.Sellable || settings.Unit == "Each", "Retail products are sold by whole item.");
            var batches = db.Set<InventoryBatch>().Where(batch => batch.ProductId == product.Id);
            if (product.Unit != settings.Unit &&
                (await batches.AnyAsync(ct) || await db.InventoryMovements.AnyAsync(x => x.ProductId == product.Id, ct) ||
                 await db.Set<StockReceipt>().AnyAsync(x => x.ProductId == product.Id, ct)))
                throw new InventoryRuleException("A product with inventory history must retain its original base unit.");
            if (settings.TracksExpiry && await batches.AnyAsync(x => x.OnHand > 0 && x.ExpiryDate == null, ct))
                throw new InventoryRuleException("Resolve existing stock without an expiry date before enabling expiry tracking.");
            product.Unit = settings.Unit;
            product.IsSellable = settings.Sellable;
            product.IsConsumable = settings.Consumable;
            product.TracksExpiry = settings.TracksExpiry;
            product.IsVisibleOnline = settings.Online;
            product.Category = Optional(settings.Category, 80);
            product.Barcode = Optional(settings.Barcode, 64);
            product.Description = Optional(settings.Description, 500);
            product.UnitCost = Money(settings.UnitCost);
            var stock = await StockAsync(product.Id, ct);
            stock.ReorderLevel = Quantity(product.Unit, settings.ReorderLevel);
        }, ct);
    }

    public async Task ReceiveAsync(ClaimsPrincipal actor, ReceivingCommand command,
        IReadOnlyList<EvidenceUpload> uploads, CancellationToken ct)
    {
        var actorId = InventoryAccess.Demand(actor, Permissions.InventoryReceive);
        Require(command.OperationId != Guid.Empty, "Missing operation ID. Reload the form.");
        await WriteAsync(async files =>
        {
            var product = await ProductAsync(command.ProductId, ct, false);
            var delivered = Quantity(product.Unit, command.Delivered, command.InputUnit);
            var accepted = Quantity(product.Unit, command.Accepted, command.InputUnit);
            var rejected = Quantity(product.Unit, command.Rejected, command.InputUnit);
            var existing = await db.Set<StockReceipt>().FindAsync([command.OperationId], ct);
            if (existing is not null)
            {
                Require(existing.ActorId == actorId && existing.ProductId == product.Id &&
                    existing.Delivered == delivered && existing.Accepted == accepted && existing.Rejected == rejected,
                    "This operation ID was already used for a different receipt.");
                return;
            }

            Require(product.IsActive, "The product is missing or inactive.");
            Require(delivered > 0 && delivered == accepted + rejected,
                "Delivered quantity must equal accepted plus rejected quantity.");
            var supplier = Required(command.Supplier, 150, "Supplier is required.");
            var normalizedName = supplier.ToUpperInvariant();
            var supplierEntry = await db.Set<InventorySupplier>()
                .SingleOrDefaultAsync(x => x.NormalizedName == normalizedName, ct);
            if (supplierEntry is null)
            {
                supplierEntry = new InventorySupplier { Name = supplier, NormalizedName = normalizedName };
                db.Add(supplierEntry);
            }
            var lot = Optional(command.Lot, 100);
            if (accepted > 0 && product.TracksExpiry)
                Require(lot is not null && command.ExpiryDate.HasValue, "This product requires a lot and expiry date.");
            if (accepted > 0 && command.ExpiryDate.HasValue)
                Require(command.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today), "Expired stock cannot be accepted.");
            var receipt = new StockReceipt
            {
                Id = command.OperationId, ProductId = product.Id, ActorId = actorId,
                SupplierId = supplierEntry.Id, Supplier = supplierEntry.Name,
                Reference = Optional(command.Reference, 100), Lot = lot, ExpiryDate = command.ExpiryDate,
                Delivered = delivered, Accepted = accepted, Rejected = rejected, UnitCost = Money(command.UnitCost),
                RejectionReason = rejected > 0 ? Required(command.RejectionReason, 500, "A rejection reason is required.") : null,
                ReceivedAtUtc = DateTime.UtcNow
            };
            var photos = await SavePhotosAsync(uploads, files, rejected > 0, ct);
            foreach (var photo in photos)
                receipt.Evidence.Add(new StockReceiptEvidence
                {
                    StorageKey = photo.File.Key, ContentType = photo.File.ContentType,
                    Sha256Hash = photo.File.Sha256, FileSizeBytes = photo.File.Length
                });
            if (accepted > 0)
            {
                var batch = new InventoryBatch
                {
                    ProductId = product.Id, Lot = lot, ExpiryDate = command.ExpiryDate,
                    ReceivedAtUtc = receipt.ReceivedAtUtc, OnHand = accepted
                };
                db.Add(batch);
                receipt.BatchId = batch.Id;
                var movement = await MoveAsync(product.Id, actorId, command.OperationId, InventoryMovementType.StockIn,
                    accepted, "Receiving", ct);
                Allocate(movement, batch.Id, accepted);
            }
            db.Add(receipt);
        }, ct);
    }

    public async Task ExecuteAsync(ClaimsPrincipal actor, InventoryCommand command,
        IReadOnlyList<EvidenceUpload> uploads, CancellationToken ct)
    {
        var action = command.Action;
        var permission = action switch
        {
            InventoryAction.Submit or InventoryAction.Cancel => Permissions.InventorySubmit,
            InventoryAction.Dispose or InventoryAction.Approve or InventoryAction.Reject => Permissions.InventoryReview,
            InventoryAction.Use or InventoryAction.ReverseUsage or InventoryAction.EstimateUsage => Permissions.InventoryUse,
            InventoryAction.Sale => Permissions.PosCreateSale,
            InventoryAction.ReverseSale => Permissions.PosVoidSale,
            InventoryAction.Adjust or InventoryAction.Quarantine or InventoryAction.Release => Permissions.InventoryAdjust,
            _ => throw new InventoryRuleException("Unknown inventory action.")
        };
        var actorId = InventoryAccess.Demand(actor, permission, action is InventoryAction.Dispose or InventoryAction.Approve
            or InventoryAction.Reject or InventoryAction.Adjust or InventoryAction.Quarantine or InventoryAction.Release
            or InventoryAction.ReverseUsage);
        Require(command.OperationId != Guid.Empty, "Missing operation ID. Reload the form.");

        await WriteAsync(async files =>
        {
            if (action is InventoryAction.Approve or InventoryAction.Reject or InventoryAction.Cancel)
            {
                await DecideAsync(actorId, command, ct);
                return;
            }
            if (action == InventoryAction.Submit)
            {
                await SubmitAsync(actorId, command, uploads, files, ct);
                return;
            }
            if (action is InventoryAction.Quarantine or InventoryAction.Release)
            {
                await QuarantineAsync(actorId, command, ct);
                return;
            }
            if (action == InventoryAction.EstimateUsage)
            {
                await EstimateUsageAsync(actorId, command, ct);
                return;
            }

            var type = action switch
            {
                InventoryAction.Dispose => InventoryMovementType.StockOut,
                InventoryAction.Adjust => InventoryMovementType.Adjustment,
                InventoryAction.Sale => InventoryMovementType.Sale,
                InventoryAction.Use => InventoryMovementType.GroomingUsage,
                InventoryAction.ReverseSale => InventoryMovementType.SaleReversal,
                InventoryAction.ReverseUsage => InventoryMovementType.GroomingUsageCorrection,
                _ => throw new InventoryRuleException("Unknown movement type.")
            };
            var existing = await db.InventoryMovements.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
            if (existing is not null)
            {
                Require(existing.PerformedByUserId == actorId && existing.ProductId == command.ProductId &&
                    existing.MovementType == type &&
                    (action == InventoryAction.Use ? existing.UsageEstimateId : existing.ReversesMovementId) == command.MovementId,
                    "This operation ID was already used for a different movement.");
                return;
            }
            await PostMovementAsync(actorId, InventoryAccess.IsManagement(actor), command, type, uploads, files, ct);
        }, ct);
    }

    private async Task WriteAsync(Func<List<string>, Task> action, CancellationToken ct)
    {
        var files = new List<string>();
        var committing = false;
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await action(files);
            await db.SaveChangesAsync(ct);
            committing = true;
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException { Number: 1205 or 1222 })
        {
            logger.LogWarning(exception, "Inventory write could not be committed.");
            throw new InventoryRuleException("Inventory changed or a value is already in use. Reload and try again.");
        }
        finally
        {
            if (!committing)
            {
                foreach (var key in files)
                {
                    try { evidence.DeleteUncommitted(key); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(exception, "Could not remove uncommitted inventory evidence {Key}.", key);
                    }
                }
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<Product> ProductAsync(Guid productId, CancellationToken ct, bool requireActive = true) =>
        await db.Products.SingleOrDefaultAsync(x => x.Id == productId && (!requireActive || x.IsActive), ct)
        ?? throw new InventoryRuleException("The product is missing or inactive.");

    private async Task<InventoryStock> StockAsync(Guid productId, CancellationToken ct)
    {
        var stock = await db.InventoryStocks.SingleOrDefaultAsync(x => x.ProductId == productId, ct);
        if (stock is not null)
            return stock;
        stock = new InventoryStock { ProductId = productId };
        db.InventoryStocks.Add(stock);
        return stock;
    }

    private async Task<InventoryMovement> MoveAsync(Guid productId, Guid actorId, Guid operationId,
        InventoryMovementType type, decimal change, string reason, CancellationToken ct)
    {
        var stock = await StockAsync(productId, ct);
        var after = stock.QuantityOnHand + change;
        Require(after >= 0 && after < 1_000_000_000_000_000m, "The resulting stock balance is outside the allowed range.");
        var movement = new InventoryMovement
        {
            ProductId = productId, PerformedByUserId = actorId, OperationId = operationId, MovementType = type,
            QuantityChange = change, QuantityBefore = stock.QuantityOnHand, QuantityAfter = after, Reason = reason
        };
        stock.QuantityOnHand = after;
        stock.UpdatedAtUtc = DateTime.UtcNow;
        db.InventoryMovements.Add(movement);
        return movement;
    }

    private static void Allocate(InventoryMovement movement, Guid batchId, decimal change) =>
        movement.Allocations.Add(new InventoryMovementAllocation { BatchId = batchId, QuantityChange = change });

    private async Task<List<(string Name, EvidenceFile File)>> SavePhotosAsync(IReadOnlyList<EvidenceUpload> uploads,
        List<string> files, bool required, CancellationToken ct)
    {
        Require(uploads.Count <= 5 && (!required || uploads.Count > 0), "Supply one to five photos where evidence is required.");
        var photos = new List<(string, EvidenceFile)>();
        foreach (var upload in uploads)
        {
            var name = Optional(Path.GetFileName(upload.Name.Replace('\\', '/')), 255) ?? "Photo";
            var file = await evidence.SaveAsync(upload.Content, ct);
            files.Add(file.Key);
            photos.Add((name, file));
        }
        return photos;
    }

    private static decimal Quantity(string unit, decimal value, string? input = null, bool signed = false)
    {
        var source = string.IsNullOrWhiteSpace(input) ? unit : input;
        Require(value > -1_000_000_000_000_000m && value < 1_000_000_000_000_000m, "Quantity is outside the allowed range.");
        if (source != unit)
        {
            Require((source, unit) is ("L", "mL") or ("kg", "g"), "The selected unit cannot be converted to this product's base unit.");
            value *= 1000;
        }
        Require((signed || value >= 0) && Math.Abs(value) < 1_000_000_000_000_000m &&
            decimal.Round(value, unit == "Each" ? 0 : 3) == value,
            "Enter a valid quantity in the product's base unit. Each requires whole numbers.");
        return value;
    }

    private static decimal? Money(decimal? value)
    {
        Require(!value.HasValue || (value >= 0 && value < 10_000_000_000_000_000m &&
            decimal.Round(value.Value, 2) == value), "Enter a valid cost with no more than two decimal places.");
        return value;
    }

    private static string? Optional(string? value, int max)
    {
        value = value?.Trim();
        Require(value is null || value.Length <= max, $"Text must be {max} characters or fewer.");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string Required(string? value, int max, string message) =>
        Optional(value, max) ?? throw new InventoryRuleException(message);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InventoryRuleException(message);
    }
}
