using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.CustomerRecordsManage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("WalkInQueue")]
public sealed class WalkInQueueController(IWalkInQueue queue) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken token) => View(await queue.BoardAsync(token));

    [HttpPost("Add")]
    public Task<IActionResult> Add(Guid petId, CancellationToken token) =>
        ChangeAsync(employee => queue.AddAsync(petId, employee, token), "Pet added to the walk-in queue.");

    [HttpPost("{id:guid}/Call")]
    public Task<IActionResult> Call(Guid id, Guid revision, string? reason, CancellationToken token) =>
        ChangeAsync(employee => queue.CallAsync(id, revision, employee,
            User.IsInRole(LocalRoles.Admin) || User.IsInRole(LocalRoles.Manager), reason, token), "Pet marked as called.");

    [HttpPost("{id:guid}/Cancel")]
    public Task<IActionResult> Cancel(Guid id, Guid revision, string? reason, CancellationToken token) =>
        ChangeAsync(employee => queue.CancelAsync(id, revision, employee, reason, token), "Pet removed from the queue.");

    private async Task<IActionResult> ChangeAsync(Func<Guid, Task<string?>> action, string success)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid ? await action(employee) : "Review the queue entry and try again.";
        if (error is null) TempData["QueueSuccess"] = success;
        else TempData["QueueError"] = error;
        return RedirectToAction(nameof(Index));
    }
}
