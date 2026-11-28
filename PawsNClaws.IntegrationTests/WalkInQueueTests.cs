using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class WalkInQueueTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Cashier_calls_in_arrival_order_and_manager_override_keeps_an_audit_reason()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var owner = new CustomerRecord { FirstName = "Alex", LastName = "Reyes" };
        var first = new PetRecord { CustomerId = owner.Id, Name = "Poppy", Species = "Dog" };
        var second = new PetRecord { CustomerId = owner.Id, Name = "Pip", Species = "Cat" };
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var arrived = DateTime.UtcNow.AddMinutes(-5);
        var early = new WalkInEntry { PetId = first.Id, AddedBy = cashier.Id, ArrivedAtUtc = arrived };
        var late = new WalkInEntry { PetId = second.Id, AddedBy = cashier.Id, ArrivedAtUtc = arrived.AddMinutes(1) };
        db.AddRange(owner, first, second, early, late);
        await db.SaveChangesAsync(token);

        using var cashierBrowser = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashierBrowser, cashier);
        using var managerBrowser = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        const string path = "/WalkInQueue";
        async Task<string> CallAsync(HttpClient browser, WalkInEntry entry, string reason)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(browser, path),
                ["revision"] = entry.Revision.ToString(), ["reason"] = reason
            });
            using var response = await browser.PostAsync($"{path}/{entry.Id}/Call", body, token);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return await browser.GetStringAsync(path, token);
        }

        Assert.Contains("Call the first pet", await CallAsync(cashierBrowser, late, "Priority"));
        Assert.Contains("Enter a reason", await CallAsync(managerBrowser, late, ""));
        Assert.Contains("Priority reason: Appointment correction", await CallAsync(managerBrowser, late, "Appointment correction"));
        Assert.Contains("Pet marked as called", await CallAsync(cashierBrowser, early, ""));
        var saved = await db.Set<WalkInEntry>().AsNoTracking().SingleAsync(x => x.Id == late.Id, token);
        Assert.Equal(manager.Id, saved.CalledBy);
        Assert.Equal("Appointment correction", saved.PriorityReason);
    }
}
