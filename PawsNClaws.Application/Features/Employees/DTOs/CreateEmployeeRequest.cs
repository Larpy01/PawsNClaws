
namespace PawsNClaws.Application.Features.Employees.DTOs;

public sealed record CreateEmployeeRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Role,
    Guid ActingUserId);
