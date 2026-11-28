using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Reports;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Reports;

public sealed class OperationsReports(LocalDbContext db) : IOperationsReports
{
    public async Task<OperationsReport> ReadAsync(DateTime fromUtc, DateTime untilUtc, CancellationToken token)
    {
        var retail = db.Set<RetailSale>().AsNoTracking();
        var sales = await ReadRetailTotalsAsync(
            retail.Where(x => x.RecordedAtUtc >= fromUtc && x.RecordedAtUtc < untilUtc), token);
        var refunds = await ReadRetailTotalsAsync(
            retail.Where(x => x.VoidedAtUtc >= fromUtc && x.VoidedAtUtc < untilUtc), token);
        var bills = db.Set<GroomingBill>().Where(x => x.IssuedAtUtc >= fromUtc && x.IssuedAtUtc < untilUtc);
        var paidByBill = from payment in db.Set<GroomingCashPayment>()
                         where payment.RecordedAtUtc < untilUtc
                         group payment by payment.VisitId into payments
                         select new { VisitId = (Guid?)payments.Key, Paid = (decimal?)payments.Sum(x => x.Amount) };
        var balances = from bill in db.Set<GroomingBill>()
                       where bill.IssuedAtUtc < untilUtc
                       join paid in paidByBill on (Guid?)bill.VisitId equals paid.VisitId into matches
                       from paid in matches.DefaultIfEmpty()
                       select bill.Total - bill.HomeDepositAmount - (paid.Paid ?? 0m);
        var cash = db.Set<GroomingCashPayment>().Where(x => x.RecordedAtUtc >= fromUtc && x.RecordedAtUtc < untilUtc);
        var deposits = db.Set<OffsiteBooking>().Where(x => x.ConfirmedAtUtc >= fromUtc && x.ConfirmedAtUtc < untilUtc);
        var shifts = db.Set<CashDrawerShift>().Where(x => x.ClosedAtUtc >= fromUtc && x.ClosedAtUtc < untilUtc)
            .Select(x => x.CountedCash!.Value - x.ExpectedAtClose!.Value);
        var checkIns = db.Set<GroomingCheckIn>().Where(x => x.RecordedAtUtc >= fromUtc && x.RecordedAtUtc < untilUtc &&
            (x.CancelledAtUtc == null || x.CancelledAtUtc >= untilUtc));
        var pickups = db.Set<GroomingVisit>().Where(x => x.PickedUpAtUtc >= fromUtc && x.PickedUpAtUtc < untilUtc);
        var arrivals = db.Set<WalkInEntry>().Where(x => x.ArrivedAtUtc >= fromUtc && x.ArrivedAtUtc < untilUtc);
        var called = await db.Set<WalkInEntry>().Where(x => x.CalledAtUtc >= fromUtc && x.CalledAtUtc < untilUtc)
            .Select(x => new { x.ArrivedAtUtc, x.CalledAtUtc }).ToListAsync(token);
        var appointments = db.Set<GroomingAppointment>().Where(x => x.StartsAtUtc >= fromUtc && x.StartsAtUtc < untilUtc);
        var movements = db.InventoryMovements.Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < untilUtc);

        return new(
            sales.Sum(x => x.Count), sales.Sum(x => x.Gross),
            refunds.Sum(x => x.Count), refunds.Sum(x => x.Gross),
            await bills.CountAsync(token), await bills.SumAsync(x => x.Total, token),
            await cash.SumAsync(x => x.Amount, token), await deposits.SumAsync(x => x.VerifiedDeposit ?? 0, token),
            await balances.Where(x => x > 0).SumAsync(token), await shifts.CountAsync(token),
            -await shifts.Where(x => x < 0).SumAsync(token), await shifts.Where(x => x > 0).SumAsync(token),
            await checkIns.CountAsync(token), await pickups.CountAsync(token), await arrivals.CountAsync(token),
            called.Count, called.Count == 0 ? null : (decimal)called.Average(x => (x.CalledAtUtc!.Value - x.ArrivedAtUtc).TotalMinutes),
            await appointments.CountAsync(token),
            await appointments.CountAsync(x => x.CancelledAtUtc < untilUtc, token), await movements.CountAsync(token),
            sales.Sum(x => x.Vat), refunds.Sum(x => x.Vat),
            sales.Sum(x => x.WithoutVatSnapshot), refunds.Sum(x => x.WithoutVatSnapshot),
            PaymentTotals(RetailPaymentMethod.Cash), PaymentTotals(RetailPaymentMethod.Online));

        RetailPaymentTotals PaymentTotals(RetailPaymentMethod method) => new(
            sales.SingleOrDefault(x => x.Method == method)?.Gross ?? 0m,
            refunds.SingleOrDefault(x => x.Method == method)?.Gross ?? 0m);
    }

    private sealed record RetailTotals(RetailPaymentMethod Method, int Count, decimal Gross, decimal Vat,
        int WithoutVatSnapshot);

    private static Task<List<RetailTotals>> ReadRetailTotalsAsync(IQueryable<RetailSale> entries, CancellationToken token) =>
        entries.GroupBy(x => x.PaymentMethod)
            .Select(group => new RetailTotals(group.Key, group.Count(), group.Sum(x => x.Total),
                group.Sum(x => x.VatAmount ?? 0m), group.Count(x => x.VatAmount == null)))
            .ToListAsync(token);
}
