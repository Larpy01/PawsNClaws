
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.DashboardView)]
public sealed class DashboardController : Controller
{
    private readonly UserManager<LocalApplicationUser> _users;

    public DashboardController(
        UserManager<LocalApplicationUser> users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _users.GetUserAsync(User);

        if (user is null || !user.IsActive)
            return Challenge();

        var roles = await _users.GetRolesAsync(user);

        var model = new DashboardViewModel
        {
            EmployeeName =
                $"{user.FirstName} {user.LastName}".Trim(),
            Roles = roles.ToArray(),
            CurrentDateUtc = DateTime.UtcNow
        };

        return View(model);
    }
}
