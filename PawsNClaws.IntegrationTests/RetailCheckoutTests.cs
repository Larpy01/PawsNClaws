using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class RetailCheckoutTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Cashier_checkout_updates_every_line_once_and_failed_cart_leaves_stock_alone()
    {
        var token = TestContext.Current.CancellationToken;
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var groomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var shampoo = new Product { SKU = "RET-001", Name = "Retail pet shampoo", Price = 125.50m };
        var brush = new Product { SKU = "RET-002", Name = "Retail pet brush", Price = 89m };
        var internalProduct = new Product { SKU = "RET-003", Name = "Grooming use only", Price = 88m, IsSellable = false, IsConsumable = true };
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var shampooBatch = new InventoryBatch { ProductId = shampoo.Id, OnHand = 6, ExpiryDate = today.AddDays(2) };
        var brushBatch = new InventoryBatch { ProductId = brush.Id, OnHand = 4, Quarantined = 2, ExpiryDate = today.AddDays(2) };
        var expiredBrushBatch = new InventoryBatch { ProductId = brush.Id, OnHand = 3, ExpiryDate = today.AddDays(-1) };
        db.AddRange(shampoo, brush, internalProduct, shampooBatch, brushBatch, expiredBrushBatch,
            new InventoryStock { ProductId = shampoo.Id, QuantityOnHand = 6 },
            new InventoryStock { ProductId = brush.Id, QuantityOnHand = 7 });
        await db.SaveChangesAsync(token);

        using var browser = app.CreateBrowser();
        using var session = await LocalAppFactory.SignInAsync(browser, cashier);
        using var other = app.CreateBrowser();
        using var otherSession = await LocalAppFactory.SignInAsync(other, groomer);
        const string checkout = "/RetailCheckout";
        var catalog = await browser.GetStringAsync(checkout, token);
        Assert.Contains(shampoo.Name, catalog);
        Assert.Contains(brush.Name, catalog);
        Assert.DoesNotContain(internalProduct.Name, catalog);
        using var unauthorized = await other.GetAsync(checkout, token);
        Assert.Contains("/Account/AccessDenied", unauthorized.Headers.Location?.OriginalString);
        var csrf = await LocalAppFactory.GetAntiforgeryTokenAsync(browser, checkout);

        async Task<HttpResponseMessage> CheckoutAsync(Guid operationId, string tendered,
            IReadOnlyList<(Guid ProductId, string Quantity)> lines, bool includeToken = true)
        {
            var fields = new Dictionary<string, string>
            {
                ["operationId"] = operationId.ToString(), ["tendered"] = tendered
            };
            if (includeToken) fields["__RequestVerificationToken"] = csrf;
            for (var i = 0; i < lines.Count; i++)
            {
                fields[$"lines[{i}].ProductId"] = lines[i].ProductId.ToString();
                fields[$"lines[{i}].Quantity"] = lines[i].Quantity;
            }
            using var body = new FormUrlEncodedContent(fields);
            return await browser.PostAsync(checkout, body, token);
        }
        Task<decimal> StockAsync(Guid productId) => db.Set<InventoryStock>().AsNoTracking()
            .Where(x => x.ProductId == productId).Select(x => x.QuantityOnHand).SingleAsync(token);
        var validCart = new[] { (shampoo.Id, "2"), (brush.Id, "1") };

        using var missingToken = await CheckoutAsync(Guid.NewGuid(), "400", validCart, includeToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var invalidStock = await CheckoutAsync(Guid.NewGuid(), "600", [(shampoo.Id, "2"), (brush.Id, "3")]);
        Assert.Equal(HttpStatusCode.OK, invalidStock.StatusCode);
        using var insufficientCash = await CheckoutAsync(Guid.NewGuid(), "339.99", validCart);
        Assert.Equal(HttpStatusCode.OK, insufficientCash.StatusCode);
        using var fractional = await CheckoutAsync(Guid.NewGuid(), "400", [(shampoo.Id, "1.5")]);
        Assert.Equal(HttpStatusCode.OK, fractional.StatusCode);
        using var internalItem = await CheckoutAsync(Guid.NewGuid(), "400", [(internalProduct.Id, "1")]);
        Assert.Equal(HttpStatusCode.OK, internalItem.StatusCode);
        Assert.Equal(6m, await StockAsync(shampoo.Id));
        Assert.Equal(7m, await StockAsync(brush.Id));
        Assert.Empty(await db.Set<InventoryMovement>().AsNoTracking().ToListAsync(token));

        var operationId = Guid.NewGuid();
        using var sold = await CheckoutAsync(operationId, "400", validCart);
        Assert.Equal(HttpStatusCode.Redirect, sold.StatusCode);
        var receiptPath = sold.Headers.Location?.OriginalString;
        Assert.Equal($"{checkout}/{operationId}", receiptPath);
        var receipt = await browser.GetStringAsync(receiptPath!, token);
        Assert.Contains(shampoo.Name, receipt);
        Assert.Contains(brush.Name, receipt);
        Assert.Contains("340.00", receipt);
        Assert.Contains("60.00", receipt);
        Assert.Contains("Non-VAT sale", receipt);

        using var replay = await CheckoutAsync(operationId, "400", validCart);
        Assert.Equal(receiptPath, replay.Headers.Location?.OriginalString);
        using var mismatchedReplay = await CheckoutAsync(operationId, "450", validCart);
        Assert.Equal(HttpStatusCode.OK, mismatchedReplay.StatusCode);
        Assert.Equal(4m, await StockAsync(shampoo.Id));
        Assert.Equal(6m, await StockAsync(brush.Id));
        Assert.Equal(3m, await db.Set<InventoryBatch>().AsNoTracking().Where(x => x.Id == expiredBrushBatch.Id)
            .Select(x => x.OnHand).SingleAsync(token));
        var movements = await db.Set<InventoryMovement>().AsNoTracking().ToListAsync(token);
        Assert.Equal(2, movements.Count);
        Assert.All(movements, movement => Assert.Equal(InventoryMovementType.Sale, movement.MovementType));
        Assert.Equal(-2m, Assert.Single(movements, x => x.ProductId == shampoo.Id).QuantityChange);
        Assert.Equal(-1m, Assert.Single(movements, x => x.ProductId == brush.Id).QuantityChange);
        var receiptRecord = await db.Set<RetailSale>().AsNoTracking().Include(x => x.Lines).SingleAsync(token);
        Assert.Equal(operationId, receiptRecord.Id);
        Assert.Equal(340m, receiptRecord.Total);
        Assert.Equal(400m, receiptRecord.Tendered);
        Assert.Equal(0m, receiptRecord.VatRate);
        Assert.Equal(0m, receiptRecord.VatAmount);
        Assert.Equal(cashier.Id, receiptRecord.RecordedByEmployeeId);
        Assert.Equal(movements.Select(x => x.Id).Order(), receiptRecord.Lines.Select(x => x.InventoryMovementId).Order());
    }
}
