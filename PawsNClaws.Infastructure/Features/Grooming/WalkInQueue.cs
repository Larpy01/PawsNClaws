using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class WalkInQueue(LocalDbContext db) : IWalkInQueue
{
    public async Task<WalkInBoard> BoardAsync(CancellationToken token)
    {
        var rows = from entry in db.Set<WalkInEntry>().AsNoTracking()
                   join pet in db.Set<PetRecord>() on entry.PetId equals pet.Id
                   join owner in db.Set<CustomerRecord>() on pet.CustomerId equals owner.Id
                   select new { Entry = entry, Pet = pet.Name, Customer = owner.FirstName + " " + owner.LastName };
        // Use the same ordering when checking who is next during a call.
        var open = await rows.Where(x => x.Entry.IsOpen)
            .OrderBy(x => x.Entry.Status).ThenBy(x => x.Entry.ArrivedAtUtc).ThenBy(x => x.Entry.Id).ToListAsync(token);
        var recent = await rows.Where(x => !x.Entry.IsOpen).OrderByDescending(x => x.Entry.ClosedAtUtc)
            .ThenBy(x => x.Entry.Id).Take(25).ToListAsync(token);
        return new(open.Select(x => ToRow(x.Entry, x.Pet, x.Customer)).ToArray(),
            recent.Select(x => ToRow(x.Entry, x.Pet, x.Customer)).ToArray());
    }

    public Task<string?> AddAsync(Guid petId, Guid employee, CancellationToken token) => WriteAsync(async () =>
    {
        if (!await db.Set<PetRecord>().AnyAsync(x => x.Id == petId, token))
            return "The pet could not be found.";
        if (await db.Set<WalkInEntry>().AnyAsync(x => x.PetId == petId && x.IsOpen, token))
            return "This pet is already on the walk-in queue.";
        var checkedIn = await (from visit in db.Set<GroomingVisit>()
                               join estimate in db.Set<GroomingQuote>() on visit.EstimateId equals estimate.Id
                               join checkIn in db.Set<GroomingCheckIn>() on visit.Id equals checkIn.VisitId
                               where estimate.PetId == petId && visit.PickedUpAtUtc == null && checkIn.CancelledAtUtc == null
                               select visit.Id).AnyAsync(token);
        if (checkedIn) return "This pet already has a grooming visit in progress.";
        db.Add(new WalkInEntry { PetId = petId, AddedBy = employee, ArrivedAtUtc = DateTime.UtcNow });
        return null;
    }, token);

    public Task<string?> CallAsync(Guid id, Guid revision, Guid employee, bool canPrioritize, string? reason,
        CancellationToken token) => WriteAsync(async () =>
    {
        var entry = await FindOpenAsync(id, revision, token);
        if (entry?.Status != WalkInStatus.Waiting) return "This entry changed. Review the queue and try again.";
        var next = await db.Set<WalkInEntry>().Where(x => x.IsOpen && x.Status == WalkInStatus.Waiting)
            .OrderBy(x => x.ArrivedAtUtc).ThenBy(x => x.Id).Select(x => x.Id).FirstAsync(token);
        if (entry.Id != next)
        {
            if (!canPrioritize) return "Call the first pet waiting in the queue.";
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                return "Enter a reason for calling this pet out of order (up to 500 characters).";
            entry.PriorityReason = reason.Trim();
        }
        entry.Status = WalkInStatus.Called;
        entry.CalledBy = employee;
        entry.CalledAtUtc = DateTime.UtcNow;
        entry.Revision = Guid.NewGuid();
        return null;
    }, token);

    public Task<string?> CancelAsync(Guid id, Guid revision, Guid employee, string? reason, CancellationToken token) =>
        WriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                return "Enter a reason for removing the pet (up to 500 characters).";
            var entry = await FindOpenAsync(id, revision, token);
            if (entry is null) return "This entry changed. Review the queue and try again.";
            entry.Status = WalkInStatus.Cancelled;
            entry.IsOpen = false;
            entry.ClosedBy = employee;
            entry.ClosedAtUtc = DateTime.UtcNow;
            entry.CancellationReason = reason.Trim();
            entry.Revision = Guid.NewGuid();
            return null;
        }, token);

    private Task<WalkInEntry?> FindOpenAsync(Guid id, Guid revision, CancellationToken token) =>
        db.Set<WalkInEntry>().SingleOrDefaultAsync(x => x.Id == id && x.IsOpen && x.Revision == revision, token);

    private async Task<string?> WriteAsync(Func<Task<string?>> action, CancellationToken token)
    {
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var error = await action();
            if (error is not null) return error;
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return null;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 1205 } } ||
            ex is SqlException { Number: 1205 })
        {
            return "The queue changed while you were working. Review it and try again.";
        }
    }

    private static WalkInRow ToRow(WalkInEntry entry, string pet, string customer) =>
        new(entry.Id, entry.PetId, pet, customer, entry.ArrivedAtUtc, entry.Status, entry.Revision,
            entry.PriorityReason, entry.CancellationReason);
}
