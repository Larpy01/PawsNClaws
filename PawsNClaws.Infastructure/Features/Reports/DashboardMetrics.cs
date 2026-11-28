using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Reports;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Reports;

public sealed class DashboardMetrics(LocalDbContext db) : IDashboardMetrics
{
    public async Task<OperationsDashboard> OperationsAsync(DateTime nowUtc, CancellationToken token)
    {
        var (today, startUtc, endUtc) = Week(nowUtc);
        var appointments = await db.Set<GroomingAppointment>()
            .Where(x => x.CancelledAtUtc == null && x.StartsAtUtc >= startUtc && x.StartsAtUtc < endUtc)
            .GroupBy(x => x.StartsAtUtc.AddHours(8).Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Count, token);
        var pickups = await db.Set<GroomingVisit>()
            .Where(x => x.PickedUpAtUtc >= startUtc && x.PickedUpAtUtc < endUtc)
            .GroupBy(x => x.PickedUpAtUtc!.Value.AddHours(8).Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Count, token);
        var sales = await db.Set<RetailSale>()
            .Where(x => x.RecordedAtUtc >= startUtc && x.RecordedAtUtc < endUtc)
            .GroupBy(x => x.PaymentMethod)
            .Select(g => new { Method = g.Key, Total = g.Sum(x => x.Total) })
            .ToDictionaryAsync(x => x.Method, x => x.Total, token);
        var walkIns = await db.Set<WalkInEntry>()
            .CountAsync(x => x.ArrivedAtUtc >= endUtc.AddDays(-1) && x.ArrivedAtUtc < endUtc &&
                x.IsOpen && x.Status == WalkInStatus.Waiting, token);
        var days = Dates(today).Select(date => new DashboardDay(date, appointments.GetValueOrDefault(date),
            pickups.GetValueOrDefault(date))).ToArray();
        return new(days, sales.GetValueOrDefault(RetailPaymentMethod.Cash), sales.GetValueOrDefault(RetailPaymentMethod.Online),
            appointments.GetValueOrDefault(today), pickups.GetValueOrDefault(today), walkIns);
    }

    public async Task<TechnicalDashboard> TechnicalAsync(DateTime nowUtc, CancellationToken token)
    {
        var (today, startUtc, endUtc) = Week(nowUtc);
        var audit = await db.EmployeeAuditLogs.Where(x => x.CreatedAtUtc >= startUtc && x.CreatedAtUtc < endUtc)
            .GroupBy(x => x.CreatedAtUtc.AddHours(8).Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Count, token);
        var accounts = await db.Users.GroupBy(x => x.IsActive).Select(g => new { Active = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Active, x => x.Count, token);
        return new(Dates(today).Select(date => new DashboardDay(date, audit.GetValueOrDefault(date))).ToArray(),
            accounts.GetValueOrDefault(true), accounts.GetValueOrDefault(false));
    }

    private static (DateOnly Today, DateTime StartUtc, DateTime EndUtc) Week(DateTime nowUtc)
    {
        var shopMidnight = nowUtc.AddHours(8).Date;
        return (DateOnly.FromDateTime(shopMidnight), shopMidnight.AddDays(-6).AddHours(-8),
            shopMidnight.AddDays(1).AddHours(-8));
    }

    private static IEnumerable<DateOnly> Dates(DateOnly today) =>
        Enumerable.Range(-6, 7).Select(today.AddDays);
}
