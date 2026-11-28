using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class GroomingCatalogTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Theory]
    [InlineData(LocalRoles.Admin, true)]
    [InlineData(LocalRoles.Manager, true)]
    [InlineData(LocalRoles.Cashier, false)]
    [InlineData(LocalRoles.Groomer, false)]
    [InlineData(LocalRoles.InventoryStaff, false)]
    public async Task Catalog_access_and_dashboard_link_follow_the_same_policy(string role, bool allowed)
    {
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, await app.CreateEmployeeAsync(role));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = TestContext.Current.CancellationToken;
        var dashboard = await client.GetStringAsync("/Dashboard", token);
        Assert.Equal(allowed, dashboard.Contains("href=\"/GroomingCatalog\"", StringComparison.Ordinal));
        using var catalog = await client.GetAsync("/GroomingCatalog", token);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Redirect, catalog.StatusCode);
        if (!allowed)
        {
            using var attempt = await SaveAsync(client, "/GroomingCatalog/New", new(), "/Dashboard");
            Assert.Equal(HttpStatusCode.Redirect, attempt.StatusCode);
            Assert.Contains("/Account/AccessDenied", attempt.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task Service_requires_duration_to_activate_and_rejects_stale_edits()
    {
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, await app.CreateEmployeeAsync(LocalRoles.Manager));
        var token = TestContext.Current.CancellationToken;
        const string newPath = "/GroomingCatalog/New";
        var input = new Dictionary<string, string>
        {
            ["Name"] = "Test grooming service", ["IsActive"] = "true", ["Breed"] = "Poodle"
        };
        using var invalid = await SaveAsync(client, newPath, input);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var validation = await invalid.Content.ReadAsStringAsync(token);
        Assert.Contains("Enter a duration", validation);
        Assert.Contains("Specify the species", validation);

        input["DurationMinutes"] = "45";
        input["Species"] = "Dog";
        using var created = await SaveAsync(client, newPath, input);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var service = await db.GroomingServices.AsNoTracking().SingleAsync(token);
        var editPath = $"/GroomingCatalog/{service.Id}/Edit";
        var index = await client.GetStringAsync("/GroomingCatalog", token);
        Assert.Contains($"href=\"{editPath}\"", index);
        Assert.Contains("45 min", index);
        var edit = await client.GetStringAsync(editPath, token);
        Assert.Contains("value=\"Dog\" selected=\"selected\"", edit);
        Assert.Contains("value=\"Poodle\" selected=\"selected\"", edit);
        Assert.Contains("value=\"Giant\"", edit);
        input["Version"] = service.Version.ToString();
        input["IsActive"] = "false";
        input["Species"] = "";
        input["Breed"] = "";
        input["Size"] = "";
        using var edited = await SaveAsync(client, editPath, input);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        using var stale = await SaveAsync(client, editPath, input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var updated = await db.GroomingServices.AsNoTracking().SingleAsync(token);
        Assert.False(updated.IsActive);
        Assert.Null(updated.Species);
        Assert.Null(updated.Breed);
        Assert.Null(updated.Size);
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, string path,
        Dictionary<string, string> values, string? tokenPath = null)
    {
        values["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(client, tokenPath ?? path);
        using var form = new FormUrlEncodedContent(values);
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }
}
