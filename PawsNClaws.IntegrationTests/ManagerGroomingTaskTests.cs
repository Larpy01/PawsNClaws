using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class ManagerGroomingTaskTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Manager_records_three_groomers_work_without_changing_the_assignments()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var admin = await app.CreateEmployeeAsync(LocalRoles.Admin);
        var first = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var second = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var third = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var snapshot = new QuoteSnapshot("Owner", "Pepper", "Dog", null, null, null,
            [new QuoteLine(Guid.NewGuid(), "Full groom", ["Bath", "Dry", "Trim"], 500m)],
            [], 500m, 500m, null, null);
        var visit = new GroomingVisit
        {
            EstimateId = Guid.NewGuid(), EstimateVersion = 1, EstimateSnapshot = JsonSerializer.Serialize(snapshot),
            StartedByEmployeeId = manager.Id, StartedAtUtc = DateTime.UtcNow, RevisionToken = Guid.NewGuid()
        };
        db.AddRange(visit, new GroomingCheckIn
        {
            VisitId = visit.Id, EmployeeId = manager.Id, RecordedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(token);

        using var managerBrowser = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        using var adminBrowser = app.CreateBrowser();
        using var adminLogin = await LocalAppFactory.SignInAsync(adminBrowser, admin);
        using var groomerBrowser = app.CreateBrowser();
        using var groomerLogin = await LocalAppFactory.SignInAsync(groomerBrowser, third);
        var taskPage = $"/GroomingVisits/{visit.Id}/Tasks";
        var progressPath = $"/GroomingVisits/{visit.Id}/TaskProgress";
        var managerToken = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, taskPage);
        using var assignments = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("revision", visit.RevisionToken.ToString()), new("__RequestVerificationToken", managerToken),
            new("groomers", first.Id.ToString()), new("groomers", second.Id.ToString()),
            new("groomers", third.Id.ToString())
        });
        using var assigned = await managerBrowser.PostAsync(taskPage, assignments, token);
        Assert.Equal($"/GroomingVisits/{visit.Id}/Checklist", assigned.Headers.Location?.OriginalString);
        Assert.Contains("Start task", await managerBrowser.GetStringAsync(taskPage, token));

        var adminToken = await LocalAppFactory.GetAntiforgeryTokenAsync(adminBrowser, taskPage);
        var groomerToken = await LocalAppFactory.GetAntiforgeryTokenAsync(groomerBrowser, "/GroomerTasks");
        Task<Guid> RevisionAsync() => db.Set<GroomingVisit>().AsNoTracking().Where(x => x.Id == visit.Id)
            .Select(x => x.RevisionToken).SingleAsync(token);
        async Task<HttpResponseMessage> ProgressAsync(HttpClient browser, int index, bool complete,
            string? antiforgery, Guid? revision = null)
        {
            var fields = new Dictionary<string, string>
            {
                ["index"] = index.ToString(), ["complete"] = complete.ToString(),
                ["revision"] = (revision ?? await RevisionAsync()).ToString(), ["id"] = visit.Id.ToString(),
                ["observations"] = $"  Groomer comment {index}  ", ["aftercare"] = $"  Groomer instruction {index}  "
            };
            if (antiforgery is not null) fields["__RequestVerificationToken"] = antiforgery;
            using var body = new FormUrlEncodedContent(fields);
            return await browser.PostAsync(progressPath, body, token);
        }

        var readyRevision = await RevisionAsync();
        using var withoutToken = await ProgressAsync(managerBrowser, 0, false, null);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        using var unauthorized = await ProgressAsync(groomerBrowser, 0, false, groomerToken);
        Assert.Contains("/Account/AccessDenied", unauthorized.Headers.Location?.OriginalString);
        using var prematureCompletion = await ProgressAsync(managerBrowser, 0, true, managerToken);
        Assert.Equal(readyRevision, await RevisionAsync());
        var checkInId = await db.Set<GroomingCheckIn>().Where(x => x.VisitId == visit.Id).Select(x => x.Id).SingleAsync(token);

        for (var index = 0; index < 3; index++)
        {
            var revision = await RevisionAsync();
            using var started = await ProgressAsync(managerBrowser, index, false, managerToken, revision);
            Assert.Equal(taskPage, started.Headers.Location?.OriginalString);
            Assert.NotEqual(revision, await RevisionAsync());
            Assert.Contains("Mark complete", await managerBrowser.GetStringAsync(taskPage, token));
            using var stale = await ProgressAsync(managerBrowser, index, true, managerToken, revision);
            Assert.Contains("Mark complete", await managerBrowser.GetStringAsync(taskPage, token));

            var finisher = index == 1 ? adminBrowser : managerBrowser;
            using var completed = await ProgressAsync(finisher, index, true, index == 1 ? adminToken : managerToken);
            Assert.Equal(taskPage, completed.Headers.Location?.OriginalString);
            Assert.Contains($"Groomer instruction {index}", await managerBrowser.GetStringAsync(taskPage, token));
            if (index < 2)
                Assert.False(await db.Set<SmsNotification>().AnyAsync(x => x.Kind == SmsNotificationKind.ReadyForPickup &&
                    x.SourceId == checkInId, token));
        }

        var employeeIds = new[] { first.Id, second.Id, third.Id };
        var checklist = await db.Set<GroomingCheckIn>().AsNoTracking().Where(x => x.VisitId == visit.Id)
            .Select(x => x.TaskChecklist).SingleAsync(token);
        Assert.True(await db.Set<SmsNotification>().AnyAsync(x => x.Kind == SmsNotificationKind.ReadyForPickup &&
            x.SourceId == checkInId && x.Status == SmsNotificationStatus.Pending, token));
        var tasks = JsonSerializer.Deserialize<GroomingTask[]>(checklist!)!;
        Assert.Equal(employeeIds, tasks.Select(x => x.Groomer.Id));
        using var document = JsonDocument.Parse(checklist!);
        for (var index = 0; index < tasks.Length; index++)
        {
            Assert.NotNull(tasks[index].Started);
            Assert.NotNull(tasks[index].Completed);
            Assert.Equal($"Groomer comment {index}", tasks[index].Observations);
            Assert.Equal($"Groomer instruction {index}", tasks[index].Aftercare);
            Assert.Equal(manager.Id, document.RootElement[index].GetProperty("StartedByEmployeeId").GetGuid());
            Assert.Equal(index == 1 ? admin.Id : manager.Id,
                document.RootElement[index].GetProperty("CompletedByEmployeeId").GetGuid());
        }
        var groomerTasks = await groomerBrowser.GetStringAsync("/GroomerTasks", token);
        Assert.Contains("Completed", groomerTasks);
        Assert.Contains("Groomer instruction 2", groomerTasks);
        Assert.DoesNotContain("Groomer instruction 0", groomerTasks);
    }

    [Fact]
    public async Task Shared_service_has_one_checklist_row_and_one_progress_record()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var first = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var second = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var third = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var snapshot = new QuoteSnapshot("Owner", "Daisy", "Dog", null, null, null,
            [new QuoteLine(Guid.NewGuid(), "Full groom", ["Brush out", "Nail care"], 500m)], [],
            500m, 500m, null, null);
        var visit = new GroomingVisit
        {
            EstimateId = Guid.NewGuid(), EstimateVersion = 1, EstimateSnapshot = JsonSerializer.Serialize(snapshot),
            StartedByEmployeeId = manager.Id, StartedAtUtc = DateTime.UtcNow, RevisionToken = Guid.NewGuid()
        };
        db.AddRange(visit, new GroomingCheckIn
        {
            VisitId = visit.Id, EmployeeId = manager.Id, RecordedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(token);

        using var managerBrowser = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        using var firstBrowser = app.CreateBrowser();
        using var firstLogin = await LocalAppFactory.SignInAsync(firstBrowser, first);
        using var secondBrowser = app.CreateBrowser();
        using var secondLogin = await LocalAppFactory.SignInAsync(secondBrowser, second);
        using var thirdBrowser = app.CreateBrowser();
        using var thirdLogin = await LocalAppFactory.SignInAsync(thirdBrowser, third);
        var taskPage = $"/GroomingVisits/{visit.Id}/Tasks";
        var checklistPage = $"/GroomingVisits/{visit.Id}/Checklist";
        var managerToken = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, taskPage);
        using var assignments = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("revision", visit.RevisionToken.ToString()), new("__RequestVerificationToken", managerToken),
            new("assignments[0].GroomerIds", first.Id.ToString()),
            new("assignments[0].GroomerIds", second.Id.ToString()),
            new("assignments[1].GroomerIds", third.Id.ToString())
        });
        using var assigned = await managerBrowser.PostAsync(taskPage, assignments, token);
        Assert.Equal(checklistPage, assigned.Headers.Location?.OriginalString);
        var checklist = await managerBrowser.GetStringAsync(checklistPage, token);
        Assert.Equal(1, checklist.Split("Brush out").Length - 1);
        Assert.Equal(1, checklist.Split("Nail care").Length - 1);

        const string groomerPage = "/GroomerTasks";
        var firstPage = await firstBrowser.GetStringAsync(groomerPage, token);
        Assert.Contains("Brush out", firstPage);
        Assert.DoesNotContain("Nail care", firstPage);
        var secondPage = await secondBrowser.GetStringAsync(groomerPage, token);
        Assert.Contains("Brush out", secondPage);
        Assert.DoesNotContain("Nail care", secondPage);
        var thirdPage = await thirdBrowser.GetStringAsync(groomerPage, token);
        Assert.Contains("Nail care", thirdPage);
        Assert.DoesNotContain("Brush out", thirdPage);

        Task<Guid> RevisionAsync() => db.Set<GroomingVisit>().AsNoTracking().Where(x => x.Id == visit.Id)
            .Select(x => x.RevisionToken).SingleAsync(token);
        var progressTokens = new Dictionary<HttpClient, string>();
        foreach (var browser in new[] { firstBrowser, secondBrowser, thirdBrowser })
            progressTokens.Add(browser, await LocalAppFactory.GetAntiforgeryTokenAsync(browser, groomerPage));
        async Task UpdateAsync(HttpClient browser, Guid revision, bool complete, string observations)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = visit.Id.ToString(), ["revision"] = revision.ToString(), ["index"] = "0",
                ["complete"] = complete.ToString(), ["observations"] = observations,
                ["__RequestVerificationToken"] = progressTokens[browser]
            });
            using var response = await browser.PostAsync(groomerPage + "/Update", body, token);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var waitingRevision = await RevisionAsync();
        await UpdateAsync(thirdBrowser, waitingRevision, false, "Unassigned change");
        Assert.Equal(waitingRevision, await RevisionAsync());
        await UpdateAsync(firstBrowser, waitingRevision, false, "");
        var startedRevision = await RevisionAsync();
        Assert.NotEqual(waitingRevision, startedRevision);
        Assert.Contains("Mark complete", await secondBrowser.GetStringAsync(groomerPage, token));
        await UpdateAsync(secondBrowser, startedRevision, true, "Finished together");
        var completedRevision = await RevisionAsync();
        Assert.NotEqual(startedRevision, completedRevision);
        await UpdateAsync(firstBrowser, completedRevision, true, "Duplicate completion");
        Assert.Equal(completedRevision, await RevisionAsync());
        Assert.Contains("Finished together", await firstBrowser.GetStringAsync(groomerPage, token));

        var savedChecklist = await db.Set<GroomingCheckIn>().AsNoTracking().Where(x => x.VisitId == visit.Id)
            .Select(x => x.TaskChecklist).SingleAsync(token);
        var tasks = JsonSerializer.Deserialize<GroomingTask[]>(savedChecklist!)!;
        Assert.Collection(tasks,
            shared =>
            {
                Assert.Equal("Brush out", shared.Service);
                Assert.Equal(new[] { first.Id, second.Id }.Order(), shared.AssignedGroomers.Select(x => x.Id).Order());
                Assert.Equal(first.Id, shared.StartedByEmployeeId);
                Assert.Equal(second.Id, shared.CompletedByEmployeeId);
                Assert.Equal("Finished together", shared.Observations);
            },
            separate =>
            {
                Assert.Equal("Nail care", separate.Service);
                Assert.Equal(third.Id, Assert.Single(separate.AssignedGroomers).Id);
                Assert.Null(separate.Started);
            });
    }
}
