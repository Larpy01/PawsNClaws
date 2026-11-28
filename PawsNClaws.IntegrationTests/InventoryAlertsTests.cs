using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;
using PawsNClaws.Local.Models;

namespace PawsNClaws.IntegrationTests;

public sealed class InventoryAlertsTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Managers_see_actionable_stock_and_expiry_alerts_and_receiving_clears_low_stock()
    {
        var token = TestContext.Current.CancellationToken;
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var staff = await app.CreateEmployeeAsync(LocalRoles.InventoryStaff);
        var today = PhilippineDateTime.Today;
        var low = new Product { SKU = "LOW-ONE", Name = "Alert shampoo" };
        var empty = new Product { SKU = "LOW-ZERO", Name = "Alert comb" };
        var healthy = new Product { SKU = "HEALTHY", Name = "Alert brush" };
        var inactive = new Product { SKU = "INACTIVE", Name = "Archived food", IsActive = false };
        var unconfigured = new Product { SKU = "NO-LEVEL", Name = "Unconfigured stock" };

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.AddRange(low, empty, healthy, inactive, unconfigured,
                new InventoryStock { ProductId = low.Id, QuantityOnHand = 17, ReorderLevel = 5 },
                new InventoryStock { ProductId = empty.Id, ReorderLevel = 2 },
                new InventoryStock { ProductId = healthy.Id, QuantityOnHand = 6, ReorderLevel = 5 },
                new InventoryStock { ProductId = inactive.Id, QuantityOnHand = 1, ReorderLevel = 2 },
                new InventoryStock { ProductId = unconfigured.Id },
                new InventoryBatch { ProductId = low.Id, Lot = "LOT-TODAY", ExpiryDate = today, OnHand = 7, Quarantined = 2 },
                new InventoryBatch { ProductId = low.Id, Lot = "LOT-YESTERDAY", ExpiryDate = today.AddDays(-1), OnHand = 10 },
                new InventoryBatch { ProductId = inactive.Id, Lot = "LOT-ARCHIVED", ExpiryDate = today.AddDays(-2), OnHand = 1, Quarantined = 1 },
                new InventoryBatch { ProductId = healthy.Id, Lot = "LOT-DAY-30", ExpiryDate = today.AddDays(30), OnHand = 3 },
                new InventoryBatch { ProductId = healthy.Id, Lot = "LOT-DAY-31", ExpiryDate = today.AddDays(31), OnHand = 2 },
                new InventoryBatch { ProductId = healthy.Id, Lot = "LOT-NO-DATE", OnHand = 1 },
                new InventoryBatch { ProductId = empty.Id, Lot = "LOT-DEPLETED", ExpiryDate = today.AddDays(-1) });
            await db.SaveChangesAsync(token);
        }

        const string alertsUrl = "/Inventory/Alerts";
        using var managerClient = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerClient, manager);
        Assert.Contains(alertsUrl, await managerClient.GetStringAsync("/Inventory", token));
        using var response = await managerClient.GetAsync(alertsUrl, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync(token);
        var lowStock = Section(html, "low-stock");
        Assert.Contains("Low stock (2)", lowStock);
        Assert.Contains(low.SKU, lowStock);
        Assert.Contains(empty.SKU, lowStock);
        Assert.DoesNotContain(healthy.SKU, lowStock);
        Assert.DoesNotContain(inactive.SKU, lowStock);
        Assert.DoesNotContain(unconfigured.SKU, lowStock);
        Assert.Contains("LOT-YESTERDAY", Section(html, "expired"));
        Assert.Contains("LOT-ARCHIVED", Section(html, "expired"));
        Assert.Contains("LOT-TODAY", Section(html, "expiring"));
        Assert.Contains("LOT-DAY-30", Section(html, "expiring"));
        Assert.DoesNotContain("LOT-DAY-31", html);
        Assert.DoesNotContain("LOT-NO-DATE", html);
        Assert.DoesNotContain("LOT-DEPLETED", html);

        using var staffClient = app.CreateBrowser();
        using var staffLogin = await LocalAppFactory.SignInAsync(staffClient, staff);
        using var denied = await staffClient.GetAsync(alertsUrl, token);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location?.OriginalString);
        Assert.DoesNotContain(alertsUrl, await staffClient.GetStringAsync("/Inventory", token));

        using var receipt = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(staffClient, "/Inventory"),
            ["OperationId"] = Guid.NewGuid().ToString(), ["ProductId"] = low.Id.ToString(),
            ["Supplier"] = "Alerts fixture", ["Delivered"] = "1", ["Accepted"] = "1"
        });
        using var received = await staffClient.PostAsync("/Inventory/Receive", receipt, token);
        Assert.Equal(HttpStatusCode.Redirect, received.StatusCode);
        var updated = await managerClient.GetStringAsync(alertsUrl, token);
        Assert.Contains("Low stock (1)", Section(updated, "low-stock"));
        Assert.DoesNotContain(low.SKU, Section(updated, "low-stock"));
        Assert.Contains("LOT-YESTERDAY", Section(updated, "expired"));
    }

    private static string Section(string page, string heading)
    {
        var start = page.IndexOf($"aria-labelledby=\"{heading}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {heading} section.");
        return page[start..page.IndexOf("</section>", start, StringComparison.Ordinal)];
    }
}
