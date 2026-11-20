namespace PawsNClaws.Application.Features.Employees.DTOs;

public sealed record ChangeEmployeeRoleRequest(
    Guid EmployeeId,
    Guid ActingUserId,
    string NewRole);
