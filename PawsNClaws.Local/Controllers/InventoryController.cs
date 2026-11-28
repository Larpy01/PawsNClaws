using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models.Inventory;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.InventoryView)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InventoryController(IInventoryService inventory) : Controller
{
    private const int UploadRequestLimit = 27 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index(int requestPage = 1, int movementPage = 1, int receiptPage = 1,
        CancellationToken cancellationToken = default) =>
        View(await inventory.GetOverviewAsync(User, requestPage, movementPage, receiptPage, cancellationToken));

    [HttpGet]
    [Authorize(Roles = LocalRoles.Admin + "," + LocalRoles.Manager)]
    public async Task<IActionResult> Alerts(CancellationToken cancellationToken) =>
        View(await inventory.GetAlertsAsync(User, cancellationToken));

    [HttpGet]
    [Authorize(Policy = Permissions.InventoryAdjust, Roles = LocalRoles.Admin + "," + LocalRoles.Manager)]
    public async Task<IActionResult> Catalog(Guid id, CancellationToken cancellationToken)
    {
        var settings = await inventory.GetCatalogAsync(User, id, cancellationToken);
        return settings is null ? NotFound() : View(new CatalogForm
        {
            ProductId = settings.ProductId, Unit = settings.Unit, Sellable = settings.Sellable,
            Consumable = settings.Consumable, TracksExpiry = settings.TracksExpiry, Online = settings.Online,
            Category = settings.Category, Barcode = settings.Barcode, Description = settings.Description,
            UnitCost = settings.UnitCost, ReorderLevel = settings.ReorderLevel
        });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.InventoryAdjust, Roles = LocalRoles.Admin + "," + LocalRoles.Manager)]
    public async Task<IActionResult> Catalog(CatalogForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(form);
        try
        {
            await inventory.SetCatalogAsync(User, new(form.ProductId, form.Unit, form.Sellable, form.Consumable,
                form.TracksExpiry, form.Online, form.Category, form.Barcode, form.Description,
                form.UnitCost, form.ReorderLevel), cancellationToken);
            TempData["Success"] = "Inventory settings saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (InventoryRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(form);
        }
    }

    [HttpPost]
    [Authorize(Policy = Permissions.InventoryReceive)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public Task<IActionResult> Receive(ReceivingForm form, CancellationToken cancellationToken) =>
        ExecuteWithPhotosAsync(form.Photos, uploads => inventory.ReceiveAsync(User,
            new(form.OperationId, form.ProductId, form.Supplier, form.Reference, form.Lot, form.ExpiryDate,
                form.Delivered, form.Accepted, form.Rejected, form.UnitCost, form.RejectionReason, form.InputUnit,
                form.SupplierInvoiceVat),
            uploads, cancellationToken));

    [HttpPost]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public Task<IActionResult> Post(MovementForm form, CancellationToken cancellationToken)
    {
        // Retail transactions are posted from the POS integration.
        if (form.Action is InventoryAction.Sale or InventoryAction.ReverseSale)
            return Task.FromResult<IActionResult>(Forbid());
        return ExecuteWithPhotosAsync(form.Photos, uploads => inventory.ExecuteAsync(User,
            new(form.OperationId, form.Action, form.ProductId, form.BatchId, form.RequestId, form.MovementId,
                form.Quantity, form.Category, form.Reason, form.InputUnit), uploads, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> RequestDetails(Guid id, CancellationToken cancellationToken)
    {
        var request = await inventory.GetRequestAsync(User, id, cancellationToken);
        return request is null ? NotFound() : View(request);
    }

    [HttpGet]
    public async Task<IActionResult> Evidence(string source, Guid id, CancellationToken cancellationToken)
    {
        var download = await inventory.OpenEvidenceAsync(User, source, id, cancellationToken);
        if (download is null)
            return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(download.Content, download.ContentType, download.Name);
    }

    private async Task<IActionResult> ExecuteWithPhotosAsync(List<IFormFile> photos,
        Func<IReadOnlyList<EvidenceUpload>, Task> execute)
    {
        if (!ModelState.IsValid || photos.Count > 5)
        {
            TempData["Error"] = "Check the form values and the number of photos.";
            return RedirectToAction(nameof(Index));
        }

        var uploads = new List<EvidenceUpload>();
        try
        {
            foreach (var photo in photos)
                uploads.Add(new EvidenceUpload(photo.FileName, photo.OpenReadStream()));
            await execute(uploads);
            TempData["Success"] = "Inventory operation recorded.";
        }
        catch (InventoryRuleException exception)
        {
            TempData["Error"] = exception.Message;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        finally
        {
            foreach (var upload in uploads)
                await upload.Content.DisposeAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
