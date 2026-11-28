using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed partial class InventoryService
{
    private async Task EstimateUsageAsync(Guid actorId, InventoryCommand command, CancellationToken ct)
    {
        var product = await ProductAsync(command.ProductId, ct);
        Require(product.IsConsumable, "Only grooming consumables can be estimated.");
        var quantity = Quantity(product.Unit, command.Quantity, command.InputUnit);
        Require(quantity > 0, "Enter a positive estimated quantity.");
        var jobReference = Required(command.Reason, 100, "A grooming job reference is required.");
        var existing = await db.Set<GroomingUsageEstimate>().FindAsync([command.OperationId], ct);
        if (existing is not null)
        {
            Require(existing.ActorId == actorId && existing.ProductId == product.Id && existing.Quantity == quantity &&
                existing.JobReference == jobReference, "This operation ID was already used for a different estimate.");
            return;
        }
        db.Add(new GroomingUsageEstimate
        {
            Id = command.OperationId, ProductId = product.Id, ActorId = actorId, JobReference = jobReference,
            Quantity = quantity, CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task SubmitAsync(Guid actorId, InventoryCommand command, IReadOnlyList<EvidenceUpload> uploads,
        List<string> files, CancellationToken ct)
    {
        var product = await ProductAsync(command.ProductId, ct, false);
        var quantity = Quantity(product.Unit, command.Quantity, command.InputUnit);
        var existing = await db.StockOutRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubmissionOperationId == command.OperationId, ct);
        if (existing is not null)
        {
            Require(existing.SubmittedByUserId == actorId && existing.ProductId == product.Id &&
                existing.BatchId == command.BatchId && existing.Quantity == quantity,
                "This operation ID was already used for a different request.");
            return;
        }

        var reason = Required(command.Reason, 500, "Explain why the stock needs to be disposed.");
        var category = DisposalCategory(command);
        var batch = await BatchAsync(command.BatchId, product.Id, ct);
        var pending = await PendingQuantityAsync(batch.Id, ct);
        Require(quantity > 0 && pending + quantity <= batch.OnHand, "There is not enough unreserved stock in this batch.");
        var request = new StockOutRequest
        {
            ProductId = product.Id, BatchId = batch.Id, SubmittedByUserId = actorId,
            Quantity = quantity, ReasonCategory = category, Reason = reason, SubmissionOperationId = command.OperationId
        };
        var photos = await SavePhotosAsync(uploads, files, true, ct);
        foreach (var photo in photos)
            request.Evidence.Add(new StockOutRequestEvidence
            {
                OriginalFileName = photo.Name, StorageKey = photo.File.Key, ContentType = photo.File.ContentType,
                Sha256Hash = photo.File.Sha256, FileSizeBytes = photo.File.Length, UploadedByUserId = actorId
            });
        var additionalQuarantine = Math.Max(0, pending + quantity - batch.Quarantined);
        batch.Quarantined += additionalQuarantine;
        if (additionalQuarantine > 0)
            RecordQuarantine(command.OperationId, batch.Id, actorId, additionalQuarantine, reason);
        db.StockOutRequests.Add(request);
    }

    private async Task DecideAsync(Guid actorId, InventoryCommand command, CancellationToken ct)
    {
        var request = await db.StockOutRequests.Include(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.Id == command.RequestId, ct)
            ?? throw new InventoryRuleException("The disposal request was not found.");
        var status = command.Action switch
        {
            InventoryAction.Approve => StockOutRequestStatus.Approved,
            InventoryAction.Reject => StockOutRequestStatus.Rejected,
            _ => StockOutRequestStatus.Cancelled
        };
        if (status == StockOutRequestStatus.Cancelled ? actorId != request.SubmittedByUserId : actorId == request.SubmittedByUserId)
            throw new UnauthorizedAccessException();
        if (request.Status == status && request.DecidedByUserId == actorId)
            return;
        Require(request.Status == StockOutRequestStatus.Pending, "The request has already been decided.");
        var comment = status == StockOutRequestStatus.Rejected
            ? Required(command.Reason, 500, "A rejection reason is required.") : Optional(command.Reason, 500);
        if (status == StockOutRequestStatus.Approved)
        {
            Require(request.Evidence.Count > 0, "The request has no evidence.");
            foreach (var photo in request.Evidence)
            {
                using var file = evidence.Open(photo.StorageKey);
                Require(file is not null, "The request evidence is unavailable.");
            }
            var batch = await BatchAsync(request.BatchId, request.ProductId, ct);
            Require(batch.Quarantined >= request.Quantity && batch.OnHand >= request.Quantity,
                "The reserved quantity is no longer available.");
            batch.Quarantined -= request.Quantity;
            batch.OnHand -= request.Quantity;
            var movement = await MoveAsync(request.ProductId, actorId, command.OperationId,
                InventoryMovementType.StockOut, -request.Quantity, request.Reason, ct);
            movement.StockOutRequestId = request.Id;
            movement.StockOutReason = request.ReasonCategory;
            Allocate(movement, batch.Id, -request.Quantity);
        }

        request.Status = status;
        request.DecidedByUserId = actorId;
        request.DecidedAtUtc = DateTime.UtcNow;
        request.DecisionComment = comment;
    }

    private async Task QuarantineAsync(Guid actorId, InventoryCommand command, CancellationToken ct)
    {
        var product = await ProductAsync(command.ProductId, ct, false);
        var quantity = Quantity(product.Unit, command.Quantity, command.InputUnit);
        var change = command.Action == InventoryAction.Release ? -quantity : quantity;
        var existing = await db.Set<InventoryQuarantineEvent>().FindAsync([command.OperationId], ct);
        if (existing is not null)
        {
            Require(existing.ActorId == actorId && existing.BatchId == command.BatchId && existing.QuantityChange == change,
                "This operation ID was already used for a different availability change.");
            return;
        }

        var batch = await BatchAsync(command.BatchId, product.Id, ct);
        var reason = Required(command.Reason, 500, "A reason is required.");
        var pending = await PendingQuantityAsync(batch.Id, ct);
        Require(quantity > 0 && batch.Quarantined + change >= pending && batch.Quarantined + change <= batch.OnHand,
            "The change exceeds available stock or would release stock reserved for review.");
        batch.Quarantined += change;
        RecordQuarantine(command.OperationId, batch.Id, actorId, change, reason);
    }

    private async Task PostMovementAsync(Guid actorId, bool management, InventoryCommand command, InventoryMovementType type,
        IReadOnlyList<EvidenceUpload> uploads, List<string> files, CancellationToken ct)
    {
        var product = await ProductAsync(command.ProductId, ct,
            command.Action is InventoryAction.Sale or InventoryAction.Use);
        if (command.Action is InventoryAction.ReverseSale or InventoryAction.ReverseUsage)
        {
            await ReverseAsync(actorId, command, type, ct);
            return;
        }

        var quantity = Quantity(product.Unit, command.Quantity, command.InputUnit, command.Action == InventoryAction.Adjust);
        Require(quantity != 0, "Enter a nonzero stock quantity.");
        var reason = command.Action == InventoryAction.Sale
            ? Optional(command.Reason, 500) ?? "Retail sale"
            : Required(command.Reason, 500, "A reason or job reference is required.");
        StockOutReason? category = command.Action == InventoryAction.Dispose ? DisposalCategory(command) : null;
        var change = command.Action == InventoryAction.Adjust ? quantity : -quantity;
        var movement = await MoveAsync(product.Id, actorId, command.OperationId, type, change, reason, ct);
        movement.StockOutReason = category;
        if (command.Action is InventoryAction.Sale or InventoryAction.Use)
        {
            Require(command.Action == InventoryAction.Sale ? product.IsSellable : product.IsConsumable,
                "This product is not configured for the requested use.");
            if (command.Action == InventoryAction.Use && command.MovementId.HasValue)
            {
                var estimate = await db.Set<GroomingUsageEstimate>().SingleOrDefaultAsync(x =>
                    x.Id == command.MovementId && x.ProductId == product.Id, ct)
                    ?? throw new InventoryRuleException("The usage estimate was not found for this product.");
                if (!management && estimate.ActorId != actorId)
                    throw new UnauthorizedAccessException();
                Require(!await db.InventoryMovements.AnyAsync(x => x.UsageEstimateId == estimate.Id, ct),
                    "Actual usage was already posted for this estimate.");
                movement.UsageEstimateId = estimate.Id;
                movement.Reason = estimate.JobReference + " · " + Required(command.Reason, 380, "A usage note is required.");
            }
            await TakeUsableStockAsync(movement, quantity, ct);
            return;
        }

        var batch = await BatchAsync(command.BatchId, product.Id, ct);
        var pending = await PendingQuantityAsync(batch.Id, ct);
        if (command.Action == InventoryAction.Dispose)
        {
            Require(batch.OnHand - pending >= quantity, "The batch does not have enough unreserved stock.");
            batch.Quarantined -= Math.Min(Math.Max(0, batch.Quarantined - pending), quantity);
        }
        else if (change < 0)
        {
            Require(batch.OnHand - batch.Quarantined >= -change, "Adjustments cannot remove quarantined stock.");
        }
        batch.OnHand += change;
        Allocate(movement, batch.Id, change);
        var photos = await SavePhotosAsync(uploads, files, change < 0, ct);
        foreach (var photo in photos)
            movement.Evidence.Add(new InventoryMovementEvidence
            {
                OriginalFileName = photo.Name, StorageKey = photo.File.Key, ContentType = photo.File.ContentType,
                Sha256Hash = photo.File.Sha256, FileSizeBytes = photo.File.Length, UploadedByUserId = actorId
            });
    }

    private async Task TakeUsableStockAsync(InventoryMovement movement, decimal quantity, CancellationToken ct, DateOnly? usageDate = null)
    {
        var today = usageDate ?? DateOnly.FromDateTime(DateTime.Today);
        var batches = await db.Set<InventoryBatch>()
            .Where(x => x.ProductId == movement.ProductId && x.OnHand > x.Quarantined &&
                (x.ExpiryDate == null || x.ExpiryDate >= today))
            .OrderBy(x => x.ExpiryDate == null).ThenBy(x => x.ExpiryDate)
            .ThenBy(x => x.ReceivedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        Require(batches.Sum(batch => batch.OnHand - batch.Quarantined) >= quantity, "Insufficient usable stock.");
        foreach (var batch in batches)
        {
            var taken = Math.Min(quantity, batch.OnHand - batch.Quarantined);
            batch.OnHand -= taken;
            Allocate(movement, batch.Id, -taken);
            quantity -= taken;
            if (quantity == 0)
                break;
        }
    }

    private async Task ReverseAsync(Guid actorId, InventoryCommand command, InventoryMovementType type, CancellationToken ct)
    {
        var originalType = type == InventoryMovementType.SaleReversal
            ? InventoryMovementType.Sale : InventoryMovementType.GroomingUsage;
        var original = await db.InventoryMovements.Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == command.MovementId && x.ProductId == command.ProductId &&
                x.MovementType == originalType, ct)
            ?? throw new InventoryRuleException("The original movement was not found.");
        Require(!await db.Set<RetailSaleLine>().AnyAsync(x => x.InventoryMovementId == original.Id, ct),
            "This stock movement belongs to a retail checkout and must be corrected with the sale.");
        await ReverseMovementAsync(original, actorId, command.OperationId, type,
            Required(command.Reason, 500, "A correction reason is required."), quarantine: false, ct);
    }

    private async Task ReverseMovementAsync(InventoryMovement original, Guid actorId, Guid operationId,
        InventoryMovementType type, string reason, bool quarantine, CancellationToken ct)
    {
        Require(original.Allocations.Count > 0 && original.Allocations.Sum(x => x.QuantityChange) == original.QuantityChange,
            "The original movement has no complete batch allocation.");
        Require(!await db.InventoryMovements.AnyAsync(x => x.ReversesMovementId == original.Id, ct),
            "The original movement was already reversed.");
        var reversal = await MoveAsync(original.ProductId, actorId, operationId, type,
            -original.QuantityChange, reason, ct);
        reversal.ReversesMovementId = original.Id;
        foreach (var allocation in original.Allocations)
        {
            var batch = await BatchAsync(allocation.BatchId, original.ProductId, ct);
            var restored = -allocation.QuantityChange;
            batch.OnHand += restored;
            if (quarantine)
            {
                batch.Quarantined += restored;
                RecordQuarantine(Guid.NewGuid(), batch.Id, actorId, restored, reason);
            }
            Allocate(reversal, batch.Id, restored);
        }
    }

    private Task<decimal> PendingQuantityAsync(Guid batchId, CancellationToken ct) =>
        db.StockOutRequests.Where(x => x.BatchId == batchId && x.Status == StockOutRequestStatus.Pending)
            .SumAsync(x => x.Quantity, ct);

    private async Task<InventoryBatch> BatchAsync(Guid? batchId, Guid productId, CancellationToken ct) =>
        await db.Set<InventoryBatch>().SingleOrDefaultAsync(x => x.Id == batchId && x.ProductId == productId, ct)
        ?? throw new InventoryRuleException("Select a batch belonging to the product.");

    private void RecordQuarantine(Guid operationId, Guid batchId, Guid actorId, decimal change, string reason) =>
        db.Add(new InventoryQuarantineEvent
        {
            OperationId = operationId, BatchId = batchId, ActorId = actorId,
            QuantityChange = change, Reason = reason, CreatedAtUtc = DateTime.UtcNow
        });

    private static StockOutReason DisposalCategory(InventoryCommand command) =>
        command.Category is { } category && Enum.IsDefined(category) ? category :
            throw new InventoryRuleException("Select a disposal reason category.");
}
