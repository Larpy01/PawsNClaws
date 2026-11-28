namespace PawsNClaws.Domain.Entities;

public enum SmsNotificationKind { AppointmentConfirmed, ReadyForPickup }
public enum SmsNotificationStatus { Pending, Submitting, Accepted, Skipped, Failed, NeedsReview }

public sealed class SmsNotification
{
    public SmsNotificationKind Kind { get; init; }
    public Guid SourceId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public SmsNotificationStatus Status { get; set; }
    public string? ProviderBatchId { get; set; }
    public string? Outcome { get; set; }
}
