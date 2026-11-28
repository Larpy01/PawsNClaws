using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class GroomingSupplyUsageTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Manager_records_actual_usage_once_with_a_reason_and_earliest_valid_stock()
    {
        var token = TestContext.Current.CancellationToken;
        var data = await SeedAsync(token);
        using var manager = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(manager, data.Manager);
        var page = await manager.GetStringAsync(data.Path, token);
        Assert.Contains(data.Product.Name, page);
        Assert.Contains("mL", page);
        Assert.DoesNotContain(data.Retail.Name, page);
        var csrf = await LocalAppFactory.GetAntiforgeryTokenAsync(manager, data.Path);

        using var noReason = await PostAsync(manager, data, data.Visit.RevisionToken, "6", reason: "", csrf);
        Assert.Equal(HttpStatusCode.OK, noReason.StatusCode);
        Assert.Contains("Explain why usage differs from the estimate.", await noReason.Content.ReadAsStringAsync(token));
        await AssertNoPostingAsync(data, token);

        using var saved = await PostAsync(manager, data, data.Visit.RevisionToken, "6", "Coat needed an extra rinse", csrf);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(data.Path, saved.Headers.Location?.OriginalString);
        Assert.Contains("Recorded: 6 mL", await manager.GetStringAsync(data.Path, token));

        await using var verification = app.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<LocalDbContext>();
        var movement = await db.InventoryMovements.AsNoTracking().SingleAsync(x => x.ProductId == data.Product.Id, token);
        Assert.Equal(data.Visit.Id, movement.GroomingVisitId);
        Assert.Equal(data.Manager.Id, movement.PerformedByUserId);
        Assert.Equal(-6m, movement.QuantityChange);
        Assert.Contains("Coat needed an extra rinse", movement.Reason);
        Assert.Equal(14m, await db.InventoryStocks.Where(x => x.ProductId == data.Product.Id).Select(x => x.QuantityOnHand).SingleAsync(token));
        var batches = await db.Set<InventoryBatch>().Where(x => x.ProductId == data.Product.Id).ToDictionaryAsync(x => x.Lot!, x => x.OnHand, token);
        Assert.Equal(0m, batches["EARLY"]);
        Assert.Equal(7m, batches["LATE"]);
        Assert.Equal(7m, batches["EXPIRED"]);

        using var retry = await PostAsync(manager, data, data.Visit.RevisionToken, "6", "Retry original submission", csrf);
        Assert.True(retry.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.Redirect);
        var visit = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(x => x.Id == data.Visit.Id, token);
        using var duplicate = await PostAsync(manager, data, visit.RevisionToken, "6", "Fresh duplicate submission", csrf);
        Assert.True(duplicate.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.Redirect);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(x => x.ProductId == data.Product.Id, token));
        Assert.Equal(14m, await db.InventoryStocks.Where(x => x.ProductId == data.Product.Id).Select(x => x.QuantityOnHand).SingleAsync(token));
    }

    [Fact]
    public async Task Supply_usage_rejects_invalid_input_unauthorized_roles_and_missing_antiforgery()
    {
        var token = TestContext.Current.CancellationToken;
        var data = await SeedAsync(token);
        using var manager = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(manager, data.Manager);
        var csrf = await LocalAppFactory.GetAntiforgeryTokenAsync(manager, data.Path);

        using var noToken = await PostAsync(manager, data, data.Visit.RevisionToken, "5", "", null);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var cashier = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashier, await app.CreateEmployeeAsync(LocalRoles.Cashier));
        using var blocked = await cashier.GetAsync(data.Path, token);
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Contains("/Account/AccessDenied", blocked.Headers.Location?.OriginalString);
        var cashierCsrf = await LocalAppFactory.GetAntiforgeryTokenAsync(cashier, "/Dashboard");
        using var blockedWrite = await PostAsync(cashier, data, data.Visit.RevisionToken, "5", "", cashierCsrf);
        Assert.Equal(HttpStatusCode.Redirect, blockedWrite.StatusCode);
        Assert.Contains("/Account/AccessDenied", blockedWrite.Headers.Location?.OriginalString);

        using var insufficient = await PostAsync(manager, data, data.Visit.RevisionToken, "14", "More was needed", csrf);
        Assert.Equal(HttpStatusCode.OK, insufficient.StatusCode);
        Assert.Contains("Insufficient usable stock.", await insufficient.Content.ReadAsStringAsync(token));
        using var negative = await PostAsync(manager, data, data.Visit.RevisionToken, "-1", "Invalid amount", csrf);
        Assert.Equal(HttpStatusCode.OK, negative.StatusCode);
        using var retail = await PostAsync(manager, data, data.Visit.RevisionToken, "5", "Wrong product", csrf, data.Retail.Id);
        Assert.Equal(HttpStatusCode.OK, retail.StatusCode);
        await AssertNoPostingAsync(data, token);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var visit = await db.Set<GroomingVisit>().SingleAsync(x => x.Id == data.Visit.Id, token);
        visit.PickedUpAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        using var closed = await PostAsync(manager, data, data.Visit.RevisionToken, "5", "", csrf);
        Assert.True(closed.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Conflict);
        await AssertNoPostingAsync(data, token);
    }

    private async Task AssertNoPostingAsync(UsageFixture data, CancellationToken token)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.False(await db.InventoryMovements.AnyAsync(x => x.ProductId == data.Product.Id || x.ProductId == data.Retail.Id, token));
        Assert.Equal(20m, await db.InventoryStocks.Where(x => x.ProductId == data.Product.Id).Select(x => x.QuantityOnHand).SingleAsync(token));
    }

    private async Task<UsageFixture> SeedAsync(CancellationToken token)
    {
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var product = new Product { Name = "Usage shampoo " + Guid.NewGuid().ToString("N"), IsConsumable = true, Unit = "mL", TracksExpiry = true };
        var retail = new Product { Name = "Retail-only brush " + Guid.NewGuid().ToString("N") };
        var snapshot = new QuoteSnapshot("Sample Owner", "Sample Pet", "Dog", null, null, null,
            [], [new QuoteSupply(product.Id, product.Name, 5m, product.Unit)], 200m, 200m, null, null);
        var visit = new GroomingVisit
        {
            EstimateId = Guid.NewGuid(), EstimateVersion = 1, EstimateSnapshot = JsonSerializer.Serialize(snapshot),
            StartedByEmployeeId = manager.Id, StartedAtUtc = DateTime.UtcNow, RevisionToken = Guid.NewGuid()
        };
        var checkIn = new GroomingCheckIn
        {
            VisitId = visit.Id, EmployeeId = manager.Id, RecordedAtUtc = DateTime.UtcNow,
            Assessment = JsonSerializer.Serialize(new GroomingCheckInForm())
        };
        var day = DateOnly.FromDateTime(DateTime.Today);
        db.AddRange(product, retail, visit, checkIn,
            new InventoryStock { ProductId = product.Id, QuantityOnHand = 20 },
            new InventoryBatch { ProductId = product.Id, Lot = "EARLY", OnHand = 5, ExpiryDate = day.AddDays(1) },
            new InventoryBatch { ProductId = product.Id, Lot = "LATE", OnHand = 8, ExpiryDate = day.AddDays(3) },
            new InventoryBatch { ProductId = product.Id, Lot = "EXPIRED", OnHand = 7, ExpiryDate = day.AddDays(-1) });
        await db.SaveChangesAsync(token);
        return new(manager, product, retail, visit);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, UsageFixture data, Guid revision,
        string quantity, string reason, string? csrf, Guid? product = null)
    {
        var values = new Dictionary<string, string>
        {
            ["revision"] = revision.ToString(), ["productId"] = (product ?? data.Product.Id).ToString(),
            ["quantity"] = quantity, ["reason"] = reason
        };
        if (csrf is not null)
            values["__RequestVerificationToken"] = csrf;
        using var body = new FormUrlEncodedContent(values);
        return await client.PostAsync(data.Path, body, TestContext.Current.CancellationToken);
    }

    private sealed record UsageFixture(TestEmployee Manager, Product Product, Product Retail, GroomingVisit Visit)
    {
        public string Path => $"/GroomingVisits/{Visit.Id}/Supplies";
    }
}
