using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Notifications;
using PawsNClaws.Application.Features.Reports;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models;
using PawsNClaws.Local.Reports;

namespace PawsNClaws.Local.Controllers;

[Authorize(Policy = Permissions.ReportsView, Roles = LocalRoles.Admin + "," + LocalRoles.Manager)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Reports/Operations")]
public sealed class OperationsReportsController(IOperationsReports reports, ISmsQueueStatus smsQueue) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? date, bool monthly, CancellationToken token)
    {
        var page = await ReadAsync(date, monthly, token);
        return page is null ? BadRequest("Choose a valid report date.") : View(page);
    }

    [HttpGet("Sms")]
    public async Task<IActionResult> Sms(CancellationToken token) => View(await smsQueue.ListAsync(token));

    [HttpGet("Export")]
    public async Task<IActionResult> Export(DateOnly? date, bool monthly, CancellationToken token)
    {
        var page = await ReadAsync(date, monthly, token);
        if (page is null) return BadRequest("Choose a valid report date.");
        var period = page.Monthly ? $"{page.FirstDay:yyyy-MM}" : $"{page.FirstDay:yyyy-MM-dd}";
        return File(OperationsReportPdf.Render(page), "application/pdf", $"operations-{period}.pdf");
    }

    private async Task<OperationsReportPage?> ReadAsync(DateOnly? date, bool monthly, CancellationToken token)
    {
        var selected = date ?? PhilippineDateTime.Today;
        if (!ModelState.IsValid || selected == DateOnly.MinValue || selected.Year == 9999) return null;
        var first = monthly ? new DateOnly(selected.Year, selected.Month, 1) : selected;
        if (first == DateOnly.MinValue) return null;
        var next = monthly ? first.AddMonths(1) : first.AddDays(1);
        var from = PhilippineDateTime.ToUtc(first.ToDateTime(TimeOnly.MinValue));
        var until = PhilippineDateTime.ToUtc(next.ToDateTime(TimeOnly.MinValue));
        return new(selected, monthly, first, next.AddDays(-1), await reports.ReadAsync(from, until, token));
    }
}
