using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.TechnicalDashboardView)]
[Route("system-admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TechnicalDashboardController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
