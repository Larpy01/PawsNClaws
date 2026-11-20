using PawsNClaws.Application.Features.Employees.DTOs;

namespace PawsNClaws.Local.Models.Employees;

public sealed class EmployeeListModel
{
    public IReadOnlyList<EmployeeDto> Employees { get; init; }
        = Array.Empty<EmployeeDto>();
}
