using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Reports;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.TechnicalDashboardView)]
[Route("system-admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TechnicalDashboardController(IDashboardMetrics metrics) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token) =>
        View(await metrics.TechnicalAsync(DateTime.UtcNow, token));
}
