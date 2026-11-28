using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Notifications;

public sealed class SmsDispatcher(LocalDbContext db, TextBeeClient client)
{
    public async Task<bool> ProcessNextAsync(string apiKey, string? deviceId, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var stale = now.AddMinutes(-2);
        var notifications = db.Set<SmsNotification>();
        var item = await notifications.Where(x =>
                (x.Status == SmsNotificationStatus.Pending && x.NextAttemptUtc <= now) ||
                (x.Status == SmsNotificationStatus.Submitting && x.LastAttemptUtc <= stale))
            .OrderBy(x => x.NextAttemptUtc).ThenBy(x => x.Kind).ThenBy(x => x.SourceId).FirstOrDefaultAsync(token);
        if (item is null) return false;

        try
        {
            if (item.Status == SmsNotificationStatus.Submitting)
            {
                item.Status = SmsNotificationStatus.NeedsReview;
                item.Outcome = "submission_interrupted";
            }
            else if (await SmsOutbox.ResolveAsync(db, item, token) is { } message)
            {
                // Persist the claim before making the non-idempotent provider request.
                item.Status = SmsNotificationStatus.Submitting;
                item.LastAttemptUtc = now;
                await db.SaveChangesAsync(token);
                var result = await client.SubmitAsync(message, apiKey, deviceId, token);
                item.Status = result.Status;
                item.Outcome = result.Outcome;
                item.ProviderBatchId = result.BatchId;
                if (result.RetryAt is { } retryAt)
                    item.NextAttemptUtc = retryAt.UtcDateTime > now.AddMinutes(1) ? retryAt.UtcDateTime : now.AddMinutes(1);
            }
            else
            {
                item.Status = SmsNotificationStatus.Skipped;
                item.Outcome = "contact_or_event_unavailable";
            }

            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
        }

        return true;
    }
}
