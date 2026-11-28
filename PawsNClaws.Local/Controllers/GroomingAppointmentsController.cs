using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.CustomerRecordsManage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("GroomingAppointments")]
public sealed class GroomingAppointmentsController(IGroomingAppointments appointments,
    IGroomingPlanning planning, IGroomingVisits visits) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken token)
    {
        if (!ModelState.IsValid || date == DateOnly.MinValue || date == DateOnly.MaxValue)
            return BadRequest("Choose a valid schedule date.");
        var day = date ?? PhilippineDateTime.Today;
        var from = PhilippineDateTime.ToUtc(day.ToDateTime(TimeOnly.MinValue));
        var until = PhilippineDateTime.ToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return View(new AppointmentBoard(day, await appointments.BoardAsync(from, until, token)));
    }

    [HttpGet("New/{estimateId:guid}")]
    [Authorize(Policy = Permissions.GroomingCatalogManage)]
    public Task<IActionResult> New(Guid estimateId, int version, CancellationToken token) =>
        NewPageAsync(estimateId, version, Guid.Empty, null, null, token);

    [HttpPost("New/{estimateId:guid}")]
    [Authorize(Policy = Permissions.GroomingCatalogManage)]
    public async Task<IActionResult> New(Guid estimateId, int version, Guid groomerId, string? startsAt,
        int? durationMinutes, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        if (!DateTime.TryParseExact(startsAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local) || local.Year is < 2000 or >= 9999)
            ModelState.AddModelError(string.Empty, "Choose a valid appointment date and time.");
        if (durationMinutes is null or < 1 or > 1440)
            ModelState.AddModelError(string.Empty, "Enter a duration between 1 and 1,440 minutes.");

        if (ModelState.IsValid && durationMinutes.HasValue)
        {
            var error = await appointments.ScheduleAsync(estimateId, version, groomerId,
                PhilippineDateTime.ToUtc(local), durationMinutes.Value, employee, token);
            if (error is null)
            {
                TempData["AppointmentSuccess"] = "Appointment scheduled.";
                return RedirectToAction(nameof(Index), new { date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            }
            ModelState.AddModelError(string.Empty, error);
        }
        return await NewPageAsync(estimateId, version, groomerId, startsAt, durationMinutes, token);
    }

    [HttpPost("{id:guid}/Cancel")]
    [Authorize(Policy = Permissions.GroomingCatalogManage)]
    public async Task<IActionResult> Cancel(Guid id, Guid revision, string? reason, DateOnly? date, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Review the appointment and try again.");
        var error = await appointments.CancelAsync(id, revision, employee, reason, token);
        if (error is null) TempData["AppointmentSuccess"] = "Appointment cancelled.";
        else TempData["AppointmentError"] = error;
        return RedirectToAction(nameof(Index), new { date = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    private async Task<IActionResult> NewPageAsync(Guid estimateId, int version, Guid groomer, string? startsAt,
        int? durationMinutes, CancellationToken token)
    {
        var estimate = (await planning.QuotesAsync(estimateId, token)).SingleOrDefault(x => x.Number == version);
        if (estimate is null) return NotFound();
        return View(nameof(New), new NewAppointmentPage(estimate, await visits.ActiveGroomersAsync(token),
            groomer, startsAt, durationMinutes));
    }
}
