using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.PosAccess)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("VisitBilling")]
public sealed class VisitBillingController(IVisitBilling billing) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page, CancellationToken token)
    {
        if (!ModelState.IsValid || page > 10_000) return BadRequest();
        page = Math.Max(1, page);
        ViewData["Page"] = page;
        return View(await billing.ListAsync(page, token));
    }

    [HttpGet("{visitId:guid}")]
    public async Task<IActionResult> Details(Guid visitId, CancellationToken token)
    {
        var bill = await billing.FindAsync(visitId, token);
        return bill is null ? NotFound() : View(bill);
    }

    [HttpGet("{visitId:guid}/Cash/{paymentId:guid}/Print")]
    public async Task<IActionResult> PrintPayment(Guid visitId, Guid paymentId, CancellationToken token)
    {
        var payment = await billing.FindPaymentAsync(visitId, paymentId, token);
        return payment is null ? NotFound() : View(payment);
    }

    [HttpPost("{visitId:guid}/Issue")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public async Task<IActionResult> Issue(Guid visitId, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = await billing.IssueAsync(visitId, employee, token);
        if (error is null) return RedirectToAction(nameof(Details), new { visitId });
        TempData["BillingError"] = error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{visitId:guid}/Cash")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public async Task<IActionResult> Cash(Guid visitId, Guid revision, Guid operationId, decimal? amount, decimal? tendered,
        CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = !ModelState.IsValid || amount is null || tendered is null
            ? "Enter both the payment amount and cash received."
            : await billing.RecordCashAsync(visitId, revision, operationId, amount.Value, tendered.Value, employee, token);
        if (error is null) return RedirectToAction(nameof(Details), new { visitId });
        var bill = await billing.FindAsync(visitId, token);
        if (bill is null) return NotFound();
        ModelState.AddModelError(string.Empty, error);
        if (revision == bill.Revision)
        {
            ViewData["PaymentAmount"] = amount;
            ViewData["CashTendered"] = tendered;
        }
        return View(nameof(Details), bill);
    }
}
