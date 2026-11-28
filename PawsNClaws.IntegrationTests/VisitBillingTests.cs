using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class VisitBillingTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Cashier_issues_one_bill_and_records_partial_cash_and_change_without_double_posting()
    {
        var token = TestContext.Current.CancellationToken;
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var groomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var checkedInVisit = NewVisit(cashier.Id, "Pepper");
        var uncheckedVisit = NewVisit(cashier.Id, "Oreo");
        db.AddRange(checkedInVisit, uncheckedVisit, new GroomingCheckIn
        {
            VisitId = checkedInVisit.Id, EmployeeId = cashier.Id, RecordedAtUtc = DateTime.UtcNow,
            Assessment = JsonSerializer.Serialize(new GroomingCheckInForm())
        });
        await db.SaveChangesAsync(token);

        using var cashierBrowser = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashierBrowser, cashier);
        using var groomerBrowser = app.CreateBrowser();
        using var groomerLogin = await LocalAppFactory.SignInAsync(groomerBrowser, groomer);
        const string board = "/VisitBilling";
        var details = $"{board}/{checkedInVisit.Id}";
        var boardPage = await cashierBrowser.GetStringAsync(board, token);
        Assert.DoesNotContain("Pepper", boardPage);
        using var missingBill = await cashierBrowser.GetAsync(details, token);
        Assert.Equal(HttpStatusCode.NotFound, missingBill.StatusCode);

        using var deniedPage = await groomerBrowser.GetAsync(board, token);
        Assert.Contains("/Account/AccessDenied", deniedPage.Headers.Location?.OriginalString);
        var cashierToken = await LocalAppFactory.GetAntiforgeryTokenAsync(cashierBrowser, "/Dashboard");
        var groomerToken = await LocalAppFactory.GetAntiforgeryTokenAsync(groomerBrowser, "/Dashboard");
        using var noToken = await IssueAsync(cashierBrowser, checkedInVisit.Id, null, token);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var deniedIssue = await IssueAsync(groomerBrowser, checkedInVisit.Id, groomerToken, token);
        Assert.Contains("/Account/AccessDenied", deniedIssue.Headers.Location?.OriginalString);

        using var deniedUnchecked = await IssueAsync(cashierBrowser, uncheckedVisit.Id, cashierToken, token);
        Assert.False(await db.Set<GroomingBill>().AnyAsync(x => x.VisitId == uncheckedVisit.Id, token));
        Assert.False(await db.Set<GroomingBill>().AnyAsync(x => x.VisitId == checkedInVisit.Id, token));

        using var issue = await IssueAsync(cashierBrowser, checkedInVisit.Id, cashierToken, token);
        Assert.Equal(HttpStatusCode.Redirect, issue.StatusCode);
        Assert.Equal(details, issue.Headers.Location?.OriginalString);
        using var duplicateIssue = await IssueAsync(cashierBrowser, checkedInVisit.Id, cashierToken, token);
        Assert.Contains("Pepper", await cashierBrowser.GetStringAsync(board, token));
        var issuedBill = await ReadBillAsync();
        Assert.Equal(2, issuedBill.EstimateVersion);
        Assert.Equal(650m, issuedBill.Total);
        Assert.Equal(1, await db.Set<GroomingBill>().CountAsync(x => x.VisitId == checkedInVisit.Id, token));

        using var deniedCash = await PostCashAsync(groomerBrowser, details, issuedBill.Revision, Guid.NewGuid(),
            "200", "500", groomerToken, token);
        Assert.Contains("/Account/AccessDenied", deniedCash.Headers.Location?.OriginalString);
        using var noCashToken = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, Guid.NewGuid(),
            "200", "500", null, token);
        Assert.Equal(HttpStatusCode.BadRequest, noCashToken.StatusCode);

        using var excessivePayment = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, Guid.NewGuid(),
            "651", "1000", cashierToken, token);
        Assert.Equal(HttpStatusCode.OK, excessivePayment.StatusCode);
        using var insufficientTender = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, Guid.NewGuid(),
            "200", "100", cashierToken, token);
        Assert.Equal(HttpStatusCode.OK, insufficientTender.StatusCode);
        using var invalidPrecision = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, Guid.NewGuid(),
            "200.001", "500", cashierToken, token);
        Assert.Equal(HttpStatusCode.OK, invalidPrecision.StatusCode);
        Assert.Empty(await db.Set<GroomingCashPayment>().Where(x => x.VisitId == checkedInVisit.Id).ToListAsync(token));

        var firstOperation = Guid.NewGuid();
        using var firstCash = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, firstOperation,
            "200", "500", cashierToken, token);
        Assert.Equal(HttpStatusCode.Redirect, firstCash.StatusCode);
        Assert.Equal(details, firstCash.Headers.Location?.OriginalString);
        var firstPayment = await db.Set<GroomingCashPayment>().AsNoTracking().SingleAsync(x => x.VisitId == checkedInVisit.Id, token);
        Assert.Equal(firstOperation, firstPayment.Id);
        Assert.Equal(200m, firstPayment.Amount);
        Assert.Equal(500m, firstPayment.Tendered);
        Assert.Equal(cashier.Id, firstPayment.RecordedByEmployeeId);
        var afterFirst = await ReadBillAsync();
        Assert.NotEqual(issuedBill.Revision, afterFirst.Revision);
        var partialPage = await cashierBrowser.GetStringAsync(details, token);
        Assert.Contains("450.00", partialPage);
        Assert.Contains("300.00", partialPage);
        var sameOperation = Guid.NewGuid();
        using var stale = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, sameOperation,
            "450", "500", cashierToken, token);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        using var replay = await PostCashAsync(cashierBrowser, details, issuedBill.Revision, firstOperation,
            "200", "500", cashierToken, token);
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        using var replayWithDifferentPayment = await PostCashAsync(cashierBrowser, details, afterFirst.Revision,
            firstOperation, "210", "500", cashierToken, token);
        Assert.Equal(HttpStatusCode.OK, replayWithDifferentPayment.StatusCode);
        Assert.Equal(1, await db.Set<GroomingCashPayment>().CountAsync(x => x.VisitId == checkedInVisit.Id, token));

        using var finalCash = await PostCashAsync(cashierBrowser, details, afterFirst.Revision, sameOperation,
            "450", "500", cashierToken, token);
        Assert.Equal(HttpStatusCode.Redirect, finalCash.StatusCode);
        var completed = await ReadBillAsync();
        using var afterSettlement = await PostCashAsync(cashierBrowser, details, completed.Revision, Guid.NewGuid(),
            "1", "1", cashierToken, token);
        Assert.Equal(HttpStatusCode.OK, afterSettlement.StatusCode);
        var payments = await db.Set<GroomingCashPayment>().AsNoTracking().Where(x => x.VisitId == checkedInVisit.Id).ToListAsync(token);
        Assert.Equal(2, payments.Count);
        Assert.Equal(650m, payments.Sum(x => x.Amount));

        var printPath = $"{details}/Cash/{firstOperation}/Print";
        Assert.Contains(printPath, await cashierBrowser.GetStringAsync(details, token));
        using var print = await cashierBrowser.GetAsync(printPath, token);
        Assert.Equal(HttpStatusCode.OK, print.StatusCode);
        Assert.True(print.Headers.CacheControl?.NoStore);
        var printed = await print.Content.ReadAsStringAsync(token);
        Assert.Contains("Pepper", printed);
        Assert.Contains("Sample owner", printed);
        Assert.Contains(firstOperation.ToString(), printed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("200.00", printed);
        Assert.Contains("500.00", printed);
        Assert.Contains("300.00", printed);
        Assert.DoesNotContain(sameOperation.ToString(), printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("450.00", printed);

        using var wrongVisit = await cashierBrowser.GetAsync($"{board}/{uncheckedVisit.Id}/Cash/{firstOperation}/Print", token);
        Assert.Equal(HttpStatusCode.NotFound, wrongVisit.StatusCode);
        using var wrongPayment = await cashierBrowser.GetAsync($"{details}/Cash/{Guid.NewGuid()}/Print", token);
        Assert.Equal(HttpStatusCode.NotFound, wrongPayment.StatusCode);
        using var deniedPrint = await groomerBrowser.GetAsync(printPath, token);
        Assert.Contains("/Account/AccessDenied", deniedPrint.Headers.Location?.OriginalString);
        using var anonymousBrowser = app.CreateBrowser();
        using var anonymousPrint = await anonymousBrowser.GetAsync(printPath, token);
        Assert.Contains("/Account/Login", anonymousPrint.Headers.Location?.OriginalString);
        Assert.Equal(2, await db.Set<GroomingCashPayment>().CountAsync(x => x.VisitId == checkedInVisit.Id, token));

        Task<GroomingBill> ReadBillAsync() =>
            db.Set<GroomingBill>().AsNoTracking().SingleAsync(x => x.VisitId == checkedInVisit.Id, token);
    }

    [Fact]
    public async Task Verified_home_deposits_and_issued_bills_cannot_create_competing_balances()
    {
        var token = TestContext.Current.CancellationToken;
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var deposited = NewVisit(manager.Id, "Luna");
        var billable = NewVisit(manager.Id, "Max");
        db.AddRange(deposited, billable, new GroomingCheckIn
        {
            VisitId = deposited.Id, EmployeeId = manager.Id, RecordedAtUtc = DateTime.UtcNow
        }, new GroomingCheckIn
        {
            VisitId = billable.Id, EmployeeId = manager.Id, RecordedAtUtc = DateTime.UtcNow
        }, new OffsiteBooking
        {
            EstimateId = deposited.EstimateId, EstimateVersion = 2, Kind = OffsiteKind.HomeService,
            Status = OffsiteStatus.Cancelled, VerifiedDeposit = 325m, DepositReference = "VERIFIED-325"
        });
        await db.SaveChangesAsync(token);

        using var browser = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(browser, manager);
        var csrf = await LocalAppFactory.GetAntiforgeryTokenAsync(browser, "/Dashboard");
        using var blockedIssue = await IssueAsync(browser, deposited.Id, csrf, token);
        Assert.Equal("/VisitBilling", blockedIssue.Headers.Location?.OriginalString);
        await browser.GetStringAsync("/VisitBilling", token);
        Assert.False(await db.Set<GroomingBill>().AnyAsync(x => x.VisitId == deposited.Id, token));

        using var issued = await IssueAsync(browser, billable.Id, csrf, token);
        Assert.Equal($"/VisitBilling/{billable.Id}", issued.Headers.Location?.OriginalString);
        var currentRevision = await db.Set<GroomingVisit>().AsNoTracking().Where(x => x.Id == billable.Id)
            .Select(x => x.RevisionToken).SingleAsync(token);
        using var changeForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["revision"] = currentRevision.ToString(), ["version"] = "1",
            ["reason"] = "Changed scope", ["__RequestVerificationToken"] = csrf
        });
        using var change = await browser.PostAsync($"/GroomingVisits/{billable.Id}/ChangeEstimate", changeForm, token);
        Assert.Equal($"/GroomingVisits/{billable.Id}", change.Headers.Location?.OriginalString);
        var visit = await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(x => x.Id == billable.Id, token);
        Assert.Equal(2, visit.EstimateVersion);
        Assert.Equal(currentRevision, visit.RevisionToken);
    }

    [Fact]
    public async Task Confirmed_home_deposit_is_applied_once_and_cash_cannot_exceed_remaining_balance()
    {
        var token = TestContext.Current.CancellationToken;
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var visit = NewVisit(manager.Id, "June");
        var deposit = new OffsiteBooking
        {
            EstimateId = visit.EstimateId, EstimateVersion = visit.EstimateVersion, Kind = OffsiteKind.HomeService,
            Status = OffsiteStatus.Confirmed, VerifiedDeposit = 325m, DepositReference = "HOME-VERIFIED-325",
            ConfirmedAtUtc = DateTime.UtcNow, ConfirmedBy = manager.Id
        };
        db.AddRange(visit, deposit, new GroomingCheckIn
        {
            VisitId = visit.Id, EmployeeId = manager.Id, RecordedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(token);

        using var browser = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(browser, cashier);
        var csrf = await LocalAppFactory.GetAntiforgeryTokenAsync(browser, "/Dashboard");
        var details = $"/VisitBilling/{visit.Id}";
        using var issued = await IssueAsync(browser, visit.Id, csrf, token);
        Assert.Equal(details, issued.Headers.Location?.OriginalString);
        using var issuedAgain = await IssueAsync(browser, visit.Id, csrf, token);
        Assert.Equal(details, issuedAgain.Headers.Location?.OriginalString);
        var bill = await db.Set<GroomingBill>().AsNoTracking().SingleAsync(x => x.VisitId == visit.Id, token);
        Assert.Equal(650m, bill.Total);
        Assert.Equal(325m, bill.HomeDepositAmount);
        Assert.Equal("HOME-VERIFIED-325", bill.HomeDepositReference);
        Assert.Equal(deposit.Id, bill.HomeDepositBookingId);
        var page = await browser.GetStringAsync(details, token);
        Assert.Contains("HOME-VERIFIED-325", page);
        Assert.Contains("325.00", page);
        Assert.Contains("325.00", await browser.GetStringAsync("/VisitBilling", token));

        using var managerBrowser = app.CreateBrowser();
        using var managerSession = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        var managerCsrf = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, "/Dashboard");
        using var cancellationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["revision"] = deposit.Revision.ToString(),
            ["reason"] = "Customer changed plans",
            ["__RequestVerificationToken"] = managerCsrf
        });
        using var cancel = await managerBrowser.PostAsync($"/OffsiteBookings/{deposit.Id}/CancelHome", cancellationForm, token);
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        var afterCancellation = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == deposit.Id, token);
        Assert.Equal(OffsiteStatus.Confirmed, afterCancellation.Status);
        Assert.Null(afterCancellation.HomeCancelledAtUtc);

        using var doubleCharge = await PostCashAsync(browser, details, bill.Revision, Guid.NewGuid(),
            "650", "650", csrf, token);
        Assert.Equal(HttpStatusCode.OK, doubleCharge.StatusCode);
        Assert.False(await db.Set<GroomingCashPayment>().AnyAsync(x => x.VisitId == visit.Id, token));
        using var settled = await PostCashAsync(browser, details, bill.Revision, Guid.NewGuid(),
            "325", "350", csrf, token);
        Assert.Equal(details, settled.Headers.Location?.OriginalString);
        var payments = await db.Set<GroomingCashPayment>().AsNoTracking().Where(x => x.VisitId == visit.Id).ToListAsync(token);
        Assert.Equal(325m, Assert.Single(payments).Amount);
        Assert.DoesNotContain("Record cash payment", await browser.GetStringAsync(details, token));
        Assert.Contains("Settled", await browser.GetStringAsync("/VisitBilling", token));
    }

    private static GroomingVisit NewVisit(Guid employeeId, string pet)
    {
        var estimate = new QuoteSnapshot("Sample owner", pet, "Dog", null, null, null,
            [new QuoteLine(Guid.NewGuid(), "Bath and dry", ["Bath"], 650m)], [], 650m, 650m, null, null);
        return new GroomingVisit
        {
            EstimateId = Guid.NewGuid(), EstimateVersion = 2, EstimateSnapshot = JsonSerializer.Serialize(estimate),
            RevisionToken = Guid.NewGuid(), StartedAtUtc = DateTime.UtcNow, StartedByEmployeeId = employeeId
        };
    }

    private static async Task<HttpResponseMessage> IssueAsync(HttpClient browser, Guid visitId, string? antiforgery,
        CancellationToken token)
    {
        var fields = new Dictionary<string, string>();
        if (antiforgery is not null) fields["__RequestVerificationToken"] = antiforgery;
        using var form = new FormUrlEncodedContent(fields);
        return await browser.PostAsync($"/VisitBilling/{visitId}/Issue", form, token);
    }

    private static async Task<HttpResponseMessage> PostCashAsync(HttpClient browser, string details, Guid revision,
        Guid operationId, string amount, string tendered, string? antiforgery, CancellationToken token)
    {
        var fields = new Dictionary<string, string>
        {
            ["revision"] = revision.ToString(), ["operationId"] = operationId.ToString(),
            ["amount"] = amount, ["tendered"] = tendered
        };
        if (antiforgery is not null) fields["__RequestVerificationToken"] = antiforgery;
        using var form = new FormUrlEncodedContent(fields);
        return await browser.PostAsync(details + "/Cash", form, token);
    }
}
