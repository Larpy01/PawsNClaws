namespace PawsNClaws.Application.Features.Employees.DTOs;

public sealed record EmployeeAuditLogPageDto(
    IReadOnlyList<EmployeeAuditLogDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
