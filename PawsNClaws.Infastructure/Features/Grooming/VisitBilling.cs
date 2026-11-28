using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Features.Inventory;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class VisitBilling(LocalDbContext db) : IVisitBilling
{
    public async Task<IReadOnlyList<VisitBillSummary>> ListAsync(int page, CancellationToken token)
    {
        var rows = await db.Set<GroomingBill>().AsNoTracking()
            .Select(x => new { x.VisitId, x.EstimateSnapshot, x.IssuedAtUtc, x.Total, Paid = x.HomeDepositAmount + x.Payments.Sum(p => p.Amount) })
            .OrderByDescending(x => x.Total > x.Paid).ThenByDescending(x => x.IssuedAtUtc).ThenBy(x => x.VisitId)
            .Skip((page - 1) * 25).Take(26).ToListAsync(token);
        return rows.Select(x =>
        {
            var estimate = JsonSerializer.Deserialize<QuoteSnapshot>(x.EstimateSnapshot)!;
            return new VisitBillSummary(x.VisitId, estimate.Pet, estimate.Owner, x.IssuedAtUtc, x.Total, x.Paid);
        }).ToArray();
    }

    public async Task<VisitBillDetails?> FindAsync(Guid visitId, CancellationToken token)
    {
        var bill = await db.Set<GroomingBill>().AsNoTracking().Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.VisitId == visitId, token);
        return bill is null ? null : new(bill.VisitId, bill.EstimateVersion, bill.Revision,
            JsonSerializer.Deserialize<QuoteSnapshot>(bill.EstimateSnapshot)!, bill.Total,
            bill.Payments.OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.Id)
                .Select(x => new VisitCashEntry(x.Id, x.Amount, x.Tendered, x.RecordedAtUtc)).ToArray(), bill.HomeDepositAmount, bill.HomeDepositReference);
    }

    public async Task<VisitCashPrintout?> FindPaymentAsync(Guid visitId, Guid paymentId, CancellationToken token)
    {
        var row = await (from payment in db.Set<GroomingCashPayment>().AsNoTracking()
                         join bill in db.Set<GroomingBill>() on payment.VisitId equals bill.VisitId
                         where payment.Id == paymentId && bill.VisitId == visitId
                         select new { bill.EstimateVersion, bill.EstimateSnapshot, payment.Id, payment.Amount,
                             payment.Tendered, payment.RecordedAtUtc }).SingleOrDefaultAsync(token);
        if (row is null) return null;
        var estimate = JsonSerializer.Deserialize<QuoteSnapshot>(row.EstimateSnapshot)!;
        return new(visitId, row.EstimateVersion, estimate.Pet, estimate.Owner,
            new VisitCashEntry(row.Id, row.Amount, row.Tendered, row.RecordedAtUtc));
    }

    public Task<string?> IssueAsync(Guid visitId, Guid employeeId, CancellationToken token) => WriteAsync(async () =>
    {
        if (await db.Set<GroomingBill>().AnyAsync(x => x.VisitId == visitId, token)) return null;
        var visit = await db.Set<GroomingVisit>().SingleOrDefaultAsync(x => x.Id == visitId, token);
        if (visit is null || !await db.Set<GroomingCheckIn>()
                .AnyAsync(x => x.VisitId == visitId && x.CancelledAtUtc == null, token))
            return "Check in the pet before issuing a bill.";
        var snapshot = JsonSerializer.Deserialize<QuoteSnapshot>(visit.EstimateSnapshot)!;
        if (!IsMoney(snapshot.Total)) return "Review the saved estimate total before issuing a bill.";
        var deposits = await db.Set<OffsiteBooking>().AsNoTracking()
            .Where(x => x.EstimateId == visit.EstimateId && x.Kind == OffsiteKind.HomeService && x.VerifiedDeposit > 0)
            .Take(2).ToListAsync(token);
        var deposit = deposits.Count == 1 ? deposits[0] : null;
        if (deposits.Count > 1 || (deposit is not null &&
            (deposit.Status != OffsiteStatus.Confirmed || deposit.EstimateVersion != visit.EstimateVersion ||
             deposit.VerifiedDeposit is not decimal verified || !IsMoney(verified) || verified > snapshot.Total ||
             string.IsNullOrWhiteSpace(deposit.DepositReference))))
            return "Reconcile the verified home-service deposit before issuing this bill.";

        db.Add(new GroomingBill
        {
            VisitId = visitId, EstimateVersion = visit.EstimateVersion, Total = snapshot.Total,
            HomeDepositBookingId = deposit?.Id, HomeDepositAmount = deposit?.VerifiedDeposit ?? 0,
            HomeDepositReference = deposit?.DepositReference,
            EstimateSnapshot = visit.EstimateSnapshot, IssuedAtUtc = DateTime.UtcNow, IssuedByEmployeeId = employeeId
        });
        visit.RevisionToken = Guid.NewGuid();
        return null;
    }, token);

    public Task<string?> RecordCashAsync(Guid visitId, Guid revision, Guid operationId, decimal amount, decimal tendered,
        Guid employeeId, CancellationToken token) => WriteAsync(async () =>
    {
        if (!IsMoney(amount) || amount <= 0 || !IsMoney(tendered) || tendered < amount)
            return "Enter a positive payment and cash received at least equal to it, with up to two decimal places.";
        if (operationId == Guid.Empty) return "Reload the bill before recording a payment.";
        var previous = await db.Set<GroomingCashPayment>().FindAsync([operationId], token);
        if (previous is not null)
            return previous.VisitId == visitId && previous.Amount == amount && previous.Tendered == tendered &&
                previous.RecordedByEmployeeId == employeeId ? null : "This payment reference was already used.";
        var bill = await db.Set<GroomingBill>().SingleOrDefaultAsync(x => x.VisitId == visitId, token);
        if (bill is null || bill.Revision != revision)
            return "The bill changed. Review the balance and enter the payment again.";
        var paid = await db.Set<GroomingCashPayment>().Where(x => x.VisitId == visitId).SumAsync(x => x.Amount, token);
        if (amount > bill.Total - bill.HomeDepositAmount - paid)
            return "The payment cannot exceed the outstanding balance.";
        var cashError = await CashDrawer.RecordAsync(db, CashDrawerEntryKind.GroomingPayment, operationId, employeeId, amount, token);
        if (cashError is not null) return cashError;
        db.Add(new GroomingCashPayment
        {
            Id = operationId, VisitId = visitId, Amount = amount, Tendered = tendered,
            RecordedAtUtc = DateTime.UtcNow, RecordedByEmployeeId = employeeId
        });
        bill.Revision = Guid.NewGuid();
        return null;
    }, token);

    private static bool IsMoney(decimal amount) =>
        amount >= 0 && amount < 10_000_000_000_000_000m && decimal.Round(amount, 2) == amount;

    private async Task<string?> WriteAsync(Func<Task<string?>> write, CancellationToken token)
    {
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var error = await write();
            if (error is not null) return error;
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return null;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or SqlException { Number: 1205 or 1222 } ||
            ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 1205 or 1222 } })
        {
            return "Billing changed while you were working. Review the latest bill before trying again.";
        }
    }
}
