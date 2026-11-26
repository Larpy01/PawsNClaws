using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.GroomingCatalogManage)]
[AutoValidateAntiforgeryToken]
[Route("GroomingCatalog")]
public sealed class GroomingCatalogController(IGroomingCatalog catalog) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken token) =>
        View(await catalog.ListAsync(token));

    [HttpGet("New", Name = "NewGroomingService")]
    [HttpGet("{id:guid}/Edit", Name = "EditGroomingService")]
    public async Task<IActionResult> Edit([FromRoute] Guid? id, CancellationToken token)
    {
        var form = id.HasValue ? await catalog.FindAsync(id.Value, token) : new GroomingServiceForm();
        return form is null ? NotFound() : View(form);
    }

    [HttpPost("New")]
    [HttpPost("{id:guid}/Edit")]
    public async Task<IActionResult> Edit([FromRoute] Guid? id, GroomingServiceForm form, CancellationToken token)
    {
        if (!ModelState.IsValid)
            return View(form);

        return await catalog.SaveAsync(id, form, token) switch
        {
            CatalogSaveResult.NotFound => NotFound(),
            CatalogSaveResult.Conflict => Conflict("Another employee changed this service. Reopen the form before saving."),
            _ => RedirectToAction(nameof(Index))
        };
    }
}
