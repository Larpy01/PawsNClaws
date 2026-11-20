using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Employees.DTOs;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class EmployeeAuditLogService
    : IEmployeeAuditLogService
{
    private readonly LocalDbContext _db;

    public EmployeeAuditLogService(LocalDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeAuditLogPageDto> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.EmployeeAuditLogs
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        // Avoid integer overflow from an untrusted page parameter.
        long skip = ((long)page - 1) * pageSize;

        if (skip >= totalCount)
        {
            return new EmployeeAuditLogPageDto(
                Array.Empty<EmployeeAuditLogDto>(),
                page,
                pageSize,
                totalCount);
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(x => new EmployeeAuditLogDto(
                x.Id,
                x.ActorUserId,
                x.TargetUserId,
                x.Action,
                x.OldValue,
                x.NewValue,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new EmployeeAuditLogPageDto(
            items,
            page,
            pageSize,
            totalCount);
    }
}
