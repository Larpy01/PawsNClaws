using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.GroomingCatalogManage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("GroomingVisits")]
public sealed class GroomingVisitsController(IGroomingVisits visits, IGroomingPlanning planning, IInventoryService inventory) : Controller
{
    [HttpPost("Start")]
    public async Task<IActionResult> Start(Guid estimateId, int version, Guid? appointmentId, CancellationToken token)
    {
        if (!ModelState.IsValid || estimateId == Guid.Empty || version < 1)
            return BadRequest("Choose a price estimate version.");
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
            return Forbid();

        var id = await visits.StartAsync(estimateId, version, employeeId, token, appointmentId);
        return id.HasValue ? RedirectToAction(nameof(Details), new { id }) : NotFound("Could not start the visit. Reopen the price estimate or use the booked version from Grooming appointments.");
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken token)
    {
        var visit = await visits.FindAsync(id, token);
        if (visit is null)
            return NotFound();
        ViewData["CheckIn"] = await visits.FindCheckInAsync(id, token);
        if (ViewData["CheckIn"] is not null)
            ViewData["PickupChecklist"] = await visits.ChecklistAsync(id, token);
        ViewData["EstimateVersions"] = (await planning.QuotesAsync(visit.EstimateId, token))
            .Where(x => x.Number != visit.EstimateVersion).ToArray();
        ViewData["CancelledCheckIns"] = await visits.FindCancelledCheckInsAsync(id, token);
        return View(visit);
    }

    [HttpGet("{id:guid}/Supplies")]
    [Authorize(Policy = Permissions.InventoryUse)]
    public async Task<IActionResult> Supplies(Guid id, CancellationToken token)
    {
        var page = await inventory.GetVisitSuppliesAsync(User, id, token);
        return page is null ? NotFound() : View(page);
    }

    [HttpPost("{id:guid}/Supplies")]
    [Authorize(Policy = Permissions.InventoryUse)]
    public async Task<IActionResult> Supplies(Guid id, Guid revision, Guid productId, decimal? quantity,
        string? reason, CancellationToken token)
    {
        if (!ModelState.IsValid || quantity is null)
            ModelState.AddModelError(string.Empty, "Enter a valid quantity used.");
        else
        {
            try
            {
                await inventory.PostVisitSupplyAsync(User, id, revision, productId, quantity.Value, reason, token);
                TempData["SupplySaved"] = "Usage recorded and deducted from stock.";
                return RedirectToAction(nameof(Supplies), new { id });
            }
            catch (InventoryRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
        var page = await inventory.GetVisitSuppliesAsync(User, id, token);
        ViewData["AttemptedProduct"] = productId;
        ViewData["AttemptedQuantity"] = quantity;
        ViewData["AttemptedReason"] = reason;
        return page is null ? NotFound() : View(page);
    }

    [HttpPost("{id:guid}/Pickup")]
    public async Task<IActionResult> Pickup(Guid id, Guid revision, string? collectedBy, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        if (string.IsNullOrWhiteSpace(collectedBy) || collectedBy.Trim().Length > 150)
            TempData["PickupError"] = "Enter the name of the person collecting the pet (up to 150 characters).";
        else if (!ModelState.IsValid || !await visits.RecordPickupAsync(id, revision, employee, collectedBy, token))
            TempData["PickupError"] = "Pickup could not be saved. Check the latest visit details and task status.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:guid}/JobOrder")]
    public async Task<IActionResult> JobOrder(Guid id, CancellationToken token)
    {
        var order = await visits.JobOrderAsync(id, token);
        return order is null ? NotFound() : View(order);
    }

    [HttpGet("{id:guid}/Tasks")]
    public async Task<IActionResult> Tasks(Guid id, CancellationToken token)
    {
        var checklist = await visits.ChecklistAsync(id, token);
        if (checklist is null) return NotFound();
        ViewData["Groomers"] = await visits.ActiveGroomersAsync(token);
        return View(checklist);
    }

    [HttpPost("{id:guid}/Tasks")]
    public async Task<IActionResult> Tasks(Guid id, Guid revision, List<Guid> groomers, List<Models.GroomingTaskAssignment> assignments,
        CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            IReadOnlyList<Guid>[] teams = assignments.Count > 0
                ? assignments.Select(x => (IReadOnlyList<Guid>)x.GroomerIds).ToArray()
                : groomers.Select(x => (IReadOnlyList<Guid>)new[] { x }).ToArray();
            var result = await visits.AssignAsync(id, revision, teams, token);
            if (result == AssignmentResult.Saved) return RedirectToAction(nameof(Checklist), new { id });
            if (result == AssignmentResult.VisitChanged) return Conflict("This visit changed. Reopen it to assign tasks.");
        }
        TempData["TaskError"] = "Choose at least one active groomer for every service. Tasks already started keep their assigned team.";
        return RedirectToAction(nameof(Tasks), new { id });
    }

    [HttpPost("{id:guid}/TaskProgress")]
    public async Task<IActionResult> TaskProgress(Guid id, Guid revision, int? index, bool? complete,
        string? observations, string? aftercare, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        if (!ModelState.IsValid || index is null || complete is null ||
            !await visits.UpdateManagedTaskAsync(employee, id, revision, index.Value, complete.Value,
                observations, aftercare, token))
        {
            TempData["TaskError"] = "The task could not be updated. Check its current status and try again.";
            TempData["TaskDraftVisit"] = id;
            TempData["TaskDraftRevision"] = revision;
            TempData["TaskDraftIndex"] = index;
            TempData["TaskDraftObservations"] = observations?.Length <= 1000 ? observations : null;
            TempData["TaskDraftAftercare"] = aftercare?.Length <= 1000 ? aftercare : null;
        }
        else TempData["TaskSuccess"] = complete.Value ? "Task marked complete." : "Task started.";
        return RedirectToAction(nameof(Tasks), new { id });
    }

    [HttpGet("{id:guid}/Checklist")]
    public async Task<IActionResult> Checklist(Guid id, CancellationToken token)
    {
        var checklist = await visits.ChecklistAsync(id, token);
        return checklist is null || checklist.Tasks.Count == 0 ? NotFound() : View(checklist);
    }

    [HttpPost("{id:guid}/ChangeEstimate")]
    public async Task<IActionResult> ChangeEstimate(Guid id, Guid? revision, int version, string? reason,
        CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
            return Forbid();
        if (!ModelState.IsValid || revision is null || version < 1)
            return BadRequest("Choose another price estimate version.");

        switch (await visits.ChangeEstimateAsync(id, revision.Value, version, employeeId, reason, token))
        {
            case EstimateChangeResult.Changed:
                return RedirectToAction(nameof(Details), new { id });
            case EstimateChangeResult.VisitMissing:
                return NotFound();
            case EstimateChangeResult.Conflict:
                return Conflict("The visit changed. Reopen it before changing the price estimate.");
            case EstimateChangeResult.BillIssued:
                TempData["EstimateChangeError"] = "A bill has been issued. The visit keeps its billed estimate version.";
                break;
            case EstimateChangeResult.SuppliesPosted:
                TempData["EstimateChangeError"] = "This visit already has stock movements. Its price estimate is locked.";
                break;
            case EstimateChangeResult.ReasonRequired:
                TempData["EstimateChangeError"] = "Enter a reason for cancelling check-in, up to 500 characters.";
                break;
            default:
                TempData["EstimateChangeError"] = "Choose another version of this price estimate.";
                break;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:guid}/CheckIn")]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken token)
    {
        var visit = await visits.FindAsync(id, token);
        if (visit is null)
            return NotFound();
        if (await visits.FindCheckInAsync(id, token) is not null)
            return RedirectToAction(nameof(Details), new { id });
        return await CheckInViewAsync(visit, new GroomingCheckInForm { VisitRevision = visit.RevisionToken }, [], token);
    }

    [HttpPost("{id:guid}/CheckIn")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> CheckIn(Guid id, GroomingCheckInForm form, [FromForm] List<Guid>? productIds,
        IFormFile? waiver, CancellationToken token)
    {
        var visit = await visits.FindAsync(id, token);
        if (visit is null)
            return NotFound();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
            return Forbid();
        if (waiver is null || waiver.Length == 0)
            ModelState.AddModelError(nameof(waiver), "Attach the signed paper waiver.");

        if (ModelState.IsValid && waiver is not null)
        {
            await using var stream = waiver.OpenReadStream();
            var result = await visits.CheckInAsync(id, employeeId, form, productIds ?? [], stream, token);
            switch (result)
            {
                case CheckInResult.Saved:
                case CheckInResult.AlreadyRecorded:
                    return RedirectToAction(nameof(Details), new { id });
                case CheckInResult.VisitMissing:
                    return NotFound();
                case CheckInResult.InvalidWaiver:
                    ModelState.AddModelError(nameof(waiver), "Choose a PDF, JPEG, or PNG up to 5 MB.");
                    break;
                case CheckInResult.VisitChanged:
                    return Conflict("The visit changed. Reopen the check-in form before saving.");
                case CheckInResult.WaitingInQueue:
                    ModelState.AddModelError(string.Empty, "Call this pet from the walk-in queue before saving check-in.");
                    break;
                case CheckInResult.UnavailableProduct:
                    ModelState.AddModelError(nameof(productIds), "A selected supply is no longer available. Check the list and choose again.");
                    break;
            }
        }
        return await CheckInViewAsync(visit, form, productIds ?? [], token);
    }

    private async Task<IActionResult> CheckInViewAsync(GroomingVisitDetails visit, GroomingCheckInForm form,
        IReadOnlyCollection<Guid> productIds, CancellationToken token)
    {
        var products = (await planning.ProductsAsync(token)).Where(x => x.Available).ToArray();
        return View(nameof(CheckIn), new Models.GroomingCheckInPage(visit, form, products, productIds));
    }

    [HttpGet("{id:guid}/Waiver")]
    public async Task<IActionResult> Waiver(Guid id, Guid? checkInId, CancellationToken token)
    {
        if (!ModelState.IsValid)
            return BadRequest();
        var waiver = await visits.FindWaiverAsync(id, checkInId, token);
        if (waiver is null)
            return NotFound();
        var extension = waiver.ContentType switch
        {
            "application/pdf" => "pdf",
            "image/jpeg" => "jpg",
            _ => "png"
        };
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(waiver.Content, waiver.ContentType, $"signed-waiver.{extension}");
    }
}
