using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Features.Notifications;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class SmsDispatcherTests
{
    [Fact]
    public async Task Offline_attempt_retries_but_uncertain_submission_does_not_send_twice()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        var owner = new CustomerRecord { Phone = "0917-000-0123", RecordConsent = true };
        var pet = new PetRecord { CustomerId = owner.Id };
        var appointment = new GroomingAppointment { PetId = pet.Id, StartsAtUtc = now.AddDays(1) };
        var notification = new SmsNotification
        {
            Kind = SmsNotificationKind.AppointmentConfirmed, SourceId = appointment.Id,
            CreatedAtUtc = now, NextAttemptUtc = now
        };
        db.AddRange(owner, pet, appointment, notification);
        await db.SaveChangesAsync(token);

        const string testKey = "test-only-credential";
        using var transport = new StubTransport(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.textbee.dev/api/v1/gateway/send-sms", req.RequestUri?.AbsoluteUri);
            Assert.Equal(testKey, Assert.Single(req.Headers.GetValues("x-api-key")));
        });
        using var client = new HttpClient(transport);
        var dispatcher = new SmsDispatcher(db, new TextBeeClient(client));
        Assert.True(await dispatcher.ProcessNextAsync(testKey, "test-device", token));
        Assert.Equal(SmsNotificationStatus.Pending, notification.Status);
        Assert.True(notification.NextAttemptUtc > now);
        Assert.False(await dispatcher.ProcessNextAsync(testKey, "test-device", token));
        Assert.Equal(1, transport.Calls);

        notification.NextAttemptUtc = now;
        await db.SaveChangesAsync(token);
        Assert.True(await dispatcher.ProcessNextAsync(testKey, "test-device", token));
        Assert.Equal(SmsNotificationStatus.Accepted, notification.Status);
        Assert.Equal("sms-batch-test", notification.ProviderBatchId);
        using var payload = JsonDocument.Parse(Assert.IsType<string>(transport.Body));
        var body = payload.RootElement;
        Assert.Equal("+639170000123", Assert.Single(body.GetProperty("recipients").EnumerateArray()).GetString());
        Assert.Contains("Your in-shop grooming appointment is confirmed", body.GetProperty("message").GetString());
        Assert.Equal("test-device", body.GetProperty("deviceId").GetString());
        Assert.DoesNotContain(testKey, transport.Body);

        var second = new GroomingAppointment { PetId = pet.Id, StartsAtUtc = now.AddDays(2) };
        var unknown = new SmsNotification
        {
            Kind = SmsNotificationKind.AppointmentConfirmed, SourceId = second.Id, CreatedAtUtc = now, NextAttemptUtc = now
        };
        db.AddRange(second, unknown);
        await db.SaveChangesAsync(token);
        Assert.True(await dispatcher.ProcessNextAsync(testKey, null, token));
        Assert.Equal(SmsNotificationStatus.NeedsReview, unknown.Status);
        Assert.False(await dispatcher.ProcessNextAsync(testKey, null, token));
        Assert.Equal(3, transport.Calls);

        var cancelled = new GroomingAppointment { PetId = pet.Id, StartsAtUtc = now.AddDays(2), CancelledAtUtc = now };
        var skipped = new SmsNotification
        {
            Kind = SmsNotificationKind.AppointmentConfirmed, SourceId = cancelled.Id, CreatedAtUtc = now, NextAttemptUtc = now
        };
        db.AddRange(cancelled, skipped);
        await db.SaveChangesAsync(token);
        Assert.True(await dispatcher.ProcessNextAsync(testKey, null, token));
        Assert.Equal(SmsNotificationStatus.Skipped, skipped.Status);
        Assert.Equal(3, transport.Calls);
    }

    private sealed class StubTransport(Action<HttpRequestMessage> verify) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            verify(request);
            Body = await request.Content!.ReadAsStringAsync(token);
            return ++Calls switch
            {
                1 => throw new HttpRequestException(HttpRequestError.NameResolutionError, "Test network unavailable."),
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":{"success":true,"smsBatchId":"sms-batch-test"}}""",
                        Encoding.UTF8, "application/json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            };
        }
    }
}
