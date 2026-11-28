using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class CashDrawerTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Shared_shift_reconciles_cash_payments_refunds_and_manual_movements_once()
    {
        var token = TestContext.Current.CancellationToken;
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var groomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var product = new Product { Name = "Retail comb", SKU = "DRAWER-COMB", Price = 100m };
        db.AddRange(product, new InventoryStock { ProductId = product.Id, QuantityOnHand = 2 },
            new InventoryBatch { ProductId = product.Id, OnHand = 2, Lot = "DRAWER-LOT" });
        var visit = new GroomingVisit
        {
            EstimateId = Guid.NewGuid(), EstimateVersion = 1, RevisionToken = Guid.NewGuid(),
            StartedByEmployeeId = cashier.Id, StartedAtUtc = DateTime.UtcNow,
            EstimateSnapshot = JsonSerializer.Serialize(new QuoteSnapshot("Test Owner", "Drawer Test Pet", "Dog",
                null, null, null, [new QuoteLine(Guid.NewGuid(), "Bath", ["Bath"], 500m)], [], 500m, 500m, null, null))
        };
        db.AddRange(visit, new GroomingCheckIn
        {
            VisitId = visit.Id, EmployeeId = cashier.Id, RecordedAtUtc = DateTime.UtcNow,
            Assessment = JsonSerializer.Serialize(new GroomingCheckInForm())
        });
        await db.SaveChangesAsync(token);

        using var cashierBrowser = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashierBrowser, cashier);
        using var managerBrowser = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        using var groomerBrowser = app.CreateBrowser();
        using var groomerLogin = await LocalAppFactory.SignInAsync(groomerBrowser, groomer);
        const string drawer = "/CashDrawer";
        var cashierCsrf = await LocalAppFactory.GetAntiforgeryTokenAsync(cashierBrowser, "/Dashboard");
        var managerCsrf = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, "/Dashboard");
        var groomerCsrf = await LocalAppFactory.GetAntiforgeryTokenAsync(groomerBrowser, "/Dashboard");

        async Task<HttpResponseMessage> PostAsync(HttpClient browser, string path, string? csrf,
            Dictionary<string, string> fields)
        {
            if (csrf is not null) fields["__RequestVerificationToken"] = csrf;
            using var body = new FormUrlEncodedContent(fields);
            return await browser.PostAsync(path, body, token);
        }

        async Task<CashDrawerShift> ShiftAsync() => await db.Set<CashDrawerShift>().AsNoTracking().SingleAsync(token);

        using var unauthorized = await PostAsync(groomerBrowser, drawer + "/Open", groomerCsrf, new() { ["openingCash"] = "500" });
        Assert.Contains("/Account/AccessDenied", unauthorized.Headers.Location?.OriginalString);
        using var noToken = await PostAsync(cashierBrowser, drawer + "/Open", null, new() { ["openingCash"] = "500" });
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var opened = await PostAsync(cashierBrowser, drawer + "/Open", cashierCsrf, new() { ["openingCash"] = "500" });
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.Equal(drawer, opened.Headers.Location?.OriginalString);
        var shift = await ShiftAsync();
        Assert.Equal(cashier.Id, shift.OpenedByEmployeeId);
        Assert.Equal(500m, shift.OpeningCash);
        using var duplicateOpen = await PostAsync(managerBrowser, drawer + "/Open", managerCsrf, new() { ["openingCash"] = "700" });
        Assert.Equal(shift.Id, (await ShiftAsync()).Id);
        Assert.Equal(500m, (await ShiftAsync()).OpeningCash);

        var addId = Guid.NewGuid();
        async Task<HttpResponseMessage> MovementAsync(Guid operationId, string amount, string reason) =>
            await PostAsync(cashierBrowser, drawer + "/Movement", cashierCsrf, new()
            {
                ["shiftId"] = shift.Id.ToString(), ["operationId"] = operationId.ToString(),
                ["amount"] = amount, ["reason"] = reason
            });
        using var added = await MovementAsync(addId, "25", "Change float top-up");
        using var replayedMovement = await MovementAsync(addId, "25", "Change float top-up");
        using var invalidMovement = await MovementAsync(Guid.NewGuid(), "0.001", "Invalid centavo fraction");
        using var excessivePayout = await MovementAsync(Guid.NewGuid(), "-526", "Would overdraw the drawer");
        var payoutId = Guid.NewGuid();
        using var paidOut = await MovementAsync(payoutId, "-50", "Approved cash drop");

        var saleId = Guid.NewGuid();
        async Task<HttpResponseMessage> RetailAsync(Guid operationId) =>
            await PostAsync(cashierBrowser, "/RetailCheckout", cashierCsrf, new()
            {
                ["operationId"] = operationId.ToString(), ["tendered"] = "200",
                ["lines[0].ProductId"] = product.Id.ToString(), ["lines[0].Quantity"] = "1"
            });
        using var sale = await RetailAsync(saleId);
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        Assert.Equal($"/RetailCheckout/{saleId}", sale.Headers.Location?.OriginalString);
        using var replayedRetail = await RetailAsync(saleId);
        Assert.Equal(sale.Headers.Location, replayedRetail.Headers.Location);

        var billPath = $"/VisitBilling/{visit.Id}";
        using var issued = await PostAsync(cashierBrowser, billPath + "/Issue", cashierCsrf, new());
        Assert.Equal(billPath, issued.Headers.Location?.OriginalString);
        var bill = await db.Set<GroomingBill>().AsNoTracking().SingleAsync(x => x.VisitId == visit.Id, token);
        var paymentId = Guid.NewGuid();
        async Task<HttpResponseMessage> GroomingCashAsync(Guid id, Guid revision) =>
            await PostAsync(cashierBrowser, billPath + "/Cash", cashierCsrf, new()
            {
                ["operationId"] = id.ToString(), ["revision"] = revision.ToString(),
                ["amount"] = "150", ["tendered"] = "200"
            });
        using var payment = await GroomingCashAsync(paymentId, bill.Revision);
        Assert.Equal(billPath, payment.Headers.Location?.OriginalString);
        using var replayedPayment = await GroomingCashAsync(paymentId, bill.Revision);
        Assert.Equal(payment.Headers.Location, replayedPayment.Headers.Location);

        async Task<HttpResponseMessage> RefundAsync() =>
            await PostAsync(managerBrowser, $"/RetailCheckout/{saleId}/Void", managerCsrf, new()
            {
                ["reason"] = "Complete return", ["cashReturned"] = "true", ["stockVerified"] = "true"
            });
        using var refund = await RefundAsync();
        Assert.Equal($"/RetailCheckout/{saleId}", refund.Headers.Location?.OriginalString);
        using var replayedRefund = await RefundAsync();
        Assert.Equal(refund.Headers.Location, replayedRefund.Headers.Location);

        var entries = await db.Set<CashDrawerEntry>().AsNoTracking().Where(x => x.ShiftId == shift.Id).ToListAsync(token);
        Assert.Equal(5, entries.Count);
        Assert.Equal(125m, entries.Sum(x => x.Amount));
        Assert.Contains(entries, x => x.Amount == 25m && x.Reason == "Change float top-up");
        Assert.Contains(entries, x => x.Amount == -50m && x.Reason == "Approved cash drop");
        Assert.Contains(entries, x => x.Kind.ToString() == "RetailSale" && x.Amount == 100m && x.ReferenceId == saleId);
        Assert.Contains(entries, x => x.Kind.ToString() == "GroomingPayment" && x.Amount == 150m && x.ReferenceId == paymentId);
        Assert.Contains(entries, x => x.Kind.ToString() == "RetailRefund" && x.Amount == -100m &&
            x.ReferenceId == saleId && x.RecordedByEmployeeId == manager.Id);
        var activePage = await cashierBrowser.GetStringAsync(drawer, token);
        Assert.Contains("625.00", activePage);

        using var wrongShift = await PostAsync(managerBrowser, drawer + "/Close", managerCsrf,
            new() { ["shiftId"] = Guid.NewGuid().ToString(), ["countedCash"] = "600" });
        Assert.Null((await ShiftAsync()).ClosedAtUtc);
        using var closed = await PostAsync(managerBrowser, drawer + "/Close", managerCsrf,
            new() { ["shiftId"] = shift.Id.ToString(), ["countedCash"] = "600" });
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        var closedShift = await ShiftAsync();
        Assert.NotNull(closedShift.ClosedAtUtc);
        Assert.Equal(manager.Id, closedShift.ClosedByEmployeeId);
        Assert.Equal(600m, closedShift.CountedCash);
        Assert.Equal(625m, closedShift.ExpectedAtClose);
        var closedPage = await managerBrowser.GetStringAsync(drawer, token);
        Assert.Contains("25.00", closedPage);

        var blockedSaleId = Guid.NewGuid();
        using var afterCloseSale = await RetailAsync(blockedSaleId);
        Assert.False(await db.Set<RetailSale>().AnyAsync(x => x.Id == blockedSaleId, token));
        var currentBill = await db.Set<GroomingBill>().AsNoTracking().SingleAsync(x => x.VisitId == visit.Id, token);
        var blockedPaymentId = Guid.NewGuid();
        using var afterCloseBill = await GroomingCashAsync(blockedPaymentId, currentBill.Revision);
        Assert.False(await db.Set<GroomingCashPayment>().AnyAsync(x => x.Id == blockedPaymentId, token));
        using var afterCloseReplay = await RetailAsync(saleId);
        Assert.Equal(sale.Headers.Location, afterCloseReplay.Headers.Location);
        Assert.Equal(5, await db.Set<CashDrawerEntry>().CountAsync(token));
    }
}
