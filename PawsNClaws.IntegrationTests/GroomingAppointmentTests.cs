using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class GroomingAppointmentTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Manager_manages_appointments_without_overlapping_pets_or_groomers()
    {
        var token = TestContext.Current.CancellationToken;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var firstGroomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var secondGroomer = await app.CreateEmployeeAsync(LocalRoles.Groomer);
        var inactiveGroomer = await app.CreateEmployeeAsync(LocalRoles.Groomer, isActive: false);
        var owner = new CustomerRecord { FirstName = "Test", LastName = "Owner" };
        var firstPet = new PetRecord { CustomerId = owner.Id, Name = "Pip", Species = "Dog" };
        var secondPet = new PetRecord { CustomerId = owner.Id, Name = "Poppy", Species = "Dog" };
        var service = new GroomingService { Name = "Bath", DurationMinutes = 30, IsActive = true };
        db.AddRange(owner, firstPet, secondPet, service);
        Guid AddEstimate(PetRecord pet)
        {
            var estimate = new GroomingQuote { Id = Guid.NewGuid(), PetId = pet.Id, LatestRevision = 2 };
            var snapshot = new QuoteSnapshot("Test Owner", pet.Name, pet.Species, null, null, null,
                [new QuoteLine(service.Id, service.Name, [service.Name], 500)], [], 500, 500, null, null);
            db.AddRange(estimate, new GroomingQuoteRevision
            {
                QuoteId = estimate.Id, Number = 1, EmployeeId = manager.Id, CreatedAtUtc = DateTime.UtcNow,
                Snapshot = JsonSerializer.Serialize(snapshot)
            });
            db.Add(new GroomingQuoteRevision
            {
                QuoteId = estimate.Id, Number = 2, EmployeeId = manager.Id, CreatedAtUtc = DateTime.UtcNow,
                Snapshot = JsonSerializer.Serialize(snapshot)
            });
            return estimate.Id;
        }
        var firstEstimate = AddEstimate(firstPet);
        var secondEstimate = AddEstimate(secondPet);
        var duplicatePetEstimate = AddEstimate(firstPet);
        await db.SaveChangesAsync(token);

        using var managerBrowser = app.CreateBrowser();
        using var managerLogin = await LocalAppFactory.SignInAsync(managerBrowser, manager);
        using var cashierBrowser = app.CreateBrowser();
        using var cashierLogin = await LocalAppFactory.SignInAsync(cashierBrowser, cashier);
        const string boardPath = "/GroomingAppointments";
        var firstForm = $"{boardPath}/New/{firstEstimate}";
        var secondForm = $"{boardPath}/New/{secondEstimate}";
        var startLocal = DateTime.UtcNow.AddHours(8).Date.AddDays(2).AddHours(10);
        var bookedDay = startLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var datedBoard = boardPath + "?date=" + bookedDay;
        string FormTime(DateTime time) => time.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        async Task<HttpResponseMessage> BookAsync(HttpClient browser, string path, Guid groomer, DateTime time,
            string duration = "30", bool includeAntiforgery = true)
        {
            var values = new Dictionary<string, string>
            {
                ["version"] = "1", ["groomerId"] = groomer.ToString(),
                ["startsAt"] = FormTime(time), ["durationMinutes"] = duration
            };
            if (includeAntiforgery)
                values["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(browser, path + "?version=1");
            using var body = new FormUrlEncodedContent(values);
            return await browser.PostAsync(path, body, token);
        }

        Assert.Contains("appointment", await cashierBrowser.GetStringAsync(boardPath, token), StringComparison.OrdinalIgnoreCase);
        using var blockedForm = await cashierBrowser.GetAsync(firstForm + "?version=1", token);
        Assert.Equal(HttpStatusCode.Redirect, blockedForm.StatusCode);
        Assert.Contains("/Account/AccessDenied", blockedForm.Headers.Location?.OriginalString);
        using var withoutToken = await BookAsync(managerBrowser, firstForm, firstGroomer.Id, startLocal, includeAntiforgery: false);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        using var inactive = await BookAsync(managerBrowser, firstForm, inactiveGroomer.Id, startLocal);
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        using var past = await BookAsync(managerBrowser, firstForm, firstGroomer.Id, startLocal.AddDays(-7));
        Assert.Equal(HttpStatusCode.OK, past.StatusCode);
        using var invalidDuration = await BookAsync(managerBrowser, firstForm, firstGroomer.Id, startLocal, duration: "0");
        Assert.Equal(HttpStatusCode.OK, invalidDuration.StatusCode);
        Assert.False(await db.Set<GroomingAppointment>().AnyAsync(token));

        using var booking = await BookAsync(managerBrowser, firstForm, firstGroomer.Id, startLocal);
        Assert.Equal(HttpStatusCode.Redirect, booking.StatusCode);
        Assert.Equal(datedBoard, booking.Headers.Location?.OriginalString);
        var saved = await db.Set<GroomingAppointment>().AsNoTracking().SingleAsync(token);
        Assert.Equal(firstPet.Id, saved.PetId);
        Assert.Equal(firstEstimate, saved.EstimateId);
        Assert.Equal(1, saved.EstimateVersion);
        Assert.Equal(firstGroomer.Id, saved.GroomerId);
        Assert.Equal(startLocal.AddHours(-8), saved.StartsAtUtc);
        Assert.Equal(saved.StartsAtUtc.AddMinutes(30), saved.EndsAtUtc);
        Assert.True(await db.Set<SmsNotification>().AnyAsync(x => x.Kind == SmsNotificationKind.AppointmentConfirmed &&
            x.SourceId == saved.Id && x.Status == SmsNotificationStatus.Pending, token));

        using var petCollision = await BookAsync(managerBrowser, $"{boardPath}/New/{duplicatePetEstimate}", secondGroomer.Id, startLocal.AddMinutes(15));
        Assert.Equal(HttpStatusCode.OK, petCollision.StatusCode);
        using var groomerCollision = await BookAsync(managerBrowser, secondForm, firstGroomer.Id, startLocal.AddMinutes(15));
        Assert.Equal(HttpStatusCode.OK, groomerCollision.StatusCode);
        Assert.Equal(1, await db.Set<GroomingAppointment>().CountAsync(token));
        using var adjacent = await BookAsync(managerBrowser, secondForm, firstGroomer.Id, startLocal.AddMinutes(30));
        Assert.Equal(HttpStatusCode.Redirect, adjacent.StatusCode);

        async Task<HttpResponseMessage> CancelAppointmentAsync(Guid appointmentId, Guid revision, string reason)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, datedBoard),
                ["revision"] = revision.ToString(), ["reason"] = reason, ["date"] = bookedDay
            });
            return await managerBrowser.PostAsync($"{boardPath}/{appointmentId}/Cancel", body, token);
        }
        using var noReason = await CancelAppointmentAsync(saved.Id, saved.Revision, "");
        Assert.Contains("Enter a reason for cancelling", await managerBrowser.GetStringAsync(datedBoard, token));
        using var stale = await CancelAppointmentAsync(saved.Id, Guid.NewGuid(), "Stale request");
        Assert.Contains("The appointment changed", await managerBrowser.GetStringAsync(datedBoard, token));
        Assert.False(await db.Set<GroomingAppointment>().AnyAsync(x => x.CancelledAtUtc != null, token));
        using var cancelled = await CancelAppointmentAsync(saved.Id, saved.Revision, "Owner rescheduled");
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.True(await db.Set<GroomingAppointment>().AnyAsync(x => x.Id == saved.Id && x.CancelledAtUtc != null, token));
        using var replacement = await BookAsync(managerBrowser, firstForm, firstGroomer.Id, startLocal);
        Assert.Equal(HttpStatusCode.Redirect, replacement.StatusCode);
        Assert.Contains("Pip", await cashierBrowser.GetStringAsync(datedBoard, token));

        async Task<HttpResponseMessage> StartVisitAsync(int version, Guid? appointmentId = null)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(managerBrowser, datedBoard),
                ["estimateId"] = firstEstimate.ToString(), ["version"] = version.ToString(CultureInfo.InvariantCulture),
                ["appointmentId"] = appointmentId?.ToString() ?? ""
            });
            return await managerBrowser.PostAsync("/GroomingVisits/Start", body, token);
        }
        using var wrongVersion = await StartVisitAsync(2);
        Assert.Equal(HttpStatusCode.NotFound, wrongVersion.StatusCode);
        Assert.False(await db.Set<GroomingVisit>().AnyAsync(token));
        using var staleStart = await StartVisitAsync(1, saved.Id);
        Assert.Equal(HttpStatusCode.NotFound, staleStart.StatusCode);
        var activeBooking = await db.Set<GroomingAppointment>().AsNoTracking()
            .SingleAsync(x => x.EstimateId == firstEstimate && x.CancelledAtUtc == null, token);
        using var started = await StartVisitAsync(1, activeBooking.Id);
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        Assert.Equal(1, (await db.Set<GroomingVisit>().AsNoTracking().SingleAsync(token)).EstimateVersion);
        using var startedCancellation = await CancelAppointmentAsync(activeBooking.Id, activeBooking.Revision, "Owner rescheduled");
        Assert.Contains("A grooming visit has already started", await managerBrowser.GetStringAsync(datedBoard, token));
        Assert.True(await db.Set<GroomingAppointment>().AnyAsync(x => x.Id == activeBooking.Id && x.CancelledAtUtc == null, token));
    }
}
