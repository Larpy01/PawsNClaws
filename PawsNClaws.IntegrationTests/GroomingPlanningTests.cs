using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;
using SkiaSharp;

namespace PawsNClaws.IntegrationTests;

public sealed class GroomingPlanningTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Manager_quotes_a_pet_preserves_revisions_and_estimates_without_posting_stock()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var owner = new CustomerRecord { FirstName = "Test", LastName = "Owner" };
        var pet = new PetRecord { CustomerId = owner.Id, Name = "Mochi", Species = "Dog", Size = "Small" };
        var bath = new GroomingService { Name = "Bath & Blow Dry", DurationMinutes = 30, IsActive = true };
        var trim = new GroomingService { Name = "Nail Trim", DurationMinutes = 15, IsActive = true, Species = "Dog" };
        var shampoo = new Product { Name = "Shampoo", SKU = "SHAMPOO", IsConsumable = true, Unit = "mL" };
        var retailProduct = new Product { Name = "Retail leash", SKU = "LEASH" };
        var inactiveSupply = new Product { Name = "Retired solution", SKU = "RETIRED", IsConsumable = true, IsActive = false };
        db.AddRange(owner, pet, bath, trim, shampoo, retailProduct, inactiveSupply);
        await db.SaveChangesAsync(token);

        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, await app.CreateEmployeeAsync(LocalRoles.Manager));
        var customerPage = await client.GetStringAsync($"/Customers/{owner.Id}", token);
        Assert.Contains($"/GroomingPlanning/PetQuotes/{pet.Id}", customerPage);
        using var package = await PostAsync(client, "Package", new()
        {
            ["Name"] = "Basic Grooming", ["ServiceIds[0]"] = bath.Id.ToString(),
            ["ServiceIds[1]"] = trim.Id.ToString(), ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, package.StatusCode);
        var bundle = await db.Set<GroomingPackage>().AsNoTracking().SingleAsync(token);

        foreach (var (condition, price) in new[] { ("", "500"), ("Matted", "650") })
        {
            using var rule = await PostAsync(client, "Price", new()
            {
                ["TargetId"] = bundle.Id.ToString(), ["Condition"] = condition, ["Amount"] = price, ["IsActive"] = "true"
            });
            Assert.Equal(HttpStatusCode.Redirect, rule.StatusCode);
        }
        using var recipe = await PostAsync(client, "Recipe", new()
        {
            ["ServiceId"] = bath.Id.ToString(), ["ProductId"] = shampoo.Id.ToString(),
            ["Quantity"] = "35.5", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, recipe.StatusCode);
        var workspace = await client.GetStringAsync("/GroomingPlanning/Index", token);
        Assert.Contains("Basic Grooming", workspace);
        Assert.Contains("Shampoo", workspace);

        var quoteId = Guid.NewGuid();
        var formHtml = await client.GetStringAsync("/GroomingPlanning/Quote", token);
        Assert.Contains("Grooming price estimate", formHtml);
        Assert.Contains("Save price estimate", formHtml);
        var addOnField = System.Text.RegularExpressions.Regex.Match(formHtml, "<input[^>]+name=\"AddOnIds\"[^>]*>").Value;
        Assert.NotEmpty(addOnField);
        Assert.DoesNotContain("required", addOnField);
        Assert.Matches("<input[^>]+disabled[^>]+id=\"OverrideReason\"", formHtml);
        var input = new Dictionary<string, string>
        {
            ["RequestId"] = quoteId.ToString(), ["PetId"] = pet.Id.ToString(),
            ["TargetId"] = bundle.Id.ToString(), ["Condition"] = "matted", ["OverrideTotal"] = "610"
        };
        using var missingReason = await PostAsync(client, "Quote", input);
        Assert.Equal(HttpStatusCode.OK, missingReason.StatusCode);
        Assert.Contains("Enter a reason for changing the total.", await missingReason.Content.ReadAsStringAsync(token));

        input["PetId"] = "";
        input["OverrideTotal"] = "";
        using var noPet = await PostAsync(client, "Quote", input);
        Assert.Equal(HttpStatusCode.OK, noPet.StatusCode);
        var noPetHtml = await noPet.Content.ReadAsStringAsync(token);
        Assert.Contains("Choose a customer and pet.", noPetHtml);
        Assert.DoesNotContain("is invalid.", noPetHtml);

        input["PetId"] = pet.Id.ToString();
        input["OverrideTotal"] = "610";
        input["OverrideReason"] = "Approved adjustment";
        using var quote = await PostAsync(client, "Quote", input);
        Assert.Equal(HttpStatusCode.Redirect, quote.StatusCode);
        Assert.Equal($"/GroomingPlanning/History/{quoteId}", quote.Headers.Location!.OriginalString);
        var quoteList = await client.GetStringAsync("/GroomingPlanning/Index", token);
        Assert.Contains("Version 1", quoteList);
        Assert.Contains("Create price estimate", quoteList);
        Assert.DoesNotContain("v@quote.Number", quoteList);
        using var duplicate = await PostAsync(client, "Quote", input);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var ruleToEdit = await db.Set<GroomingPriceRule>().AsNoTracking().SingleAsync(x => x.Condition == "Matted", token);
        using var edited = await PostAsync(client, $"Price/{ruleToEdit.Id}", new()
        {
            ["Version"] = ruleToEdit.Version.ToString(), ["TargetId"] = bundle.Id.ToString(),
            ["Condition"] = "Matted", ["Amount"] = "700", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        input["Revision"] = "1";
        input["OverrideTotal"] = "";
        input.Remove("OverrideReason");
        using var revision = await PostAsync(client, $"Quote/{quoteId}", input);
        Assert.Equal(HttpStatusCode.Redirect, revision.StatusCode);
        using var stale = await PostAsync(client, $"Quote/{quoteId}", input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var planning = scope.ServiceProvider.GetRequiredService<IGroomingPlanning>();
        var history = await planning.QuotesAsync(quoteId, token);
        Assert.Collection(history,
            latest => { Assert.Equal(2, latest.Number); Assert.Equal(700m, latest.Snapshot.Total); },
            original =>
            {
                Assert.Equal(650m, original.Snapshot.BaseTotal);
                Assert.Equal(610m, original.Snapshot.Total);
                Assert.Equal("Approved adjustment", original.Snapshot.OverrideReason);
                Assert.Equal(new[] { "Bath & Blow Dry", "Nail Trim" }, Assert.Single(original.Snapshot.Lines).Services);
                Assert.Equal(35.5m, Assert.Single(original.Snapshot.Supplies).Quantity);
            });
        Assert.Empty(await db.InventoryMovements.ToListAsync(token));
        Assert.Contains(quoteId.ToString(), await client.GetStringAsync($"/GroomingPlanning/PetQuotes/{pet.Id}", token));
        Assert.Contains("Approved adjustment", await client.GetStringAsync($"/GroomingPlanning/History/{quoteId}", token));

        var historyPath = $"/GroomingPlanning/History/{quoteId}";
        var historyPage = await client.GetStringAsync(historyPath, token);
        Assert.Contains("Start grooming visit from Version 1", historyPage);
        Assert.Contains(ExpectedPhilippineTime(history[0].CreatedAtUtc), historyPage);
        var antiforgery = await LocalAppFactory.GetAntiforgeryTokenAsync(client, historyPath);
        using var invalidVersion = await StartVisitAsync(client, quoteId, 99, antiforgery);
        Assert.Equal(HttpStatusCode.NotFound, invalidVersion.StatusCode);
        using var noToken = await StartVisitAsync(client, quoteId, 1, null);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var started = await StartVisitAsync(client, quoteId, 1, antiforgery);
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var visitPath = started.Headers.Location!.OriginalString;
        var visitPage = await client.GetStringAsync(visitPath, token);
        Assert.Contains("Grooming visit for Mochi", visitPage);
        Assert.Contains("Price estimate version 1", visitPage);
        Assert.Contains("Nail Trim", visitPage);
        Assert.Contains("Total ₱610.00", visitPage);
        Assert.Contains("Version 2 · ₱700.00", visitPage);
        using var repeated = await StartVisitAsync(client, quoteId, 2, antiforgery);
        Assert.Equal(started.Headers.Location, repeated.Headers.Location);
        var savedVisit = await db.Set<GroomingVisit>().SingleAsync(token);
        Assert.Contains(ExpectedPhilippineTime(savedVisit.StartedAtUtc), visitPage);
        var updatedHistory = await client.GetStringAsync(historyPath, token);
        Assert.Contains("Open grooming visit", updatedHistory);
        Assert.DoesNotContain("Start grooming visit from Version", updatedHistory);
        Assert.False(await db.InventoryMovements.AnyAsync(token));

        var checkInPath = visitPath + "/CheckIn";
        var checkInPage = await client.GetStringAsync(checkInPath, token);
        Assert.Contains("Not assessed", checkInPage);
        Assert.Contains("Signed paper waiver", checkInPage);
        Assert.DoesNotContain("waiverPhoto", checkInPage);
        Assert.DoesNotContain("Form.WaiverVersion", checkInPage);
        Assert.Contains("Shampoo", checkInPage);
        Assert.DoesNotContain("Retail leash", checkInPage);
        Assert.DoesNotContain("Retired solution", checkInPage);
        var checkInToken = await LocalAppFactory.GetAntiforgeryTokenAsync(client, checkInPath);
        using var missingWaiver = await CheckInAsync(client, checkInPath, checkInToken, savedVisit.RevisionToken, null);
        Assert.Contains("Attach the signed paper waiver.", await missingWaiver.Content.ReadAsStringAsync(token));
        using var invalidWaiver = await CheckInAsync(client, checkInPath, checkInToken, savedVisit.RevisionToken, "plain text"u8.ToArray());
        Assert.Contains("Choose a PDF, JPEG, or PNG", await invalidWaiver.Content.ReadAsStringAsync(token));
        Assert.False(await db.Set<GroomingCheckIn>().AnyAsync(token));

        using var bitmap = new SKBitmap(2, 2);
        using var waiverImage = SKImage.FromBitmap(bitmap);
        using var encodedWaiver = waiverImage.Encode(SKEncodedImageFormat.Png, 100);
        var waiverBytes = encodedWaiver.ToArray();
        Assert.DoesNotContain("value=\"Version 3\"", checkInPage);
        input["Revision"] = "2";
        using var third = await PostAsync(client, $"Quote/{quoteId}", input);
        Assert.Equal(HttpStatusCode.Redirect, third.StatusCode);
        using var unavailableSupply = await CheckInAsync(client, checkInPath, checkInToken,
            savedVisit.RevisionToken, waiverBytes, retailProduct.Id);
        Assert.Contains("A selected supply is no longer available", await unavailableSupply.Content.ReadAsStringAsync(token));
        async Task<HttpResponseMessage> QueuePost(string path, Dictionary<string, string> values)
        {
            values["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(client, $"/Customers/{owner.Id}");
            using var body = new FormUrlEncodedContent(values);
            return await client.PostAsync("/WalkInQueue/" + path, body, token);
        }
        using var queued = await QueuePost("Add", new() { ["petId"] = pet.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, queued.StatusCode);
        using var duplicateQueue = await QueuePost("Add", new() { ["petId"] = pet.Id.ToString() });
        Assert.Contains("already on the walk-in queue", await client.GetStringAsync("/WalkInQueue", token));
        using var waitingCheckIn = await CheckInAsync(client, checkInPath, checkInToken, savedVisit.RevisionToken, waiverBytes);
        Assert.Contains("Call this pet from the walk-in queue", await waitingCheckIn.Content.ReadAsStringAsync(token));
        var entry = await db.Set<WalkInEntry>().AsNoTracking().SingleAsync(token);
        using var called = await QueuePost(entry.Id + "/Call", new() { ["revision"] = entry.Revision.ToString() });
        Assert.Contains("Open price estimates for check-in", await client.GetStringAsync("/WalkInQueue", token));
        using var savedCheckIn = await CheckInAsync(client, checkInPath, checkInToken, savedVisit.RevisionToken,
            waiverBytes, shampoo.Id, waiverVersion: "Version 3");
        Assert.Equal(HttpStatusCode.Redirect, savedCheckIn.StatusCode);
        using var repeatedCheckIn = await CheckInAsync(client, checkInPath, checkInToken, savedVisit.RevisionToken, waiverBytes);
        Assert.Equal(HttpStatusCode.Redirect, repeatedCheckIn.StatusCode);
        var closedEntry = await db.Set<WalkInEntry>().AsNoTracking().SingleAsync(token);
        Assert.Equal(WalkInStatus.CheckedIn, closedEntry.Status);
        Assert.Equal(savedVisit.Id, closedEntry.VisitId);
        var recorded = await db.Set<GroomingCheckIn>().SingleAsync(token);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(waiverBytes)), recorded.WaiverSha256);
        var jobOrder = await client.GetStringAsync(visitPath + "/JobOrder", token);
        Assert.Contains("Price estimate version 1", jobOrder);
        Assert.Contains("610.00", jobOrder);
        Assert.Contains("Please use the gentle brush.", jobOrder);
        Assert.Contains("Nail Trim", jobOrder);
        var checkedInPage = await client.GetStringAsync(visitPath, token);
        Assert.Contains("Waiver version: 1", checkedInPage);
        Assert.Contains("Needs attention", checkedInPage);
        Assert.Contains("Please use the gentle brush.", checkedInPage);
        Assert.Contains("<li>Shampoo</li>", checkedInPage);
        Assert.Contains("11/27/2026", checkedInPage);
        using var downloaded = await client.GetAsync(visitPath + "/Waiver", token);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(waiverBytes, await downloaded.Content.ReadAsByteArrayAsync(token));

        Assert.Contains("Cancel check-in and change version", checkedInPage);
        var firstGroomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var secondGroomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var inactiveGroomer = await app.CreateEmployeeAsync(LocalRoles.Groomer, false);
        var taskPath = visitPath + "/Tasks";
        var checklistPath = visitPath + "/Checklist";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(checklistPath, token)).StatusCode);
        var taskPage = await client.GetStringAsync(taskPath, token);
        Assert.Contains(firstGroomer.Id.ToString(), taskPage);
        Assert.DoesNotContain(inactiveGroomer.Id.ToString(), taskPage);
        var taskToken = await LocalAppFactory.GetAntiforgeryTokenAsync(client, taskPath);
        var taskRevision = (await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token)).RevisionToken;
        using var assignments = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("revision", taskRevision.ToString()), new("__RequestVerificationToken", taskToken),
            new("groomers", firstGroomer.Id.ToString()), new("groomers", secondGroomer.Id.ToString())
        });
        using var assigned = await client.PostAsync(taskPath, assignments, token);
        Assert.Equal(checklistPath, assigned.Headers.Location?.OriginalString);
        var printPage = await client.GetStringAsync(checklistPath, token);
        Assert.Contains("Print checklist", printPage);
        Assert.Contains("Nail Trim", printPage);
        Assert.Equal(2, printPage.Split("<section>").Length - 1);
        Assert.Contains("Print saved checklist", await client.GetStringAsync(taskPath, token));
        using var groomerClient = app.CreateBrowser();
        using var groomerLogin = await LocalAppFactory.SignInAsync(groomerClient, firstGroomer);
        const string myTasks = "/GroomerTasks";
        var myPage = await groomerClient.GetStringAsync(myTasks, token);
        Assert.Contains("Start task", myPage);
        Assert.DoesNotContain("Nail Trim", myPage);
        var groomerToken = await LocalAppFactory.GetAntiforgeryTokenAsync(groomerClient, myTasks);
        async Task UpdateTask(int index, bool complete)
        {
            var visit = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token);
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = visit.Id.ToString(), ["revision"] = visit.RevisionToken.ToString(),
                ["index"] = index.ToString(), ["complete"] = complete.ToString(),
                ["observations"] = "Coat brushed", ["aftercare"] = "Brush gently",
                ["__RequestVerificationToken"] = groomerToken
            });
            using var response = await groomerClient.PostAsync(myTasks + "/Update", body, token);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        await UpdateTask(1, false);
        Assert.Contains("could not be updated", await groomerClient.GetStringAsync(myTasks, token));
        await UpdateTask(0, false);
        Assert.Contains("Mark complete", await groomerClient.GetStringAsync(myTasks, token));
        await UpdateTask(0, true);
        Assert.Contains("Completed", await groomerClient.GetStringAsync(myTasks, token));
        var managerTasks = await client.GetStringAsync(taskPath, token);
        Assert.Contains("Coat brushed", managerTasks);
        Assert.Contains("Brush gently", managerTasks);
        var checkedInVisit = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token);
        var changeToken = await LocalAppFactory.GetAntiforgeryTokenAsync(client, visitPath);
        async Task<HttpResponseMessage> ChangeVersionAsync(Guid revision, string reason)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["revision"] = revision.ToString(), ["version"] = "2", ["reason"] = reason,
                ["__RequestVerificationToken"] = changeToken
            });
            return await client.PostAsync(visitPath + "/ChangeEstimate", form, token);
        }

        using var withoutReason = await ChangeVersionAsync(checkedInVisit.RevisionToken, "");
        Assert.Contains("Enter a reason for cancelling check-in", await client.GetStringAsync(visitPath, token));
        using var staleChange = await ChangeVersionAsync(savedVisit.RevisionToken, "Use updated price");
        Assert.Equal(HttpStatusCode.Conflict, staleChange.StatusCode);
        using var changed = await ChangeVersionAsync(checkedInVisit.RevisionToken, "Use updated price");
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        var resetVisit = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token);
        Assert.Equal(2, resetVisit.EstimateVersion);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(checklistPath, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(visitPath + "/JobOrder", token)).StatusCode);
        var resetPage = await client.GetStringAsync(visitPath, token);
        Assert.Contains("Waiting for check-in", resetPage);
        Assert.Contains("Use updated price", resetPage);
        Assert.Contains("<li>Shampoo</li>", resetPage);
        Assert.Contains("Total ₱700.00", resetPage);
        using var originalWaiver = await client.GetAsync(visitPath + "/Waiver?checkInId=" + recorded.Id, token);
        Assert.Equal(waiverBytes, await originalWaiver.Content.ReadAsByteArrayAsync(token));
        using var oldCheckIn = await CheckInAsync(client, checkInPath, checkInToken, savedVisit.RevisionToken, waiverBytes);
        Assert.Equal(HttpStatusCode.Conflict, oldCheckIn.StatusCode);
        using var newCheckIn = await CheckInAsync(client, checkInPath, checkInToken, resetVisit.RevisionToken, waiverBytes);
        Assert.Equal(HttpStatusCode.Redirect, newCheckIn.StatusCode);
        Assert.Equal(2, await db.Set<GroomingCheckIn>().CountAsync(token));
        Assert.Equal(1, await db.Set<GroomingCheckIn>().CountAsync(x => x.CancelledAtUtc == null, token));

        var updatedVisitPage = await client.GetStringAsync(visitPath, token);
        Assert.Contains("Waiver version: 2", updatedVisitPage);
        Assert.Contains("Waiver version: 1", updatedVisitPage);
        var pickupRevision = (await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token)).RevisionToken;
        async Task PickupAsync(Guid revision, string recipient)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["revision"] = revision.ToString(), ["collectedBy"] = recipient, ["__RequestVerificationToken"] = changeToken
            });
            using var response = await client.PostAsync(visitPath + "/Pickup", body, token);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        await PickupAsync(pickupRevision, "Early pickup");
        Assert.Contains("Pickup could not be saved", await client.GetStringAsync(visitPath, token));
        using var finalAssignments = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("revision", pickupRevision.ToString()), new("__RequestVerificationToken", taskToken),
            new("groomers", firstGroomer.Id.ToString()), new("groomers", firstGroomer.Id.ToString())
        });
        using var finalAssigned = await client.PostAsync(taskPath, finalAssignments, token);
        Assert.Equal(checklistPath, finalAssigned.Headers.Location?.OriginalString);
        await UpdateTask(0, false);
        await UpdateTask(0, true);
        await UpdateTask(1, false);
        await UpdateTask(1, true);
        var readyPage = await client.GetStringAsync(visitPath, token);
        Assert.Contains("Record pickup", readyPage);
        Assert.Contains("Brush gently", readyPage);
        var ready = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token);
        await PickupAsync(ready.RevisionToken, "Jamie");
        await PickupAsync(ready.RevisionToken, "Duplicate");
        var completed = await client.GetStringAsync(visitPath, token);
        Assert.Contains("Picked up by Jamie", completed);
        Assert.DoesNotContain("Record pickup</button>", completed);
        var released = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token);
        Assert.NotNull(released.PickupRecordedByEmployeeId);
        Assert.Contains(ExpectedPhilippineTime(released.PickedUpAtUtc!.Value), completed);
        using var cashier = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashier, await app.CreateEmployeeAsync(LocalRoles.Cashier));
        using var blockedOrder = await cashier.GetAsync(visitPath + "/JobOrder", token);
        Assert.Contains("/Account/AccessDenied", blockedOrder.Headers.Location?.OriginalString);
        using var blockedWaiver = await cashier.GetAsync(visitPath + "/Waiver", token);
        Assert.Equal(HttpStatusCode.Redirect, blockedWaiver.StatusCode);
        Assert.Contains("/Account/AccessDenied", blockedWaiver.Headers.Location!.OriginalString);

        using var deactivated = await PostAsync(client, $"Package/{bundle.Id}", new()
        {
            ["Name"] = bundle.Name, ["Version"] = bundle.Version.ToString(),
            ["ServiceIds[0]"] = bath.Id.ToString(), ["ServiceIds[1]"] = trim.Id.ToString()
        });
        Assert.Equal(HttpStatusCode.Redirect, deactivated.StatusCode);
        input["Revision"] = "3";
        using var unavailable = await PostAsync(client, $"Quote/{quoteId}", input);
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        Assert.Contains("Choose an available service or package", await unavailable.Content.ReadAsStringAsync(token));
    }

    [Fact]
    public async Task Cashier_cannot_access_planning()
    {
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, await app.CreateEmployeeAsync(LocalRoles.Cashier));
        using var response = await client.GetAsync("/GroomingPlanning/Index", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
        var token = await LocalAppFactory.GetAntiforgeryTokenAsync(client, "/Dashboard");
        using var start = await StartVisitAsync(client, Guid.NewGuid(), 1, token);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Contains("/Account/AccessDenied", start.Headers.Location!.OriginalString);
    }

    private static string ExpectedPhilippineTime(DateTime utc) =>
        utc.AddHours(8).ToString("MM/dd/yyyy hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> CheckInAsync(HttpClient client, string path,
        string antiforgery, Guid revision, byte[]? waiver, Guid? productId = null, string? waiverVersion = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(antiforgery), "__RequestVerificationToken" },
            { new StringContent(revision.ToString()), "Form.VisitRevision" },
            { new StringContent("2"), "Form.Coat" },
            { new StringContent("Please use the gentle brush."), "Form.OwnerInstructions" },
            { new StringContent("Test Owner"), "Form.Signer" },
            { new StringContent("2026-11-27"), "Form.SignedOn" }
        };
        if (waiverVersion is not null) form.Add(new StringContent(waiverVersion), "Form.WaiverVersion");
        if (waiver is not null)
            form.Add(new ByteArrayContent(waiver), "waiver", "signed.png");
        if (productId.HasValue)
            form.Add(new StringContent(productId.Value.ToString()), "productIds");
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> StartVisitAsync(HttpClient client, Guid estimateId,
        int version, string? antiforgery)
    {
        var fields = new Dictionary<string, string>
        {
            ["estimateId"] = estimateId.ToString(),
            ["version"] = version.ToString()
        };
        if (antiforgery is not null)
            fields["__RequestVerificationToken"] = antiforgery;
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync("/GroomingVisits/Start", form, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        path = "/GroomingPlanning/" + path;
        fields["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(client, path);
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }
}
