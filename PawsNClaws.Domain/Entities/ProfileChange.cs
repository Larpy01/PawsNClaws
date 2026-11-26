namespace PawsNClaws.Domain.Entities;

public sealed class ProfileChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid? PetId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid RecordVersion { get; set; }
    public string Action { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAtUtc { get; set; }
}
