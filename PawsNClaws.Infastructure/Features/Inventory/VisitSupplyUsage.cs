using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed partial class InventoryService
{
    private async Task<GroomingVisit?> CheckedInVisitAsync(Guid id, CancellationToken ct) =>
        await db.Set<GroomingVisit>().SingleOrDefaultAsync(visit => visit.Id == id &&
            db.Set<GroomingCheckIn>().Any(checkIn => checkIn.VisitId == visit.Id && checkIn.CancelledAtUtc == null), ct);

    public async Task<VisitSupplies?> GetVisitSuppliesAsync(ClaimsPrincipal actor, Guid visitId, CancellationToken ct)
    {
        InventoryAccess.Demand(actor, Permissions.InventoryUse, management: true);
        var visit = await CheckedInVisitAsync(visitId, ct);
        if (visit is null) return null;

        var snapshot = JsonSerializer.Deserialize<QuoteSnapshot>(visit.EstimateSnapshot)!;
        var estimated = snapshot.Supplies.ToDictionary(x => x.ProductId);
        var posted = await db.InventoryMovements.Where(x => x.GroomingVisitId == visitId)
            .Select(x => new { x.ProductId, x.QuantityChange, x.Reason,
                Reversed = db.InventoryMovements.Any(reversal => reversal.ReversesMovementId == x.Id) })
            .ToDictionaryAsync(x => x.ProductId, ct);
        var relevantIds = estimated.Keys.Concat(posted.Keys).Distinct().ToArray();
        var products = await db.Products.Where(x => (x.IsActive && x.IsConsumable) || relevantIds.Contains(x.Id))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        var notePrefix = $"Grooming visit {visitId} - ";
        var items = products.Select(product =>
        {
            var planned = estimated.GetValueOrDefault(product.Id);
            var movement = posted.GetValueOrDefault(product.Id);
            var unit = movement is not null ? product.Unit : planned?.Unit ?? product.Unit;
            return new VisitSupply(product.Id, product.Name, unit, planned?.Quantity ?? 0,
                movement is null ? null : -movement.QuantityChange,
                product.IsConsumable && product.IsActive && unit == product.Unit && (planned is null || planned.Unit == product.Unit),
                movement?.Reversed ?? false,
                movement?.Reason is { } note && note.StartsWith(notePrefix, StringComparison.Ordinal)
                    ? note[notePrefix.Length..] : null);
        }).ToArray();
        return new(visit.Id, visit.RevisionToken, snapshot.Pet, visit.PickedUpAtUtc.HasValue, items);
    }

    public Task PostVisitSupplyAsync(ClaimsPrincipal actor, Guid visitId, Guid revision, Guid productId,
        decimal quantity, string? reason, CancellationToken ct)
    {
        var employee = InventoryAccess.Demand(actor, Permissions.InventoryUse, management: true);
        return WriteAsync(async _ =>
        {
            var visit = await CheckedInVisitAsync(visitId, ct)
                ?? throw new InventoryRuleException("Check in the pet before recording supplies.");
            Require(!visit.PickedUpAtUtc.HasValue, "This pet has already been picked up.");
            Require(!await db.InventoryMovements.AnyAsync(x => x.GroomingVisitId == visitId && x.ProductId == productId, ct),
                "Usage for this product was already recorded. Review the stock movement before making a correction.");
            Require(visit.RevisionToken == revision, "The visit changed. Review the latest details and try again.");
            var product = await ProductAsync(productId, ct);
            Require(product.IsConsumable, "Choose an active grooming consumable.");
            var actual = Quantity(product.Unit, quantity);
            Require(actual > 0, "Enter a quantity greater than zero.");
            var snapshot = JsonSerializer.Deserialize<QuoteSnapshot>(visit.EstimateSnapshot)!;
            var planned = snapshot.Supplies.SingleOrDefault(x => x.ProductId == productId);
            Require(planned is null || planned.Unit == product.Unit, "The product unit changed since this estimate was created.");
            var note = actual == planned?.Quantity
                ? Optional(reason, 380)
                : Required(reason, 380, "Explain why usage differs from the estimate.");
            var reference = $"Grooming visit {visitId}";
            var movement = await MoveAsync(productId, employee, Guid.NewGuid(), InventoryMovementType.GroomingUsage,
                -actual, note is null ? reference : $"{reference} - {note}", ct);
            movement.GroomingVisitId = visitId;
            var shopDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
            await TakeUsableStockAsync(movement, actual, ct, shopDate);
            visit.RevisionToken = Guid.NewGuid();
        }, ct);
    }
}
