using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class RetailVoidTests
{
    [Fact]
    public async Task Manager_records_full_refund_and_restores_original_lots_to_quarantine_once()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = new LocalAppFactory("Testing", vatRegistered: true);
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        using var managerBrowser = app.CreateBrowser();
        using var managerSession = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        const string createProduct = "/Products/Create";
        var productForm = await managerBrowser.GetStringAsync(createProduct, token);
        Assert.Contains("VAT-inclusive selling price", productForm);
        Assert.Contains("data-price=\"319.00\"", productForm);
        const string sku = "RTN-001";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, createProduct),
            ["SKU"] = sku, ["Name"] = "Glam Organic Forever Love Dog Shampoo 500ml", ["Price"] = "319.00"
        });
        using var created = await managerBrowser.PostAsync(createProduct, form, token);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var product = await db.Products.SingleAsync(x => x.SKU == sku, token);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var early = new InventoryBatch
        {
            ProductId = product.Id, Lot = "Early lot", OnHand = 3, Quarantined = 1,
            ExpiryDate = today.AddDays(2)
        };
        var later = new InventoryBatch
        {
            ProductId = product.Id, Lot = "Later lot", OnHand = 5, ExpiryDate = today.AddDays(3)
        };
        var untouched = new InventoryBatch
        {
            ProductId = product.Id, Lot = "Untouched lot", OnHand = 4, ExpiryDate = today.AddDays(4)
        };
        db.AddRange(early, later, untouched, new InventoryStock { ProductId = product.Id, QuantityOnHand = 12 });
        await db.SaveChangesAsync(token);
        var stock = await managerBrowser.GetStringAsync("/Inventory", token);
        Assert.Contains("VAT-inclusive selling price", stock);
        Assert.Contains(">319.00</td>", stock);

        using var cashierBrowser = app.CreateBrowser();
        using var cashierSession = await LocalAppFactory.SignInAsync(cashierBrowser, cashier);
        const string checkout = "/RetailCheckout";
        Assert.Contains("data-price=\"319.00\"", await cashierBrowser.GetStringAsync(checkout, token));
        var cashierToken = await LocalAppFactory.GetAntiforgeryTokenAsync(cashierBrowser, checkout);
        var saleId = Guid.NewGuid();
        using var cart = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = cashierToken, ["operationId"] = saleId.ToString(),
            ["tendered"] = "1500", ["expectedTotal"] = "1276.00",
            ["lines[0].ProductId"] = product.Id.ToString(), ["lines[0].Quantity"] = "4"
        });
        using var saleResponse = await cashierBrowser.PostAsync(checkout, cart, token);
        Assert.Equal(HttpStatusCode.Redirect, saleResponse.StatusCode);
        var salePath = $"{checkout}/{saleId}";
        var voidPath = $"{salePath}/Void";
        var managerPage = await managerBrowser.GetStringAsync(salePath, token);
        Assert.Contains("VAT (12%)", managerPage);
        Assert.Contains("PHP 136.71", managerPage);
        Assert.Contains("PHP 1,139.29", managerPage);
        Assert.Contains(voidPath, managerPage);
        Assert.Contains(early.Lot!, managerPage);
        Assert.Contains(later.Lot!, managerPage);
        Assert.DoesNotContain(untouched.Lot!, managerPage);
        var cashierPage = await cashierBrowser.GetStringAsync(salePath, token);
        Assert.DoesNotContain(voidPath, cashierPage);
        Assert.Contains(salePath, await managerBrowser.GetStringAsync($"{checkout}/Sales", token));
        var managerToken = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, salePath);

        async Task<HttpResponseMessage> VoidAsync(HttpClient browser, string? antiforgeryToken,
            string reason = "  Returned unopened, held for quality review.  ",
            bool stockVerified = true, bool cashReturned = true)
        {
            var fields = new Dictionary<string, string>
            {
                ["reason"] = reason, ["stockVerified"] = stockVerified.ToString(),
                ["cashReturned"] = cashReturned.ToString(), ["quarantineReturned"] = "true"
            };
            if (antiforgeryToken is not null) fields["__RequestVerificationToken"] = antiforgeryToken;
            using var content = new FormUrlEncodedContent(fields);
            return await browser.PostAsync(voidPath, content, token);
        }

        using var denied = await VoidAsync(cashierBrowser, cashierToken);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location?.OriginalString);
        using var missingToken = await VoidAsync(managerBrowser, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var unverifiedStock = await VoidAsync(managerBrowser, managerToken, stockVerified: false);
        Assert.Equal(HttpStatusCode.OK, unverifiedStock.StatusCode);
        using var unreturnedCash = await VoidAsync(managerBrowser, managerToken, cashReturned: false);
        Assert.Equal(HttpStatusCode.OK, unreturnedCash.StatusCode);
        using var blankReason = await VoidAsync(managerBrowser, managerToken, reason: " ");
        Assert.Equal(HttpStatusCode.OK, blankReason.StatusCode);
        var original = await db.Set<InventoryMovement>().AsNoTracking().Include(x => x.Allocations)
            .SingleAsync(x => x.ProductId == product.Id, token);
        Assert.Equal(InventoryMovementType.Sale, original.MovementType);
        Assert.Equal(2, original.Allocations.Count);

        using var voided = await VoidAsync(managerBrowser, managerToken);
        Assert.Equal(HttpStatusCode.Redirect, voided.StatusCode);
        Assert.Equal(salePath, voided.Headers.Location?.OriginalString);
        var receipt = await managerBrowser.GetStringAsync(salePath, token);
        Assert.Contains("Refunded", receipt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("held for quality review", receipt);
        Assert.DoesNotContain(voidPath, receipt);
        using var repeated = await VoidAsync(managerBrowser, managerToken, reason: "Attempt to replace the original reason");
        Assert.True(repeated.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);

        var savedSale = await db.Set<RetailSale>().AsNoTracking().SingleAsync(x => x.Id == saleId, token);
        Assert.Equal(1276m, savedSale.Total);
        Assert.Equal(1500m, savedSale.Tendered);
        Assert.Equal(0.12m, savedSale.VatRate);
        Assert.Equal(136.71m, savedSale.VatAmount);
        Assert.Equal(cashier.Id, savedSale.RecordedByEmployeeId);
        Assert.Equal(manager.Id, savedSale.VoidedByEmployeeId);
        Assert.NotNull(savedSale.VoidedAtUtc);
        Assert.Equal("Returned unopened, held for quality review.", savedSale.VoidReason);
        Assert.True(savedSale.ReturnsQuarantined);
        var reversal = await db.Set<InventoryMovement>().AsNoTracking().Include(x => x.Allocations)
            .SingleAsync(x => x.ReversesMovementId == original.Id, token);
        Assert.Equal(InventoryMovementType.SaleReversal, reversal.MovementType);
        Assert.Equal(manager.Id, reversal.PerformedByUserId);
        Assert.Equal(4m, reversal.QuantityChange);
        Assert.Equal(
            original.Allocations.Select(x => (x.BatchId, -x.QuantityChange)).OrderBy(x => x.BatchId),
            reversal.Allocations.Select(x => (x.BatchId, x.QuantityChange)).OrderBy(x => x.BatchId));
        Assert.Equal(2, await db.Set<InventoryMovement>().CountAsync(x => x.ProductId == product.Id, token));

        var restored = await db.Set<InventoryBatch>().AsNoTracking().Where(x => x.ProductId == product.Id)
            .ToDictionaryAsync(x => x.Id, token);
        Assert.Equal((3m, 3m), (restored[early.Id].OnHand, restored[early.Id].Quarantined));
        Assert.Equal((5m, 2m), (restored[later.Id].OnHand, restored[later.Id].Quarantined));
        Assert.Equal((4m, 0m), (restored[untouched.Id].OnHand, restored[untouched.Id].Quarantined));
        Assert.Equal(12m, await db.Set<InventoryStock>().AsNoTracking().Where(x => x.ProductId == product.Id)
            .Select(x => x.QuantityOnHand).SingleAsync(token));
    }
}
