namespace PawsNClaws.Domain.Entities;

public sealed class EmployeeAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ActorUserId { get; set; }

    public Guid TargetUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
