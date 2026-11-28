using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class Phase7BookingTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Shop_appointments_fit_entirely_within_Philippine_opening_hours()
    {
        var (manager, groomer, estimate) = await CreateEstimateAsync();
        var token = TestContext.Current.CancellationToken;
        using var browser = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(browser, manager);
        var path = $"/GroomingAppointments/New/{estimate}";
        var localDay = DateTime.UtcNow.AddHours(8).Date.AddDays(3);

        async Task<HttpResponseMessage> ScheduleAsync(DateTime localStart, int minutes) => await PostAsync(browser,
            path, path + "?version=1", new()
            {
                ["version"] = "1", ["groomerId"] = groomer.Id.ToString(), ["startsAt"] = FormTime(localStart),
                ["durationMinutes"] = minutes.ToString(CultureInfo.InvariantCulture)
            });

        using var beforeOpening = await ScheduleAsync(localDay.AddHours(8).AddMinutes(59), 30);
        Assert.Equal(HttpStatusCode.OK, beforeOpening.StatusCode);
        using var pastClosing = await ScheduleAsync(localDay.AddHours(20).AddMinutes(31), 30);
        Assert.Equal(HttpStatusCode.OK, pastClosing.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.False(await db.Set<GroomingAppointment>().AnyAsync(x => x.EstimateId == estimate, token));

        using var endingAtClosing = await ScheduleAsync(localDay.AddHours(20).AddMinutes(30), 30);
        Assert.Equal(HttpStatusCode.Redirect, endingAtClosing.StatusCode);
        var booking = await db.Set<GroomingAppointment>().AsNoTracking().SingleAsync(x => x.EstimateId == estimate, token);
        Assert.Equal(localDay.AddHours(13), booking.EndsAtUtc);
    }

    [Fact]
    public async Task Groomer_weekly_hours_and_species_limit_the_slot()
    {
        var (manager, groomer, estimate) = await CreateEstimateAsync();
        var token = TestContext.Current.CancellationToken;
        var localDay = DateTime.UtcNow.AddHours(8).Date.AddDays(3);
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        var prefix = $"Grooming:Groomers:{groomer.Id}";
        configuration[$"{prefix}:WeeklyHours:{localDay.DayOfWeek}:Start"] = "11:00";
        configuration[$"{prefix}:WeeklyHours:{localDay.DayOfWeek}:End"] = "12:00";
        configuration[$"{prefix}:Species:0"] = "Cat";

        using var browser = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(browser, manager);
        var path = $"/GroomingAppointments/New/{estimate}";
        async Task<HttpResponseMessage> ScheduleAsync(DateTime localStart) => await PostAsync(browser,
            path, path + "?version=1", new()
            {
                ["version"] = "1", ["groomerId"] = groomer.Id.ToString(), ["startsAt"] = FormTime(localStart),
                ["durationMinutes"] = "30"
            });

        using var beforeShift = await ScheduleAsync(localDay.AddHours(10));
        Assert.Contains("groomer is unavailable", await beforeShift.Content.ReadAsStringAsync(token));
        using var exceedsShift = await ScheduleAsync(localDay.AddHours(11).AddMinutes(31));
        Assert.Contains("groomer is unavailable", await exceedsShift.Content.ReadAsStringAsync(token));
        using var otherDay = await ScheduleAsync(localDay.AddDays(1).AddHours(11));
        Assert.Contains("groomer is unavailable", await otherDay.Content.ReadAsStringAsync(token));
        using var speciesMismatch = await ScheduleAsync(localDay.AddHours(11));
        Assert.Contains("trained to work with this pet species", await speciesMismatch.Content.ReadAsStringAsync(token));

        configuration[$"{prefix}:Species:0"] = "dog";
        using var fitsShift = await ScheduleAsync(localDay.AddHours(11).AddMinutes(30));
        Assert.Equal(HttpStatusCode.Redirect, fitsShift.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.Single(await db.Set<GroomingAppointment>().Where(x => x.EstimateId == estimate).ToListAsync(token));
    }

    [Fact]
    public async Task Offsite_request_requires_manager_antiforgery_and_contact_before_review()
    {
        var (manager, _, estimate) = await CreateEstimateAsync();
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var token = TestContext.Current.CancellationToken;
        using var managerBrowser = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        using var cashierBrowser = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashierBrowser, cashier);

        const string board = "/OffsiteBookings";
        var path = $"{board}/New/{estimate}";
        var page = path + "?version=1";
        var localStart = DateTime.UtcNow.AddHours(8).Date.AddDays(3).AddHours(10);

        using var blocked = await cashierBrowser.GetAsync(page, token);
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Contains("/Account/AccessDenied", blocked.Headers.Location?.OriginalString);
        var form = new Dictionary<string, string>
        {
            ["version"] = "1", ["kind"] = "HomeService", ["requestedAt"] = FormTime(localStart),
            ["address"] = "", ["phone"] = "09171234567"
        };
        using var withoutToken = await PostAsync(managerBrowser, path, null, form);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        using var incomplete = await PostAsync(managerBrowser, path, page, form);
        Assert.Equal(HttpStatusCode.OK, incomplete.StatusCode);

        Assert.Contains("value=\"09171234567\"", await incomplete.Content.ReadAsStringAsync(token));

        form["address"] = "123 Booking Test Street";
        using var requested = await PostAsync(managerBrowser, path, page, form);
        Assert.Equal(HttpStatusCode.Redirect, requested.StatusCode);
        var boardHtml = await managerBrowser.GetStringAsync(board, token);
        Assert.Contains("Test Pet", boardHtml);
        Assert.Contains("downtown Davao City", boardHtml);
        Assert.Contains("123 Booking Test Street", boardHtml);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var home = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.EstimateId == estimate, token);
        Assert.Equal(localStart.AddHours(-8), home.RequestedAtUtc);
        Assert.Equal(manager.Id, home.RecordedBy);
        Assert.Equal(OffsiteStatus.Requested, home.Status);

        using var duplicate = await PostAsync(managerBrowser, path, page, form);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already has an open request", await duplicate.Content.ReadAsStringAsync(token));
        async Task<HttpResponseMessage> ReviewAsync(Guid id, Guid revision, string decision, string reason) =>
            await PostAsync(managerBrowser, $"{board}/{id}/Review", board, new()
            {
                ["revision"] = revision.ToString(), ["decision"] = decision, ["reason"] = reason
            });

        using var wrongDecision = await ReviewAsync(home.Id, home.Revision, "Reviewed", "Manual review");
        Assert.Contains("Choose a decision for this request type", await managerBrowser.GetStringAsync(board, token));
        using var stale = await ReviewAsync(home.Id, Guid.NewGuid(), "AwaitingDeposit", "Manual review");
        Assert.Contains("request has changed", await managerBrowser.GetStringAsync(board, token));
        using var reviewed = await ReviewAsync(home.Id, home.Revision, "AwaitingDeposit", "Awaiting customer transfer");
        Assert.Equal(HttpStatusCode.Redirect, reviewed.StatusCode);
        var historyPath = reviewed.Headers.Location?.OriginalString;
        Assert.NotNull(historyPath);
        var history = await managerBrowser.GetStringAsync(historyPath, token);
        Assert.Contains("Awaiting deposit", history);
        Assert.Contains("Awaiting customer transfer", history);
        Assert.DoesNotContain("123 Booking Test Street", await managerBrowser.GetStringAsync(board, token));

        form["kind"] = "Pickup";
        using var pickupRequest = await PostAsync(managerBrowser, path, page, form);
        Assert.Equal(HttpStatusCode.Redirect, pickupRequest.StatusCode);
        form["kind"] = "ReturnDelivery";
        using var returnRequest = await PostAsync(managerBrowser, path, page, form);
        Assert.Equal(HttpStatusCode.Redirect, returnRequest.StatusCode);

        var entries = await db.Set<OffsiteBooking>().AsNoTracking().Where(x => x.EstimateId == estimate).ToListAsync(token);
        Assert.Equal(3, entries.Count);
        var pickup = entries.Single(x => x.Kind == OffsiteKind.Pickup);
        using var transportReviewed = await ReviewAsync(pickup.Id, pickup.Revision, "Reviewed", "Reviewed the route");
        Assert.Equal(HttpStatusCode.Redirect, transportReviewed.StatusCode);
        var savedPickup = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == pickup.Id, token);
        Assert.Equal(OffsiteStatus.Reviewed, savedPickup.Status);
        Assert.Equal(manager.Id, savedPickup.ReviewedBy);
        var delivery = entries.Single(x => x.Kind == OffsiteKind.ReturnDelivery);
        using var deliveryReviewed = await ReviewAsync(delivery.Id, delivery.Revision, "Reviewed", "Return route reviewed");
        Assert.Equal(HttpStatusCode.Redirect, deliveryReviewed.StatusCode);
        var savedDelivery = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == delivery.Id, token);

        async Task<HttpResponseMessage> ScheduleTransportAsync(OffsiteBooking item, HttpClient client, DateTime start) =>
            await PostAsync(client, $"{board}/{item.Id}/ScheduleTransport", historyPath, new()
            {
                ["revision"] = item.Revision.ToString(), ["assignee"] = cashier.Id.ToString(),
                ["startsAt"] = FormTime(start), ["minutes"] = "30"
            });

        using var scheduled = await ScheduleTransportAsync(savedPickup, managerBrowser, localStart);
        Assert.Equal(HttpStatusCode.Redirect, scheduled.StatusCode);
        Assert.Contains("Transport scheduled", await managerBrowser.GetStringAsync(historyPath, token));
        var scheduledPickup = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == pickup.Id, token);
        Assert.Equal(OffsiteStatus.Scheduled, scheduledPickup.Status);
        Assert.Equal(cashier.Id, scheduledPickup.TransportEmployeeId);
        Assert.Equal(localStart.AddHours(-8), scheduledPickup.TransportStartUtc);
        Assert.Equal(localStart.AddHours(-8).AddMinutes(30), scheduledPickup.TransportEndUtc);
        using var conflicting = await ScheduleTransportAsync(savedDelivery, managerBrowser, localStart.AddMinutes(15));
        Assert.Equal(HttpStatusCode.Redirect, conflicting.StatusCode);
        Assert.Contains("already has work scheduled", await managerBrowser.GetStringAsync(historyPath, token));
        Assert.True(await db.Set<OffsiteBooking>().AnyAsync(x => x.Id == delivery.Id && x.Status == OffsiteStatus.Reviewed, token));
        using var following = await ScheduleTransportAsync(savedDelivery, managerBrowser, localStart.AddMinutes(30));
        Assert.Equal(HttpStatusCode.Redirect, following.StatusCode);
        var laterDelivery = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == delivery.Id, token);
        Assert.Equal(OffsiteStatus.Scheduled, laterDelivery.Status);
        Assert.Equal(scheduledPickup.TransportEndUtc, laterDelivery.TransportStartUtc);

        async Task<HttpResponseMessage> CancelTransportAsync(Guid id, Guid revision, string reason) =>
            await PostAsync(managerBrowser, $"{board}/{id}/CancelTransport", historyPath, new()
            {
                ["revision"] = revision.ToString(), ["reason"] = reason
            });

        using var invalidCancellation = await CancelTransportAsync(laterDelivery.Id, laterDelivery.Revision, "");
        Assert.Contains("Enter a cancellation reason", await managerBrowser.GetStringAsync(historyPath, token));
        using var cancelReturn = await CancelTransportAsync(laterDelivery.Id, laterDelivery.Revision, "Owner changed plans");
        Assert.Equal(HttpStatusCode.Redirect, cancelReturn.StatusCode);
        var cancelledReturn = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == delivery.Id, token);
        Assert.Equal(OffsiteStatus.Cancelled, cancelledReturn.Status);
        Assert.Equal("Owner changed plans", cancelledReturn.TransportCancellationReason);
        Assert.Equal(manager.Id, cancelledReturn.TransportCancelledBy);

        async Task<HttpResponseMessage> HandoverAsync(Guid revision, string person) =>
            await PostAsync(managerBrowser, $"{board}/{pickup.Id}/Handover", historyPath, new()
            {
                ["revision"] = revision.ToString(), ["person"] = person
            });

        using var empty = await HandoverAsync(scheduledPickup.Revision, " ");
        Assert.Contains("Enter a handover name", await managerBrowser.GetStringAsync(historyPath, token));
        using var custody = await HandoverAsync(scheduledPickup.Revision, "Pet owner");
        Assert.Equal(HttpStatusCode.Redirect, custody.StatusCode);
        var collected = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == pickup.Id, token);
        Assert.Equal(OffsiteStatus.InCustody, collected.Status);
        Assert.Equal("Pet owner", collected.ReleasedByName);
        Assert.NotNull(collected.CustodyAtUtc);
        Assert.Equal(manager.Id, collected.CustodyRecordedBy);

        using var cancelAfterCollection = await CancelTransportAsync(collected.Id, collected.Revision, "Cancelled");
        Assert.Contains("before the pet is collected", await managerBrowser.GetStringAsync(historyPath, token));
        Assert.True(await db.Set<OffsiteBooking>().AnyAsync(x => x.Id == pickup.Id && x.Status == OffsiteStatus.InCustody, token));

        using var staleHandover = await HandoverAsync(scheduledPickup.Revision, "Old browser");
        Assert.Contains("booking changed", await managerBrowser.GetStringAsync(historyPath, token));
        using var delivered = await HandoverAsync(collected.Revision, "Reception staff");
        Assert.Equal(HttpStatusCode.Redirect, delivered.StatusCode);
        var completed = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == pickup.Id, token);
        Assert.Equal(OffsiteStatus.Completed, completed.Status);
        Assert.Equal("Reception staff", completed.AcceptedByName);
        Assert.Equal(manager.Id, completed.DeliveryRecordedBy);
        Assert.True(completed.DeliveredAtUtc >= collected.CustodyAtUtc);

        var cashierToken = await LocalAppFactory.GetAntiforgeryTokenAsync(cashierBrowser, "/Customers/New");
        using var blockedHandover = await PostAsync(cashierBrowser, $"{board}/{pickup.Id}/Handover", null, new()
        {
            ["revision"] = completed.Revision.ToString(), ["person"] = "Cashier",
            ["__RequestVerificationToken"] = cashierToken
        });
        Assert.Contains("/Account/AccessDenied", blockedHandover.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Development_home_service_default_requires_deposit_and_paid_cancellation_releases_the_slot()
    {
        using var development = new LocalAppFactory("Development");
        var (manager, groomer, estimate) = await CreateEstimateAsync(development);
        var (_, _, competingEstimate) = await CreateEstimateAsync(development);
        var token = TestContext.Current.CancellationToken;
        const string depositKey = "Grooming:HomeService:DepositPercent";
        Assert.Equal("50", development.Services.GetRequiredService<IConfiguration>()[depositKey]);
        Assert.Null(app.Services.GetRequiredService<IConfiguration>()[depositKey]);
        await using var scope = development.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var startLocal = DateTime.UtcNow.AddHours(8).Date.AddDays(4).AddHours(10);
        var startUtc = startLocal.AddHours(-8);
        var hold = new OffsiteBooking
        {
            EstimateId = estimate, EstimateVersion = 1, Kind = OffsiteKind.HomeService,
            Status = OffsiteStatus.AwaitingDeposit, RequestedAtUtc = startUtc,
            Address = "123 Booking Test Street", Phone = "09171234567", RecordedBy = manager.Id
        };
        db.Add(hold);
        await db.SaveChangesAsync(token);
        using var browser = development.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(browser, manager);
        const string history = "/OffsiteBookings?history=true";

        async Task<HttpResponseMessage> ReserveAsync(DateTime expiryLocal) => await PostAsync(browser,
            $"/OffsiteBookings/{hold.Id}/HoldHome", history, new()
            {
                ["revision"] = hold.Revision.ToString(), ["groomerId"] = groomer.Id.ToString(),
                ["minutes"] = "90", ["expiresAt"] = FormTime(expiryLocal)
            });

        using var staleExpiry = await ReserveAsync(startLocal.AddDays(-7));
        Assert.Contains("expiry before", await browser.GetStringAsync(history, token));
        using var held = await ReserveAsync(startLocal.AddHours(-1));
        Assert.Equal(HttpStatusCode.Redirect, held.StatusCode);
        Assert.Contains("Home-service slot reserved", await browser.GetStringAsync(history, token));
        var reserved = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == hold.Id, token);
        Assert.Equal(250m, reserved.DepositRequired);
        Assert.Equal(startUtc.AddHours(-1), reserved.HoldExpiresAtUtc);
        Assert.Equal(startUtc.AddMinutes(90), reserved.HomeEndsAtUtc);
        Assert.Equal(OffsiteStatus.AwaitingDeposit, reserved.Status);

        var inShop = $"/GroomingAppointments/New/{competingEstimate}";
        using var overlappingAppointment = await PostAsync(browser, inShop, inShop + "?version=1", new()
        {
            ["version"] = "1", ["groomerId"] = groomer.Id.ToString(),
            ["startsAt"] = FormTime(startLocal.AddHours(1)), ["durationMinutes"] = "30"
        });
        Assert.Equal(HttpStatusCode.OK, overlappingAppointment.StatusCode);
        Assert.False(await db.Set<GroomingAppointment>().AnyAsync(x => x.EstimateId == competingEstimate, token));

        async Task<HttpResponseMessage> ConfirmAsync(bool accepted, string amount) => await PostAsync(browser,
            $"/OffsiteBookings/{hold.Id}/ConfirmHome", history, new()
            {
                ["revision"] = reserved.Revision.ToString(), ["accepted"] = accepted.ToString(),
                ["amount"] = amount, ["reference"] = "Bank reference 321"
            });

        using var underpaid = await ConfirmAsync(true, "249.99");
        Assert.Contains("verified deposit", await browser.GetStringAsync(history, token));
        using var notAccepted = await ConfirmAsync(false, "250");
        Assert.Contains("customer accepted", await browser.GetStringAsync(history, token));
        Assert.True(await db.Set<OffsiteBooking>().AnyAsync(x => x.Id == hold.Id && x.Status == OffsiteStatus.AwaitingDeposit, token));

        using var confirmed = await ConfirmAsync(true, "250");
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Contains("Home-service booking confirmed", await browser.GetStringAsync(history, token));
        var saved = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == hold.Id, token);
        Assert.Equal(OffsiteStatus.Confirmed, saved.Status);
        Assert.Equal(250m, saved.VerifiedDeposit);
        Assert.Equal("Bank reference 321", saved.DepositReference);
        Assert.Equal(manager.Id, saved.ConfirmedBy);
        var cancelPath = $"/OffsiteBookings/{hold.Id}/CancelHome";
        using var missingReason = await PostAsync(browser, cancelPath, history, new()
        {
            ["revision"] = saved.Revision.ToString(), ["reason"] = "  "
        });
        Assert.Equal(HttpStatusCode.Redirect, missingReason.StatusCode);
        Assert.Contains("reason", await browser.GetStringAsync(history, token), StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.Set<OffsiteBooking>().AnyAsync(x => x.Id == hold.Id && x.Status == OffsiteStatus.Confirmed, token));

        using var cancelled = await PostAsync(browser, cancelPath, history, new()
        {
            ["revision"] = saved.Revision.ToString(), ["reason"] = "Owner requested cancellation."
        });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        var cancellationHistory = await browser.GetStringAsync(history, token);
        Assert.Contains("Awaiting manual refund review", cancellationHistory);
        Assert.Contains("Bank reference 321", cancellationHistory);
        var cancelledBooking = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == hold.Id, token);
        Assert.Equal(OffsiteStatus.Cancelled, cancelledBooking.Status);
        Assert.Equal(250m, cancelledBooking.VerifiedDeposit);
        Assert.Equal("Bank reference 321", cancelledBooking.DepositReference);
        Assert.Equal(manager.Id, cancelledBooking.HomeCancelledBy);
        Assert.NotNull(cancelledBooking.HomeCancelledAtUtc);
        Assert.Equal("Owner requested cancellation.", cancelledBooking.HomeCancellationReason);
        Assert.Null(cancelledBooking.RefundReviewedAtUtc);

        using var reopenedSlot = await PostAsync(browser, inShop, inShop + "?version=1", new()
        {
            ["version"] = "1", ["groomerId"] = groomer.Id.ToString(),
            ["startsAt"] = FormTime(startLocal.AddHours(1)), ["durationMinutes"] = "30"
        });
        Assert.Equal(HttpStatusCode.Redirect, reopenedSlot.StatusCode);
        Assert.True(await db.Set<GroomingAppointment>().AnyAsync(x => x.EstimateId == competingEstimate, token));

        var reviewPath = $"/OffsiteBookings/{hold.Id}/ReviewRefund";
        using var staleReview = await PostAsync(browser, reviewPath, history, new()
        {
            ["revision"] = saved.Revision.ToString(), ["note"] = "Stale review"
        });
        Assert.Equal(HttpStatusCode.Redirect, staleReview.StatusCode);
        Assert.Contains("Awaiting manual refund review", await browser.GetStringAsync(history, token));

        using var reviewed = await PostAsync(browser, reviewPath, history, new()
        {
            ["revision"] = cancelledBooking.Revision.ToString(), ["note"] = "Manager contacted the customer."
        });
        Assert.Equal(HttpStatusCode.Redirect, reviewed.StatusCode);
        Assert.Contains("Refund reviewed", await browser.GetStringAsync(history, token));
        var reviewedBooking = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == hold.Id, token);
        Assert.Equal(manager.Id, reviewedBooking.RefundReviewedBy);
        Assert.NotNull(reviewedBooking.RefundReviewedAtUtc);
        Assert.Equal("Manager contacted the customer.", reviewedBooking.RefundReviewNote);
        Assert.Equal(250m, reviewedBooking.VerifiedDeposit);
    }

    [Fact]
    public async Task Unpaid_home_service_cancellation_does_not_offer_refund_review()
    {
        var (manager, _, estimate) = await CreateEstimateAsync();
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var request = new OffsiteBooking
        {
            EstimateId = estimate, EstimateVersion = 1, Kind = OffsiteKind.HomeService,
            Status = OffsiteStatus.AwaitingDeposit, RequestedAtUtc = DateTime.UtcNow.AddDays(3),
            Address = "Downtown Davao City", Phone = "09171234567", RecordedBy = manager.Id
        };
        db.Add(request);
        await db.SaveChangesAsync(token);
        using var browser = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(browser, manager);
        const string history = "/OffsiteBookings?history=true";
        using var cancelled = await PostAsync(browser, $"/OffsiteBookings/{request.Id}/CancelHome", history, new()
        {
            ["revision"] = request.Revision.ToString(), ["reason"] = "Customer changed plans."
        });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        var page = await browser.GetStringAsync(history, token);
        Assert.Contains("Customer changed plans.", page);
        Assert.DoesNotContain("Awaiting manual refund review", page);
        var saved = await db.Set<OffsiteBooking>().AsNoTracking().SingleAsync(x => x.Id == request.Id, token);
        Assert.Equal(OffsiteStatus.Cancelled, saved.Status);
        Assert.Null(saved.VerifiedDeposit);
    }

    private async Task<(TestEmployee Manager, TestEmployee Groomer, Guid Estimate)> CreateEstimateAsync(LocalAppFactory? factory = null)
    {
        factory ??= app;
        var token = TestContext.Current.CancellationToken;
        var manager = await factory.CreateEmployeeAsync(LocalRoles.Manager);
        var groomer = await factory.CreateEmployeeAsync(LocalRoles.Groomer);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var owner = new CustomerRecord { FirstName = "Test", LastName = "Owner" };
        var pet = new PetRecord { CustomerId = owner.Id, Name = "Test Pet", Species = "Dog" };
        var service = new GroomingService { Name = "Bath", DurationMinutes = 30, IsActive = true };
        var estimate = new GroomingQuote { Id = Guid.NewGuid(), PetId = pet.Id, LatestRevision = 1 };
        var snapshot = new QuoteSnapshot("Test Owner", pet.Name, pet.Species, null, null, null,
            [new QuoteLine(service.Id, service.Name, [service.Name], 500)], [], 500, 500, null, null);
        db.AddRange(owner, pet, service, estimate, new GroomingQuoteRevision
        {
            QuoteId = estimate.Id, Number = 1, EmployeeId = manager.Id, CreatedAtUtc = DateTime.UtcNow,
            Snapshot = JsonSerializer.Serialize(snapshot)
        });
        await db.SaveChangesAsync(token);
        return (manager, groomer, estimate.Id);
    }

    private static string FormTime(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string path, string? tokenPath,
        Dictionary<string, string> fields)
    {
        if (tokenPath is not null)
            fields["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(browser, tokenPath);
        using var body = new FormUrlEncodedContent(fields);
        return await browser.PostAsync(path, body, TestContext.Current.CancellationToken);
    }
}
