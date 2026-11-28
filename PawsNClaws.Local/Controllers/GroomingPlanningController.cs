using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.GroomingCatalogManage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("GroomingPlanning/[action]")]
public sealed class GroomingPlanningController(IGroomingPlanning planning, IGroomingVisits visits) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        ViewData["Targets"] = await planning.TargetsAsync(token);
        ViewData["Products"] = await planning.ProductsAsync(token);
        return View(new PlanningOverview(await planning.PackagesAsync(token), await planning.PricesAsync(token),
            await planning.RecipesAsync(token), await planning.QuotesAsync(null, token)));
    }

    [HttpGet("{id:guid?}")]
    public async Task<IActionResult> Package(Guid? id, CancellationToken token)
    {
        var form = id.HasValue
            ? (await planning.PackagesAsync(token)).FirstOrDefault(x => x.Id == id)?.Form
            : new GroomingPackageForm();
        return form is null ? NotFound() : await PackageEditor(form, token);
    }

    [HttpPost("{id:guid?}")]
    public async Task<IActionResult> Package([FromRoute] Guid? id, GroomingPackageForm form, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            var result = await planning.SavePackageAsync(id, form, token);
            if (result.Error is null)
                return Complete(result);
            ModelState.AddModelError(string.Empty, result.Error);
        }
        return await PackageEditor(form, token);
    }

    private async Task<IActionResult> PackageEditor(GroomingPackageForm form, CancellationToken token)
    {
        ViewData["Options"] = await planning.ServicesAsync(token);
        return View(nameof(Package), form);
    }

    [HttpGet("{id:guid?}")]
    public async Task<IActionResult> Price(Guid? id, CancellationToken token)
    {
        var form = id.HasValue
            ? (await planning.PricesAsync(token)).FirstOrDefault(x => x.Id == id)?.Form
            : new GroomingPriceForm();
        return form is null ? NotFound() : await PriceEditor(form, token);
    }

    [HttpPost("{id:guid?}")]
    public async Task<IActionResult> Price([FromRoute] Guid? id, GroomingPriceForm form, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            var result = await planning.SavePriceAsync(id, form, token);
            if (result.Error is null)
                return Complete(result);
            ModelState.AddModelError(string.Empty, result.Error);
        }
        return await PriceEditor(form, token);
    }

    private async Task<IActionResult> PriceEditor(GroomingPriceForm form, CancellationToken token)
    {
        ViewData["Options"] = await planning.TargetsAsync(token);
        return View(nameof(Price), form);
    }

    [HttpGet("{id:guid?}")]
    public async Task<IActionResult> Recipe(Guid? id, CancellationToken token)
    {
        var form = id.HasValue
            ? (await planning.RecipesAsync(token)).FirstOrDefault(x => x.Id == id)?.Form
            : new GroomingRecipeForm();
        return form is null ? NotFound() : await RecipeEditor(form, token);
    }

    [HttpPost("{id:guid?}")]
    public async Task<IActionResult> Recipe([FromRoute] Guid? id, GroomingRecipeForm form, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            var result = await planning.SaveRecipeAsync(id, form, token);
            if (result.Error is null)
                return Complete(result);
            ModelState.AddModelError(string.Empty, result.Error);
        }
        return await RecipeEditor(form, token);
    }

    private async Task<IActionResult> RecipeEditor(GroomingRecipeForm form, CancellationToken token)
    {
        ViewData["Options"] = await planning.ServicesAsync(token);
        ViewData["Products"] = await planning.ProductsAsync(token);
        return View(nameof(Recipe), form);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> History(Guid id, CancellationToken token)
    {
        var history = await planning.QuotesAsync(id, token);
        if (history.Count == 0)
            return NotFound();
        var visit = await visits.FindByEstimateAsync(id, token);
        ViewData["VisitId"] = visit?.Id;
        ViewData["VisitEstimateVersion"] = visit?.EstimateVersion;
        return View(history);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> PetQuotes(Guid id, CancellationToken token)
    {
        var pet = (await planning.PetsAsync(token)).FirstOrDefault(x => x.Id == id);
        if (pet is null)
            return NotFound();
        ViewData["Pet"] = pet;
        return View(await planning.QuotesAsync(null, token, id));
    }

    [HttpGet("{id:guid?}")]
    public async Task<IActionResult> Quote(Guid? id, Guid? petId, CancellationToken token)
    {
        if (!id.HasValue)
            return await QuoteEditor(new GroomingQuoteForm { PetId = petId }, token);
        var latest = (await planning.QuotesAsync(id, token)).FirstOrDefault();
        if (latest is null)
            return NotFound();
        return await QuoteEditor(new GroomingQuoteForm
        {
            Revision = latest.Number, PetId = latest.PetId, TargetId = latest.Snapshot.Lines[0].TargetId,
            AddOnIds = latest.Snapshot.Lines.Skip(1).Select(x => x.TargetId).ToList(),
            Condition = latest.Snapshot.Condition
        }, token);
    }

    [HttpPost("{id:guid?}")]
    public async Task<IActionResult> Quote([FromRoute] Guid? id, GroomingQuoteForm form, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee))
                return Forbid();
            var result = await planning.SaveQuoteAsync(id, form, employee, token);
            if (result.Id.HasValue)
                return RedirectToAction(nameof(History), new { id = result.Id });
            if (result.Error is null)
                return Complete(result);
            ModelState.AddModelError(string.Empty, result.Error);
        }
        return await QuoteEditor(form, token);
    }

    private async Task<IActionResult> QuoteEditor(GroomingQuoteForm form, CancellationToken token)
    {
        ViewData["Pets"] = await planning.PetsAsync(token);
        ViewData["Options"] = await planning.TargetsAsync(token);
        ViewData["Services"] = await planning.ServicesAsync(token);
        return View("Estimate", form);
    }

    private IActionResult Complete(PlanningResult result)
    {
        if (result.Missing)
            return NotFound();
        if (result.Conflict)
            return Conflict("The record changed or was already saved. Reopen the grooming workspace before retrying.");
        return RedirectToAction(nameof(Index));
    }
}
