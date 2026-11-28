using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Application.Features.Notifications;

public sealed record SmsQueueRow(SmsNotificationKind Kind, DateTime CreatedAtUtc, SmsNotificationStatus Status,
    DateTime? LastAttemptUtc, string? Outcome, string? ProviderBatchId);

public interface ISmsQueueStatus
{
    Task<IReadOnlyList<SmsQueueRow>> ListAsync(CancellationToken token);
}
