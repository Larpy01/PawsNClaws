using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Customers;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.CustomerRecordsManage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Customers")]
public sealed class CustomersController(ICustomerRecords records) : Controller
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? query, int page = 1, CancellationToken token = default)
    {
        ViewData["Query"] = query;
        ViewData["Page"] = Math.Clamp(page, 1, 1000000);
        return View(await records.SearchAsync(query, page, token));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken token) =>
        await records.FindAsync(id, token) is { } profile ? View(profile) : NotFound();

    [HttpGet("New")]
    [HttpGet("{id:guid}/Edit")]
    public async Task<IActionResult> Owner([FromRoute] Guid? id, CancellationToken token)
    {
        if (!id.HasValue)
            return View(new CustomerForm());
        return await records.FindAsync(id.Value, token) is { } profile ? View(profile.Details) : NotFound();
    }

    [HttpPost("New")]
    [HttpPost("{id:guid}/Edit")]
    public async Task<IActionResult> Owner([FromRoute] Guid? id, CustomerForm form, CancellationToken token)
    {
        if (!ModelState.IsValid)
            return View(form);
        try
        {
            var saved = await records.SaveCustomerAsync(id, form, Actor, token);
            return saved.HasValue ? RedirectToAction(nameof(Details), new { id = saved }) : NotFound();
        }
        catch (ProfileConflictException)
        {
            return Conflict("Another employee changed this record. Reopen the profile before making corrections.");
        }
    }

    [HttpGet("{ownerId:guid}/Pets/New")]
    [HttpGet("{ownerId:guid}/Pets/{petId:guid}/Edit")]
    public async Task<IActionResult> Pet([FromRoute] Guid ownerId, [FromRoute] Guid? petId, CancellationToken token)
    {
        var owner = await records.FindAsync(ownerId, token);
        if (owner is null)
            return NotFound();
        ViewData["Owner"] = owner.Details.FirstName + " " + owner.Details.LastName;
        if (!petId.HasValue)
            return View(new PetForm());
        var pet = owner.Pets.FirstOrDefault(x => x.Id == petId);
        return pet is null ? NotFound() : View(pet.Details);
    }

    [HttpPost("{ownerId:guid}/Pets/New")]
    [HttpPost("{ownerId:guid}/Pets/{petId:guid}/Edit")]
    public async Task<IActionResult> Pet([FromRoute] Guid ownerId, [FromRoute] Guid? petId, PetForm form, CancellationToken token)
    {
        if (!ModelState.IsValid)
        {
            var owner = await records.FindAsync(ownerId, token);
            if (owner is null)
                return NotFound();
            ViewData["Owner"] = owner.Details.FirstName + " " + owner.Details.LastName;
            return View(form);
        }
        try
        {
            var saved = await records.SavePetAsync(ownerId, petId, form, Actor, token);
            return saved.HasValue ? RedirectToAction(nameof(Details), new { id = ownerId }) : NotFound();
        }
        catch (ProfileConflictException)
        {
            return Conflict("Another employee changed this pet record. Reopen the profile before making corrections.");
        }
    }
}
