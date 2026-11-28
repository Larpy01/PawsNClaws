namespace PawsNClaws.Domain.Entities;

public enum WalkInStatus { Waiting, Called, CheckedIn, Cancelled }

public sealed class WalkInEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PetId { get; set; }
    public Guid AddedBy { get; set; }
    public DateTime ArrivedAtUtc { get; set; }
    public WalkInStatus Status { get; set; }
    public bool IsOpen { get; set; } = true;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid? CalledBy { get; set; }
    public DateTime? CalledAtUtc { get; set; }
    public string? PriorityReason { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? VisitId { get; set; }
}
