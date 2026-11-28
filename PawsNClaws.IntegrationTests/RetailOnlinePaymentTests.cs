using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class RetailOnlinePaymentTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Online_payment_requires_a_unique_reference_and_refund_keeps_cash_drawer_unchanged()
    {
        var token = TestContext.Current.CancellationToken;
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var product = new Product { SKU = "ONLINE-001", Name = "Pet comb", Price = 112m };
        db.AddRange(product, new InventoryStock { ProductId = product.Id, QuantityOnHand = 3 },
            new InventoryBatch { ProductId = product.Id, OnHand = 3 },
            new CashDrawerShift { OpeningCash = 500m, OpenedAtUtc = DateTime.UtcNow, OpenedByEmployeeId = cashier.Id });
        await db.SaveChangesAsync(token);

        const string checkout = "/RetailCheckout";
        using var cashierClient = app.CreateBrowser();
        using var cashierSignIn = await LocalAppFactory.SignInAsync(cashierClient, cashier);
        using var managerClient = app.CreateBrowser();
        using var managerSignIn = await LocalAppFactory.SignInAsync(managerClient, manager);
        var form = await cashierClient.GetStringAsync(checkout, token);
        Assert.Contains("GCash / Online payment", form);
        var checkoutToken = await LocalAppFactory.GetAntiforgeryTokenAsync(cashierClient, checkout);
        var saleId = Guid.NewGuid();
        const string paymentReference = "000123-ABC";

        async Task<HttpResponseMessage> PayAsync(Guid operationId, string reference, string amount = "112.00",
            string method = "Online")
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["operationId"] = operationId.ToString(), ["paymentMethod"] = method, ["tendered"] = amount,
                ["expectedTotal"] = "112.00", ["onlinePaymentReference"] = reference,
                ["lines[0].ProductId"] = product.Id.ToString(), ["lines[0].Quantity"] = "1",
                ["__RequestVerificationToken"] = checkoutToken
            });
            return await cashierClient.PostAsync(checkout, body, token);
        }

        using var missing = await PayAsync(saleId, "");
        Assert.Contains("Enter the online payment reference number.", await missing.Content.ReadAsStringAsync(token));
        using var underpayment = await PayAsync(saleId, paymentReference, "111.99");
        Assert.Contains("must equal the full sale total", await underpayment.Content.ReadAsStringAsync(token));
        Assert.Empty(await db.Set<RetailSale>().ToListAsync(token));

        using var paid = await PayAsync(saleId, "  000123-abc  ");
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        var salePath = $"{checkout}/{saleId}";
        Assert.Equal(salePath, paid.Headers.Location?.OriginalString);
        using var replay = await PayAsync(saleId, paymentReference);
        Assert.Equal(salePath, replay.Headers.Location?.OriginalString);
        using var sameIdDifferentMethod = await PayAsync(saleId, "", method: "Cash");
        Assert.Contains("already belongs to another sale", await sameIdDifferentMethod.Content.ReadAsStringAsync(token));
        using var duplicate = await PayAsync(Guid.NewGuid(), paymentReference);
        Assert.Contains("online payment reference has already been recorded", await duplicate.Content.ReadAsStringAsync(token));

        var record = await db.Set<RetailSale>().AsNoTracking().SingleAsync(token);
        Assert.Equal(RetailPaymentMethod.Online, record.PaymentMethod);
        Assert.Equal(paymentReference, record.OnlinePaymentReference);
        Assert.Equal(record.Total, record.Tendered);
        Assert.Equal(2m, await db.InventoryStocks.Select(x => x.QuantityOnHand).SingleAsync(token));
        Assert.Empty(await db.Set<CashDrawerEntry>().ToListAsync(token));
        var details = await managerClient.GetStringAsync(salePath, token);
        Assert.Contains(paymentReference, details);
        Assert.DoesNotContain("Cash received", details);
        Assert.Contains("Record full online refund", details);
        Assert.Contains("GCash / Online", await managerClient.GetStringAsync(checkout + "/Sales", token));

        var refundToken = await LocalAppFactory.GetAntiforgeryTokenAsync(managerClient, salePath);
        async Task<HttpResponseMessage> RefundAsync(string confirmation)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["reason"] = "Returned unopened.", ["stockVerified"] = "true",
                [confirmation] = "true", ["__RequestVerificationToken"] = refundToken
            });
            return await managerClient.PostAsync(salePath + "/Void", body, token);
        }

        using var wrongRefundMethod = await RefundAsync("cashReturned");
        Assert.Contains("Confirm the full amount was refunded online.", await wrongRefundMethod.Content.ReadAsStringAsync(token));
        using var refund = await RefundAsync("onlineReturned");
        Assert.Equal(salePath, refund.Headers.Location?.OriginalString);
        Assert.Equal(3m, await db.InventoryStocks.Select(x => x.QuantityOnHand).SingleAsync(token));
        Assert.NotNull((await db.Set<RetailSale>().AsNoTracking().SingleAsync(token)).VoidedAtUtc);
        Assert.Empty(await db.Set<CashDrawerEntry>().ToListAsync(token));
    }
}
