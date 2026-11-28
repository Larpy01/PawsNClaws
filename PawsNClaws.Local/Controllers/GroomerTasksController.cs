using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Roles = LocalRoles.Groomer)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GroomerTasksController(IGroomingVisits visits) : Controller
{
    public async Task<IActionResult> Index(CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        return View(await visits.MyTasksAsync(employee, token));
    }

    [HttpPost]
    public async Task<IActionResult> Update(Guid id, Guid revision, int index, bool complete,
        string? observations, string? aftercare, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        if (!ModelState.IsValid || !await visits.UpdateTaskAsync(employee, id, revision, index, complete, observations, aftercare, token))
            TempData["TaskError"] = "The task could not be updated. Check its current status and try again.";
        return RedirectToAction(nameof(Index));
    }
}
