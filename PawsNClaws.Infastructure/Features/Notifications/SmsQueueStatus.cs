using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Notifications;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Notifications;

public sealed class SmsQueueStatus(LocalDbContext db) : ISmsQueueStatus
{
    public async Task<IReadOnlyList<SmsQueueRow>> ListAsync(CancellationToken token) =>
        await db.Set<SmsNotification>().AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Kind).ThenBy(x => x.SourceId).Take(100)
            .Select(x => new SmsQueueRow(x.Kind, x.CreatedAtUtc, x.Status, x.LastAttemptUtc, x.Outcome, x.ProviderBatchId))
            .ToListAsync(token);
}
