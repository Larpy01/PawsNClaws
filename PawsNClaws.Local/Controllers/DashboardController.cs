
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Reports;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.DashboardView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController : Controller
{
    private readonly UserManager<LocalApplicationUser> _users;
    private readonly IDashboardMetrics _metrics;
    private readonly IAuthorizationService _authorization;

    public DashboardController(UserManager<LocalApplicationUser> users, IDashboardMetrics metrics,
        IAuthorizationService authorization)
    {
        _users = users;
        _metrics = metrics;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var user = await _users.GetUserAsync(User);

        if (user is null || !user.IsActive)
            return Challenge();

        var showOperations = (User.IsInRole(LocalRoles.Admin) || User.IsInRole(LocalRoles.Manager)) &&
            (await _authorization.AuthorizeAsync(User, Permissions.ReportsView)).Succeeded;
        return View(new DashboardViewModel
        {
            EmployeeName = $"{user.FirstName} {user.LastName}".Trim(),
            Roles = (await _users.GetRolesAsync(user)).ToArray(),
            Operations = showOperations ? await _metrics.OperationsAsync(DateTime.UtcNow, token) : null
        });
    }
}
