using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Roles = LocalRoles.Admin)]
[Authorize(Policy = Permissions.UsersView)]
public sealed class EmployeeAuditLogsController : Controller
{
    private readonly IEmployeeAuditLogService _auditLogs;

    public EmployeeAuditLogsController(
        IEmployeeAuditLogService auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
            return BadRequest();

        var result = await _auditLogs.GetPageAsync(
            page,
            25,
            cancellationToken);

        return View(result);
    }
}
