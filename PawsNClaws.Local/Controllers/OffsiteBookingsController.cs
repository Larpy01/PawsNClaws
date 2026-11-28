using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.GroomingCatalogManage)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("OffsiteBookings")]
public sealed class OffsiteBookingsController(IOffsiteBookings bookings, IGroomingPlanning planning, IGroomingVisits visits) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(bool history, CancellationToken token)
    {
        ViewData["History"] = history;
        if (history)
        {
            ViewData["TransportStaff"] = await bookings.TransportStaffAsync(token);
            ViewData["HomeGroomers"] = await visits.ActiveGroomersAsync(token);
        }
        return View(await bookings.BoardAsync(history, token));
    }

    [HttpGet("New/{estimateId:guid}")]
    public async Task<IActionResult> New(Guid estimateId, int version, CancellationToken token)
    {
        var estimate = await GetEstimateAsync(estimateId, version, token);
        return estimate is null ? NotFound() : View(new OffsiteBookingPage(estimate));
    }

    [HttpPost("New/{estimateId:guid}")]
    public async Task<IActionResult> New(Guid estimateId, int version, OffsiteKind? kind, string? requestedAt,
        string? address, string? phone, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var estimate = await GetEstimateAsync(estimateId, version, token);
        if (estimate is null) return NotFound();
        if (!DateTime.TryParseExact(requestedAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local) || local.Year is < 2000 or >= 9999 || kind is null)
            ModelState.AddModelError(string.Empty, "Choose a request type and a valid date and time.");
        if (ModelState.IsValid && kind.HasValue)
        {
            var request = new OffsiteRequest(kind.Value, PhilippineDateTime.ToUtc(local), address ?? "", phone ?? "");
            var error = await bookings.RequestAsync(estimateId, version, request, employee, token);
            if (error is null)
            {
                TempData["OffsiteSuccess"] = "Request recorded for staff review.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, error);
        }
        return View(new OffsiteBookingPage(estimate, kind, requestedAt, address, phone));
    }

    [HttpPost("{id:guid}/Review")]
    public async Task<IActionResult> Review(Guid id, Guid revision, OffsiteStatus? decision, string? reason,
        CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid && decision.HasValue
            ? await bookings.ReviewAsync(id, revision, decision.Value, reason, employee, token)
            : "Choose a decision and enter a review note.";
        if (error is null) TempData["OffsiteSuccess"] = "Review recorded.";
        else TempData["OffsiteError"] = error;
        return RedirectToAction(nameof(Index), new { history = error is null });
    }

    [HttpPost("{id:guid}/ScheduleTransport")]
    public async Task<IActionResult> ScheduleTransport(Guid id, Guid revision, Guid assignee, string? startsAt,
        int minutes, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid && DateTime.TryParseExact(startsAt, "yyyy-MM-dd'T'HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) && local.Year is >= 2000 and < 9999
            ? await bookings.ScheduleTransportAsync(id, revision, assignee, PhilippineDateTime.ToUtc(local), minutes,
                employee, token)
            : "Choose a staff member, date, time and duration.";
        return TransportResult(error, "Transport scheduled.");
    }

    [HttpPost("{id:guid}/Handover")]
    public async Task<IActionResult> Handover(Guid id, Guid revision, string? person, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid ? await bookings.HandoverAsync(id, revision, person, employee, token)
            : "Review the handover and try again.";
        return TransportResult(error, "Handover recorded.");
    }

    [HttpPost("{id:guid}/CancelTransport")]
    public async Task<IActionResult> CancelTransport(Guid id, Guid revision, string? reason, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid ? await bookings.CancelTransportAsync(id, revision, reason, employee, token)
            : "Review the request and try again.";
        return TransportResult(error, "Transport cancelled.");
    }

    [HttpPost("{id:guid}/HoldHome")]
    public async Task<IActionResult> HoldHome(Guid id, Guid revision, Guid groomerId, int minutes, string? expiresAt,
        CancellationToken token)
    {
        if (!ModelState.IsValid || !DateTime.TryParseExact(expiresAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local) || local.Year is < 2000 or >= 9999)
            return TransportResult("Choose a valid hold expiry.", "");
        return TransportResult(await bookings.HoldHomeAsync(id, revision, groomerId, minutes,
            PhilippineDateTime.ToUtc(local), token), "Home-service slot reserved.");
    }

    [HttpPost("{id:guid}/ConfirmHome")]
    public async Task<IActionResult> ConfirmHome(Guid id, Guid revision, bool accepted, decimal? amount, string? reference,
        CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        if (!ModelState.IsValid || !amount.HasValue)
            return TransportResult("Enter the verified amount.", "");
        return TransportResult(await bookings.ConfirmHomeAsync(id, revision, accepted, amount.Value, reference,
            employee, token), "Home-service booking confirmed.");
    }

    [HttpPost("{id:guid}/CancelHome")]
    public async Task<IActionResult> CancelHome(Guid id, Guid revision, string? reason, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid ? await bookings.CancelHomeAsync(id, revision, reason, employee, token)
            : "Review the request and try again.";
        return TransportResult(error, "Home-service booking cancelled.");
    }

    [HttpPost("{id:guid}/ReviewRefund")]
    public async Task<IActionResult> ReviewRefund(Guid id, Guid revision, string? note, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employee)) return Forbid();
        var error = ModelState.IsValid ? await bookings.ReviewRefundAsync(id, revision, note, employee, token)
            : "Review the request and try again.";
        return TransportResult(error, "Refund review recorded.");
    }

    private IActionResult TransportResult(string? error, string success)
    {
        if (error is null) TempData["OffsiteSuccess"] = success;
        else TempData["OffsiteError"] = error;
        return RedirectToAction(nameof(Index), new { history = true });
    }

    private async Task<QuoteVersion?> GetEstimateAsync(Guid id, int version, CancellationToken token) =>
        (await planning.QuotesAsync(id, token)).SingleOrDefault(x => x.Number == version);
}
