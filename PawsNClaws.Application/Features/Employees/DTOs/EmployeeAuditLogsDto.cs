namespace PawsNClaws.Application.Features.Employees.DTOs;

public sealed record EmployeeAuditLogDto(
    Guid Id,
    Guid ActorUserId,
    Guid TargetUserId,
    string Action,
    string? OldValue,
    string? NewValue,
    DateTime CreatedAtUtc);
