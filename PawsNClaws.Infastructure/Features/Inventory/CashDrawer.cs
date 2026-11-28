using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed class CashDrawer(LocalDbContext db) : ICashDrawer
{
    public async Task<CashDrawerBoard> BoardAsync(ClaimsPrincipal actor, CancellationToken token)
    {
        InventoryAccess.Demand(actor, Permissions.PosAccess);
        var shifts = db.Set<CashDrawerShift>().AsNoTracking();
        var active = await shifts.SingleOrDefaultAsync(x => x.ClosedAtUtc == null, token);
        var recent = await shifts.Where(x => x.ClosedAtUtc != null).OrderByDescending(x => x.ClosedAtUtc)
            .ThenBy(x => x.Id).Take(25).ToListAsync(token);
        return new(active, active is null ? 0 : await ExpectedAsync(db, active, token), recent);
    }

    public async Task<CashShiftDetails?> FindAsync(ClaimsPrincipal actor, Guid id, CancellationToken token)
    {
        InventoryAccess.Demand(actor, Permissions.PosAccess);
        var shift = await db.Set<CashDrawerShift>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (shift is null) return null;
        var entries = await db.Set<CashDrawerEntry>().AsNoTracking().Where(x => x.ShiftId == id)
            .OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.Id).ToListAsync(token);
        return new(shift, shift.ExpectedAtClose ?? shift.OpeningCash + entries.Sum(x => x.Amount), entries);
    }

    public Task<string?> OpenAsync(ClaimsPrincipal actor, decimal openingCash, CancellationToken token)
    {
        var employee = InventoryAccess.Demand(actor, Permissions.PosCreateSale);
        return WriteAsync(async () =>
        {
            if (!ValidMoney(openingCash)) return "Enter valid opening cash with up to two decimal places.";
            if (await db.Set<CashDrawerShift>().AnyAsync(x => x.ClosedAtUtc == null, token))
                return "A cash shift is already open.";
            db.Add(new CashDrawerShift { OpeningCash = openingCash, OpenedAtUtc = DateTime.UtcNow, OpenedByEmployeeId = employee });
            return null;
        }, token);
    }

    public Task<string?> MoveAsync(ClaimsPrincipal actor, Guid shiftId, Guid operationId, decimal amount,
        string? reason, CancellationToken token)
    {
        var employee = InventoryAccess.Demand(actor, Permissions.PosCreateSale);
        return WriteAsync(async () =>
        {
            if (amount == 0 || !ValidMoney(amount < 0 ? -amount : amount) || operationId == Guid.Empty)
                return "Enter a nonzero cash movement with up to two decimal places.";
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                return "Enter a reason for the cash movement (up to 500 characters).";
            var note = reason.Trim();
            var prior = await db.Set<CashDrawerEntry>().SingleOrDefaultAsync(x =>
                x.Kind == CashDrawerEntryKind.Manual && x.ReferenceId == operationId, token);
            if (prior is not null)
                return prior.ShiftId == shiftId && prior.Amount == amount && prior.Reason == note &&
                    prior.RecordedByEmployeeId == employee ? null : "This cash movement reference is already in use.";
            var shift = await db.Set<CashDrawerShift>().SingleOrDefaultAsync(x => x.Id == shiftId && x.ClosedAtUtc == null, token);
            if (shift is null) return "The shift changed. Open the cash drawer and try again.";
            return await AddEntryAsync(db, shift, CashDrawerEntryKind.Manual, operationId, employee, amount, note, token);
        }, token);
    }

    public Task<string?> CloseAsync(ClaimsPrincipal actor, Guid shiftId, decimal countedCash, CancellationToken token)
    {
        var employee = InventoryAccess.Demand(actor, Permissions.PosCreateSale);
        return WriteAsync(async () =>
        {
            if (!ValidMoney(countedCash)) return "Enter valid counted cash with up to two decimal places.";
            var shift = await db.Set<CashDrawerShift>().SingleOrDefaultAsync(x => x.Id == shiftId && x.ClosedAtUtc == null, token);
            if (shift is null) return "The shift changed. Review the current cash drawer before closing.";
            shift.CountedCash = countedCash;
            shift.ExpectedAtClose = await ExpectedAsync(db, shift, token);
            shift.ClosedAtUtc = DateTime.UtcNow;
            shift.ClosedByEmployeeId = employee;
            return null;
        }, token);
    }

    // Called within the same transaction as its source payment or sale.
    internal static async Task<string?> RecordAsync(LocalDbContext context, CashDrawerEntryKind kind, Guid referenceId,
        Guid employee, decimal amount, CancellationToken token)
    {
        if (amount == 0) return null;
        var shifts = context.Set<CashDrawerShift>();
        var shift = await shifts.SingleOrDefaultAsync(x => x.ClosedAtUtc == null, token);
        if (shift is null)
            return await shifts.AnyAsync(token) ? "Open a cash shift before recording cash." : null;
        return await AddEntryAsync(context, shift, kind, referenceId, employee, amount, null, token);
    }

    private static async Task<string?> AddEntryAsync(LocalDbContext context, CashDrawerShift shift, CashDrawerEntryKind kind,
        Guid referenceId, Guid employee, decimal amount, string? reason, CancellationToken token)
    {
        var next = await ExpectedAsync(context, shift, token) + amount;
        if (!ValidMoney(next))
            return next < 0 ? "The expected cash in the drawer is too low for this payout." : "The cash drawer balance exceeds the allowed range.";
        context.Add(new CashDrawerEntry { ShiftId = shift.Id, Kind = kind, ReferenceId = referenceId, Amount = amount,
            RecordedByEmployeeId = employee, RecordedAtUtc = DateTime.UtcNow, Reason = reason });
        return null;
    }

    private static async Task<decimal> ExpectedAsync(LocalDbContext context, CashDrawerShift shift, CancellationToken token) =>
        shift.OpeningCash + await context.Set<CashDrawerEntry>().Where(x => x.ShiftId == shift.Id).SumAsync(x => x.Amount, token);

    private static bool ValidMoney(decimal value) =>
        value >= 0 && value < 10_000_000_000_000_000m && decimal.Round(value, 2) == value;

    private async Task<string?> WriteAsync(Func<Task<string?>> change, CancellationToken token)
    {
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var error = await change();
            if (error is not null) return error;
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return null;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or SqlException { Number: 1205 or 1222 } ||
            ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 1205 or 1222 } })
        {
            return "The cash drawer changed. Review the latest totals before trying again.";
        }
    }
}
