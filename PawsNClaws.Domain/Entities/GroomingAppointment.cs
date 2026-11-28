namespace PawsNClaws.Domain.Entities;

public sealed class GroomingAppointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid PetId { get; set; }
    public Guid GroomerId { get; set; }
    public Guid EstimateId { get; set; }
    public int EstimateVersion { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public Guid ScheduledBy { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
}
