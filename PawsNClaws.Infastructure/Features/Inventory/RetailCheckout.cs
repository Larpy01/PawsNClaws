using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed partial class InventoryService : IRetailCheckout
{
    public async Task<IReadOnlyList<RetailProduct>> ProductsAsync(ClaimsPrincipal actor, CancellationToken token)
    {
        InventoryAccess.Demand(actor, Permissions.PosAccess);
        var shopDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        return await db.Products.Where(x => x.IsActive && x.IsSellable && x.Unit == "Each")
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new RetailProduct(x.Id, x.Name, x.SKU, x.Price,
                db.Set<InventoryBatch>().Where(batch => batch.ProductId == x.Id && batch.OnHand > batch.Quarantined &&
                    (batch.ExpiryDate == null || batch.ExpiryDate >= shopDate))
                    .Sum(batch => batch.OnHand - batch.Quarantined)))
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<RetailSaleSummary>> ListSalesAsync(ClaimsPrincipal actor, int page, CancellationToken token)
    {
        InventoryAccess.Demand(actor, Permissions.PosAccess);
        return await db.Set<RetailSale>().AsNoTracking()
            .OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * 25).Take(26)
            .Select(x => new RetailSaleSummary(x.Id, x.RecordedAtUtc, x.Total, x.VoidedAtUtc, x.PaymentMethod))
            .ToListAsync(token);
    }

    public Task VoidSaleAsync(ClaimsPrincipal actor, Guid id, string? reason, bool stockVerified, bool cashReturned,
        bool onlineReturned, bool quarantineReturned, CancellationToken token)
    {
        var employee = InventoryAccess.Demand(actor, Permissions.PosVoidSale, management: true);
        var note = Required(reason, 500, "Enter a reason for the refund.");
        Require(stockVerified, "Confirm the returned items match the original sale.");
        return WriteAsync(async _ =>
        {
            var sale = await db.Set<RetailSale>().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new InventoryRuleException("The retail sale was not found.");
            var isCash = sale.PaymentMethod == RetailPaymentMethod.Cash;
            Require(isCash ? cashReturned : onlineReturned, isCash
                ? "Confirm the full cash amount was returned."
                : "Confirm the full amount was refunded online.");
            if (sale.VoidedAtUtc.HasValue)
            {
                Require(sale.VoidReason == note && sale.ReturnsQuarantined == quarantineReturned,
                    "This retail sale was already refunded.");
                return;
            }
            if (isCash)
            {
                var cashError = await CashDrawer.RecordAsync(db, CashDrawerEntryKind.RetailRefund, id, employee, -sale.Total, token);
                if (cashError is not null) throw new InventoryRuleException(cashError);
            }
            Require(sale.Lines.Count > 0, "This sale has no stock lines to reverse.");
            foreach (var line in sale.Lines.OrderBy(x => x.ProductId))
            {
                var original = await db.InventoryMovements.Include(x => x.Allocations)
                    .SingleOrDefaultAsync(x => x.Id == line.InventoryMovementId && x.ProductId == line.ProductId &&
                        x.MovementType == InventoryMovementType.Sale && x.QuantityChange == -line.Quantity, token)
                    ?? throw new InventoryRuleException("The original stock movement could not be verified.");
                await ReverseMovementAsync(original, employee, Guid.NewGuid(), InventoryMovementType.SaleReversal,
                    note, quarantineReturned, token);
            }
            sale.VoidedAtUtc = DateTime.UtcNow;
            sale.VoidedByEmployeeId = employee;
            sale.VoidReason = note;
            sale.ReturnsQuarantined = quarantineReturned;
        }, token);
    }

    public async Task<RetailSaleView?> FindSaleAsync(ClaimsPrincipal actor, Guid id, CancellationToken token)
    {
        InventoryAccess.Demand(actor, Permissions.PosAccess);
        var sale = await db.Set<RetailSale>().AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, token);
        if (sale is null) return null;
        var lots = await (from line in db.Set<RetailSaleLine>()
                          join allocation in db.Set<InventoryMovementAllocation>() on line.InventoryMovementId equals allocation.MovementId
                          join batch in db.Set<InventoryBatch>() on allocation.BatchId equals batch.Id
                          where line.SaleId == id
                          orderby line.Name, batch.ExpiryDate, batch.Id
                          select new RetailSoldLot(line.Name, line.Sku, batch.Id, batch.Lot, batch.ExpiryDate,
                              -allocation.QuantityChange)).ToListAsync(token);
        return new(sale.Id, sale.RecordedAtUtc, sale.Total, sale.Tendered,
            sale.Lines.OrderBy(x => x.Name).ThenBy(x => x.ProductId)
                .Select(x => new RetailSoldLine(x.Name, x.Sku, x.Quantity, x.UnitPrice)).ToArray(),
            sale.VoidedAtUtc, sale.VoidReason, sale.ReturnsQuarantined, lots, sale.VatRate, sale.VatAmount,
            sale.PaymentMethod, sale.OnlinePaymentReference);
    }

    public Task CheckoutAsync(ClaimsPrincipal actor, Guid operationId, IReadOnlyList<RetailLineInput> lines,
        decimal tendered, decimal? expectedTotal, RetailPaymentMethod paymentMethod, string? onlinePaymentReference,
        CancellationToken token)
    {
        var employee = InventoryAccess.Demand(actor, Permissions.PosCreateSale);
        Require(operationId != Guid.Empty, "Reload checkout before recording a sale.");
        Require(lines.Count is >= 1 and <= 10, "Choose between 1 and 10 products.");
        Require(lines.All(x => x.ProductId.HasValue && x.ProductId != Guid.Empty && x.Quantity is > 0),
            "Choose a product and enter a positive whole quantity on each line.");
        var quantities = lines.GroupBy(x => x.ProductId!.Value).ToDictionary(x => x.Key, x => (long)x.Sum(line => (long)line.Quantity!.Value));
        Require(quantities.Values.All(x => x <= int.MaxValue), "A product quantity is too large.");
        Money(tendered);
        Require(paymentMethod is RetailPaymentMethod.Cash or RetailPaymentMethod.Online, "Choose a payment method.");
        var isCash = paymentMethod == RetailPaymentMethod.Cash;
        string? reference = null;
        if (!isCash)
        {
            reference = Required(onlinePaymentReference, 80, "Enter the online payment reference number.").ToUpperInvariant();
            Require(reference.Any(char.IsAsciiLetterOrDigit) &&
                reference.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/' or '.'),
                "Use letters, numbers, hyphens, underscores, slashes, or periods in the payment reference.");
        }

        return WriteAsync(async _ =>
        {
            var existing = await db.Set<RetailSale>().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == operationId, token);
            if (existing is not null)
            {
                Require(existing.Tendered == tendered && existing.RecordedByEmployeeId == employee &&
                    existing.PaymentMethod == paymentMethod && existing.OnlinePaymentReference == reference &&
                    existing.Lines.Count == quantities.Count &&
                    existing.Lines.All(line => quantities.TryGetValue(line.ProductId, out var quantity) && quantity == line.Quantity),
                    "This checkout reference already belongs to another sale.");
                return;
            }

            var ids = quantities.Keys.ToArray();
            var products = await db.Products.Where(x => ids.Contains(x.Id) && x.IsActive && x.IsSellable && x.Unit == "Each")
                .OrderBy(x => x.Id).ToListAsync(token);
            Require(products.Count == quantities.Count, "Choose active products that are available for retail sale.");
            var total = products.Sum(x => Money(x.Price)!.Value * quantities[x.Id]);
            Money(total);
            Require(expectedTotal is null || expectedTotal == total,
                "The total changed. Review the latest prices before recording payment.");
            if (isCash)
            {
                Require(tendered >= total, "Cash received must cover the sale total.");
                var cashError = await CashDrawer.RecordAsync(db, CashDrawerEntryKind.RetailSale, operationId, employee, total, token);
                if (cashError is not null) throw new InventoryRuleException(cashError);
            }
            else
            {
                Require(total > 0 && tendered == total, "The verified online payment must equal the full sale total.");
                Require(!await db.Set<RetailSale>().AnyAsync(x => x.OnlinePaymentReference == reference, token),
                    "This online payment reference has already been recorded.");
            }
            var now = DateTime.UtcNow;
            var shopDate = DateOnly.FromDateTime(now.AddHours(8));
            var sale = new RetailSale { Id = operationId, RecordedAtUtc = now, RecordedByEmployeeId = employee,
                Total = total, Tendered = tendered, VatRate = retailTax.Rate, VatAmount = retailTax.VatIncludedIn(total),
                PaymentMethod = paymentMethod, OnlinePaymentReference = reference };
            foreach (var product in products)
            {
                var quantity = (int)quantities[product.Id];
                var stock = await StockAsync(product.Id, token);
                Require(stock.QuantityOnHand >= quantity, $"Insufficient stock for {product.Name}.");
                var movement = await MoveAsync(product.Id, employee, Guid.NewGuid(), InventoryMovementType.Sale,
                    -quantity, $"Retail checkout {sale.Id}", token);
                await TakeUsableStockAsync(movement, quantity, token, shopDate);
                sale.Lines.Add(new RetailSaleLine { ProductId = product.Id, InventoryMovementId = movement.Id,
                    Name = product.Name, Sku = product.SKU, Quantity = quantity, UnitPrice = product.Price });
            }
            db.Add(sale);
        }, token);
    }
}
