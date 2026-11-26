using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class CustomerRecordTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Theory]
    [InlineData(LocalRoles.Admin, true)]
    [InlineData(LocalRoles.Manager, true)]
    [InlineData(LocalRoles.Cashier, true)]
    [InlineData(LocalRoles.InventoryStaff, false)]
    [InlineData(LocalRoles.Groomer, false)]
    public async Task Dashboard_link_and_customer_routes_follow_the_same_policy(string role, bool allowed)
    {
        using var client = app.CreateBrowser();
        var employee = await app.CreateEmployeeAsync(role);
        using var login = await LocalAppFactory.SignInAsync(client, employee);
        var token = TestContext.Current.CancellationToken;
        var dashboard = await client.GetStringAsync("/Dashboard", token);
        Assert.Equal(allowed, dashboard.Contains("href=\"/Customers\"", StringComparison.Ordinal));
        using var result = await client.GetAsync("/Customers", token);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Redirect, result.StatusCode);
        if (!allowed)
        {
            var antiforgery = await LocalAppFactory.GetAntiforgeryTokenAsync(client, "/Dashboard");
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["FirstName"] = "Blocked", ["LastName"] = "Owner", ["__RequestVerificationToken"] = antiforgery
            });
            using var blocked = await client.PostAsync("/Customers/New", form, token);
            Assert.Equal("/Account/AccessDenied", blocked.Headers.Location!.AbsolutePath);
        }
    }

    [Fact]
    public async Task Walk_in_with_two_pets_is_searchable_and_corrections_preserve_ownership()
    {
        var token = TestContext.Current.CancellationToken;
        var actor = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, actor);
        using var created = await PostAsync(client, "/Customers/New", new()
        {
            ["FirstName"] = "Test", ["LastName"] = "Owner", ["RecordConsent"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var path = created.Headers.Location!.OriginalString;
        foreach (var name in new[] { "Mochi", "Pepper" })
        {
            using var added = await PostAsync(client, path + "/Pets/New", new()
            {
                ["Name"] = name, ["Species"] = "Dog", ["Size"] = "Small", ["VaccinationStatus"] = "Initial dose recorded"
            });
            Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        }
        var profile = await client.GetStringAsync(path, token);
        Assert.Contains("Mochi", profile);
        Assert.Contains("Pepper", profile);
        var search = await client.GetStringAsync("/Customers?query=Mochi", token);
        Assert.Contains(path, search);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var pet = await db.Set<PetRecord>().AsNoTracking().SingleAsync(x => x.Name == "Mochi", token);
        var editPath = path + "/Pets/" + pet.Id + "/Edit";
        Assert.Equal("Small", pet.Size);
        var editPage = await client.GetStringAsync(editPath, token);
        Assert.Contains("selected=\"selected\">Small</option>", editPage);
        Assert.Contains("selected=\"selected\">Initial dose recorded</option>", editPage);
        var input = new Dictionary<string, string>
        {
            ["Name"] = "Mochi", ["Species"] = "Dog", ["Size"] = "Medium", ["Allergies"] = "Oats",
            ["VaccinationStatus"] = "Fully vaccinated", ["Version"] = pet.Version.ToString()
        };
        using var correction = await PostAsync(client, editPath, input);
        Assert.Equal(HttpStatusCode.Redirect, correction.StatusCode);
        using var stale = await PostAsync(client, editPath, input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Oats", await client.GetStringAsync(path, token));
        Assert.Contains("selected=\"selected\">Fully vaccinated</option>", await client.GetStringAsync(editPath, token));
        Assert.Equal("Medium", await db.Set<PetRecord>().Where(x => x.Id == pet.Id).Select(x => x.Size).SingleAsync(token));

        using var other = await PostAsync(client, "/Customers/New", new() { ["FirstName"] = "Other", ["LastName"] = "Owner" });
        var otherPath = other.Headers.Location!.OriginalString;
        using var crossed = await PostAsync(client, otherPath + "/Pets/" + pet.Id + "/Edit", input, otherPath + "/Pets/New");
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        Assert.Equal(4, await db.Set<ProfileChange>().CountAsync(x => x.CustomerId == pet.CustomerId && x.EmployeeId == actor.Id, token));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path,
        Dictionary<string, string> values, string? tokenPath = null)
    {
        values["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(client, tokenPath ?? path);
        using var form = new FormUrlEncodedContent(values);
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }
}
