using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class GroomingQuoteUserTests
{
    [Theory]
    [InlineData("No optional fields", null, 500)]
    [InlineData("Blank optional fields", null, 500)]
    [InlineData("Unknown condition uses base price", null, 500)]
    [InlineData("Matching coat condition", null, 650)]
    [InlineData("Existing dog rule with All breed and size", null, 599)]
    [InlineData("Specific price beats All filters", null, 650)]
    [InlineData("One add-on", null, 575)]
    [InlineData("Two add-ons", null, 625)]
    [InlineData("Add-on removed", null, 500)]
    [InlineData("Custom total", null, 400)]
    [InlineData("Zero custom total", null, 0)]
    [InlineData("Reason without custom total", null, 500)]
    [InlineData("Missing pet", "Choose a customer and pet.", 0)]
    [InlineData("Missing main service", "Choose a service or package.", 0)]
    [InlineData("Main service also selected as add-on", "Add-ons must be individual services separate from the main selection.", 0)]
    [InlineData("Inactive add-on", "unavailable or does not apply", 0)]
    [InlineData("Wrong species", "unavailable or does not apply", 0)]
    [InlineData("Missing price", "No approved price rule matches", 0)]
    [InlineData("Tied prices", "More than one price matches", 0)]
    [InlineData("Blank reason", "Enter a reason for changing the total.", 0)]
    [InlineData("Fractional cent", "Enter a custom total with up to two decimal places.", 0)]
    [InlineData("Negative total", "Custom price estimate total", 0)]
    public async Task Quote_form_handles_user_choices(string scenario, string? error, int expectedTotal)
    {
        await using var app = new LocalAppFactory();
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var owner = new CustomerRecord { FirstName = "Test", LastName = "Owner" };
        var pet = new PetRecord { CustomerId = owner.Id, Name = "Mochi", Species = "Dog" };
        var bath = new GroomingService { Name = "Bath", IsActive = true, DurationMinutes = 30 };
        var trim = new GroomingService { Name = "Nail trim", IsActive = scenario != "Inactive add-on", DurationMinutes = 10 };
        var ears = new GroomingService { Name = "Ear cleaning", IsActive = true, DurationMinutes = 10 };
        var catService = new GroomingService { Name = "Cat bath", Species = "Cat", IsActive = true, DurationMinutes = 30 };
        var unpriced = new GroomingService { Name = "Unpriced service", IsActive = true, DurationMinutes = 30 };
        db.AddRange(owner, pet, bath, trim, ears, catService, unpriced,
            new GroomingPriceRule { ServiceId = bath.Id, Amount = 500, IsActive = true },
            new GroomingPriceRule { ServiceId = bath.Id, Amount = 650, Condition = "Matted", IsActive = true },
            new GroomingPriceRule { ServiceId = trim.Id, Amount = 75, IsActive = true },
            new GroomingPriceRule { ServiceId = ears.Id, Amount = 50, IsActive = true });
        if (scenario == "Tied prices")
            db.Add(new GroomingPriceRule { ServiceId = bath.Id, Amount = 550, IsActive = true });
        if (scenario is "Existing dog rule with All breed and size" or "Specific price beats All filters")
            db.Add(new GroomingPriceRule { ServiceId = bath.Id,
                Species = scenario == "Specific price beats All filters" ? " ALL " : "Dog",
                Breed = "All", Size = "all", Condition = "All", Amount = 599, IsActive = true });
        await db.SaveChangesAsync(token);

        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, await app.CreateEmployeeAsync(LocalRoles.Manager));
        const string url = "/GroomingPlanning/Quote";
        var id = Guid.NewGuid();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("RequestId", id.ToString()),
            new("PetId", scenario == "Missing pet" ? "" : pet.Id.ToString()),
            new("TargetId", scenario switch
            {
                "Missing main service" => "",
                "Wrong species" => catService.Id.ToString(),
                "Missing price" => unpriced.Id.ToString(),
                _ => bath.Id.ToString()
            }),
            new("__RequestVerificationToken", await LocalAppFactory.GetAntiforgeryTokenAsync(client, url))
        };
        switch (scenario)
        {
            case "Blank optional fields":
                fields.Add(new("Condition", ""));
                fields.Add(new("OverrideTotal", ""));
                break;
            case "Unknown condition uses base price":
                fields.Add(new("Condition", "Unknown"));
                break;
            case "Matching coat condition":
            case "Specific price beats All filters":
                fields.Add(new("Condition", " matted "));
                break;
            case "One add-on":
            case "Inactive add-on":
                fields.Add(new("AddOnIds", trim.Id.ToString()));
                break;
            case "Two add-ons":
                fields.Add(new("AddOnIds", trim.Id.ToString()));
                fields.Add(new("AddOnIds", ears.Id.ToString()));
                break;
            case "Main service also selected as add-on":
                fields.Add(new("AddOnIds", bath.Id.ToString()));
                break;
            case "Reason without custom total":
                fields.Add(new("OverrideReason", "A reason typed earlier"));
                break;
            case "Custom total":
            case "Zero custom total":
            case "Blank reason":
            case "Fractional cent":
            case "Negative total":
                fields.Add(new("OverrideTotal", scenario switch
                {
                    "Zero custom total" => "0",
                    "Fractional cent" => "400.001",
                    "Negative total" => "-1",
                    _ => "400"
                }));
                fields.Add(new("OverrideReason", scenario == "Blank reason" ? " " : "Approved discount"));
                break;
        }
        using var form = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync(url, form, token);
        var planning = scope.ServiceProvider.GetRequiredService<IGroomingPlanning>();
        var versions = await planning.QuotesAsync(id, token);
        if (error is not null)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(error, await response.Content.ReadAsStringAsync(token));
            Assert.Empty(versions);
            return;
        }

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var quote = Assert.Single(versions).Snapshot;
        Assert.Equal(expectedTotal, quote.Total);
        Assert.Equal(scenario switch { "Two add-ons" => 3, "One add-on" => 2, _ => 1 }, quote.Lines.Count);
        Assert.Equal(scenario is "Custom total" or "Zero custom total" ? "Approved discount" : null, quote.OverrideReason);
        Assert.False(await db.InventoryMovements.AnyAsync(token));
    }
}
