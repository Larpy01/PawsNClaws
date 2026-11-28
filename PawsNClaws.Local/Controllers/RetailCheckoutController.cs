using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.PosAccess)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("RetailCheckout")]
public sealed class RetailCheckoutController(IRetailCheckout checkout) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public Task<IActionResult> Index(CancellationToken token) =>
        CheckoutViewAsync(Guid.NewGuid(), [], null, RetailPaymentMethod.Cash, null, token);

    [HttpPost("")]
    [Authorize(Policy = Permissions.PosCreateSale)]
    public async Task<IActionResult> Index(Guid operationId, List<RetailLineInput> lines, decimal? tendered,
        decimal? expectedTotal, RetailPaymentMethod paymentMethod, string? onlinePaymentReference,
        CancellationToken token)
    {
        var selected = lines.Where(x => x.ProductId.HasValue || x.Quantity.HasValue).ToArray();
        if (!ModelState.IsValid || tendered is null)
            ModelState.AddModelError(string.Empty, "Review the selected products, quantities, and payment details.");
        else
        {
            try
            {
                await checkout.CheckoutAsync(User, operationId, selected, tendered.Value, expectedTotal,
                    paymentMethod, onlinePaymentReference, token);
                return RedirectToAction(nameof(Details), new { id = operationId });
            }
            catch (InventoryRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
        return await CheckoutViewAsync(operationId, selected.Take(10).ToArray(), tendered, paymentMethod,
            onlinePaymentReference, token);
    }

    [HttpGet("Sales")]
    public async Task<IActionResult> Sales(int page, CancellationToken token)
    {
        if (!ModelState.IsValid || page > 10_000) return BadRequest();
        page = Math.Max(1, page);
        ViewData["Page"] = page;
        return View(await checkout.ListSalesAsync(User, page, token));
    }

    [HttpPost("{id:guid}/Void")]
    [Authorize(Policy = Permissions.PosVoidSale, Roles = LocalRoles.Admin + "," + LocalRoles.Manager)]
    public async Task<IActionResult> Void(Guid id, string? reason, bool stockVerified, bool cashReturned,
        bool onlineReturned, bool quarantineReturned, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await checkout.VoidSaleAsync(User, id, reason, stockVerified, cashReturned, onlineReturned,
                    quarantineReturned, token);
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (InventoryRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
        else ModelState.AddModelError(string.Empty, "Review the refund details and try again.");

        var sale = await checkout.FindSaleAsync(User, id, token);
        if (sale is null) return NotFound();
        ViewData["RefundReason"] = reason?.Length <= 500 ? reason : null;
        ViewData["QuarantineReturned"] = quarantineReturned;
        return View(nameof(Details), sale);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken token)
    {
        var sale = await checkout.FindSaleAsync(User, id, token);
        return sale is null ? NotFound() : View(sale);
    }

    private async Task<IActionResult> CheckoutViewAsync(Guid operationId, RetailLineInput[] lines,
        decimal? tendered, RetailPaymentMethod paymentMethod, string? onlinePaymentReference, CancellationToken token)
    {
        ViewData["OperationId"] = operationId == Guid.Empty ? Guid.NewGuid() : operationId;
        ViewData["Lines"] = lines;
        ViewData["Tendered"] = tendered;
        ViewData["OnlinePayment"] = paymentMethod == RetailPaymentMethod.Online;
        ViewData["OnlinePaymentReference"] = onlinePaymentReference?.Length <= 80 ? onlinePaymentReference : null;
        return View(nameof(Index), await checkout.ProductsAsync(User, token));
    }
}
