using PawsNClaws.Application.Features.Employees.DTOs;

namespace PawsNClaws.Application.Features.Employees.Interfaces;

public interface IEmployeeAuditLogService
{
    Task<EmployeeAuditLogPageDto> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
