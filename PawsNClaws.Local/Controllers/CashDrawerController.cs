using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.PosAccess)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("CashDrawer")]
public sealed class CashDrawerController(ICashDrawer drawer) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken token) => View(await drawer.BoardAsync(User, token));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken token)
    {
        var shift = await drawer.FindAsync(User, id, token);
        return shift is null ? NotFound() : View(shift);
    }

    [HttpPost("Open")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public async Task<IActionResult> Open(decimal? openingCash, CancellationToken token) =>
        Result(ModelState.IsValid && openingCash.HasValue ? await drawer.OpenAsync(User, openingCash.Value, token)
            : "Enter the opening cash.", "Cash shift opened.");

    [HttpPost("Movement")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public async Task<IActionResult> Movement(Guid shiftId, Guid operationId, decimal? amount, string? reason,
        CancellationToken token) =>
        Result(ModelState.IsValid && amount.HasValue
            ? await drawer.MoveAsync(User, shiftId, operationId, amount.Value, reason, token)
            : "Enter the cash movement and its reason.", "Cash movement recorded.");

    [HttpPost("Close")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public async Task<IActionResult> Close(Guid shiftId, decimal? countedCash, CancellationToken token)
    {
        var error = ModelState.IsValid && countedCash.HasValue
            ? await drawer.CloseAsync(User, shiftId, countedCash.Value, token) : "Enter the counted cash.";
        return Result(error, "Cash shift closed.");
    }

    private IActionResult Result(string? error, string message)
    {
        if (error is null) TempData["CashDrawerSuccess"] = message;
        else TempData["CashDrawerError"] = error;
        return RedirectToAction(nameof(Index));
    }
}
