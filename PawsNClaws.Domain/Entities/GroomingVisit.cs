namespace PawsNClaws.Domain.Entities;

public sealed class GroomingVisit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EstimateId { get; set; }
    public int EstimateVersion { get; set; }
    public Guid StartedByEmployeeId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public string EstimateSnapshot { get; set; } = "";
    public DateTime? PickedUpAtUtc { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(150)]
    public string? CollectedBy { get; set; }
    public Guid? PickupRecordedByEmployeeId { get; set; }
    public Guid RevisionToken { get; set; }
}
