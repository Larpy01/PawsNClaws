namespace PawsNClaws.Application.Features.Employees.DTOs;

public sealed record EmployeeDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    IReadOnlyList<string> Roles);
