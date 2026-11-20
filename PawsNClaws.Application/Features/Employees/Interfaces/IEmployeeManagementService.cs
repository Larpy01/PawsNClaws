using PawsNClaws.Application.Features.Employees.DTOs;

namespace PawsNClaws.Application.Features.Employees.Interfaces;

public interface IEmployeeManagementService
{
    Task<IReadOnlyList<EmployeeDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<EmployeeDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, IReadOnlyList<string> Errors)> CreateAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default);


    Task<(bool Succeeded, IReadOnlyList<string> Errors)>
        SetActiveStatusAsync(
            Guid employeeId,
            Guid actingUserId,
            bool isActive,
            CancellationToken cancellationToken = default);

}
