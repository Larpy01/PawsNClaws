using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Features.Inventory;
using PawsNClaws.Infastructure.Features.Notifications;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class GroomingVisits(LocalDbContext db) : IGroomingVisits
{
    public async Task<GroomingJobOrder?> JobOrderAsync(Guid id, CancellationToken token)
    {
        var checklist = await ChecklistAsync(id, token);
        if (checklist is null) return null;
        var contact = await (from estimate in db.Set<GroomingQuote>()
                             join pet in db.Set<PetRecord>() on estimate.PetId equals pet.Id
                             join owner in db.Set<CustomerRecord>() on pet.CustomerId equals owner.Id
                             where estimate.Id == checklist.Visit.EstimateId
                             select new { owner.Phone, owner.Address }).SingleOrDefaultAsync(token);
        var checkIn = await FindCheckInAsync(id, token);
        return checkIn is null ? null : new(checklist, contact?.Phone, contact?.Address, checkIn.Assessment.OwnerInstructions);
    }

    public async Task<bool> RecordPickupAsync(Guid id, Guid revision, Guid employee, string collectedBy, CancellationToken token)
    {
        var visit = await db.Set<GroomingVisit>().SingleOrDefaultAsync(x => x.Id == id, token);
        if (visit is null || visit.PickedUpAtUtc.HasValue || visit.RevisionToken != revision ||
            string.IsNullOrWhiteSpace(collectedBy) || collectedBy.Trim().Length > 150)
            return false;
        var checklist = await ChecklistAsync(id, token);
        if (checklist is null || checklist.Tasks.Count == 0 || checklist.Tasks.Any(x => !x.Completed.HasValue))
            return false;
        visit.CollectedBy = collectedBy.Trim();
        visit.PickedUpAtUtc = DateTime.UtcNow;
        visit.PickupRecordedByEmployeeId = employee;
        visit.RevisionToken = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(token);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ChecklistGroomer>> ActiveGroomersAsync(CancellationToken token) =>
        await (from user in db.Users
               join membership in db.UserRoles on user.Id equals membership.UserId
               join role in db.Roles on membership.RoleId equals role.Id
               where user.IsActive && role.Name == Identity.Local.LocalRoles.Groomer
               orderby user.LastName, user.FirstName, user.Id
               select new ChecklistGroomer(user.Id, user.FirstName + " " + user.LastName)).ToListAsync(token);

    public async Task<VisitChecklist?> ChecklistAsync(Guid id, CancellationToken token)
    {
        var visit = await FindAsync(id, token);
        var checkIn = await db.Set<GroomingCheckIn>().Where(x => x.VisitId == id && x.CancelledAtUtc == null)
            .Select(x => new { x.TaskChecklist }).SingleOrDefaultAsync(token);
        return visit is null || checkIn is null ? null : new VisitChecklist(visit,
            JsonSerializer.Deserialize<GroomingTask[]>(checkIn.TaskChecklist ?? "[]")!);
    }

    public async Task<AssignmentResult> AssignAsync(Guid id, Guid revision, IReadOnlyList<IReadOnlyList<Guid>> assignments, CancellationToken token)
    {
        var visit = await db.Set<GroomingVisit>().SingleOrDefaultAsync(x => x.Id == id, token);
        if (visit is null || visit.PickedUpAtUtc.HasValue || visit.RevisionToken != revision)
            return AssignmentResult.VisitChanged;
        var checklist = await ChecklistAsync(id, token);
        if (checklist is null)
            return AssignmentResult.VisitChanged;
        var services = checklist.Services;
        var available = (await ActiveGroomersAsync(token)).ToDictionary(x => x.Id);
        if (services.Length == 0 || services.Length != assignments.Count)
            return AssignmentResult.InvalidSelection;
        var tasks = new GroomingTask[services.Length];
        for (var index = 0; index < services.Length; index++)
        {
            var selected = assignments[index].Distinct().Order().ToArray();
            if (selected.Length == 0)
                return AssignmentResult.InvalidSelection;
            var existing = checklist.Tasks.ElementAtOrDefault(index);
            var sameTeam = existing is not null &&
                existing.AssignedGroomers.Select(x => x.Id).Order().SequenceEqual(selected);
            if (existing?.Started is not null)
            {
                if (!sameTeam) return AssignmentResult.InvalidSelection;
                tasks[index] = existing;
                continue;
            }
            if (selected.Any(id => !available.ContainsKey(id)))
                return AssignmentResult.InvalidSelection;
            tasks[index] = sameTeam ? existing! : new GroomingTask(services[index], available[selected[0]],
                AdditionalGroomers: selected.Skip(1).Select(id => available[id]).ToArray());
        }
        var checkInId = await db.Set<GroomingCheckIn>().Where(x => x.VisitId == id && x.CancelledAtUtc == null)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);
        if (checkInId is null)
            return AssignmentResult.VisitChanged;
        var checkIn = db.Attach(new GroomingCheckIn { Id = checkInId.Value });
        checkIn.Property(x => x.TaskChecklist).CurrentValue = JsonSerializer.Serialize(tasks);
        visit.RevisionToken = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(token);
            return AssignmentResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return AssignmentResult.VisitChanged;
        }
    }

    public async Task<IReadOnlyList<AssignedTask>> MyTasksAsync(Guid employee, CancellationToken token)
    {
        var rows = await (from check in db.Set<GroomingCheckIn>()
                          join visit in db.Set<GroomingVisit>() on check.VisitId equals visit.Id
                          where check.CancelledAtUtc == null && check.TaskChecklist != null
                          orderby check.RecordedAtUtc descending
                          select new { visit.Id, visit.RevisionToken, visit.EstimateSnapshot, check.TaskChecklist }).ToListAsync(token);
        var result = new List<AssignedTask>();
        foreach (var row in rows)
        {
            var tasks = JsonSerializer.Deserialize<GroomingTask[]>(row.TaskChecklist!)!;
            for (var i = 0; i < tasks.Length; i++)
                if (tasks[i].AssignedGroomers.Any(groomer => groomer.Id == employee))
                    result.Add(new(row.Id, row.RevisionToken, i,
                        JsonSerializer.Deserialize<QuoteSnapshot>(row.EstimateSnapshot)!.Pet, tasks[i]));
        }
        return result;
    }

    public Task<bool> UpdateTaskAsync(Guid employee, Guid id, Guid revision, int index, bool complete,
        string? observations, string? aftercare, CancellationToken token) =>
        UpdateTaskProgressAsync(employee, id, revision, index, complete, observations, aftercare, false, token);

    public Task<bool> UpdateManagedTaskAsync(Guid employee, Guid id, Guid revision, int index, bool complete,
        string? observations, string? aftercare, CancellationToken token) =>
        UpdateTaskProgressAsync(employee, id, revision, index, complete, observations, aftercare, true, token);

    private async Task<bool> UpdateTaskProgressAsync(Guid employee, Guid id, Guid revision, int index, bool complete,
        string? observations, string? aftercare, bool onBehalf, CancellationToken token)
    {
        var visit = await db.Set<GroomingVisit>().SingleOrDefaultAsync(x => x.Id == id, token);
        if (visit is null || visit.PickedUpAtUtc.HasValue || visit.RevisionToken != revision || observations?.Length > 1000 || aftercare?.Length > 1000)
            return false;
        var row = await db.Set<GroomingCheckIn>().Where(x => x.VisitId == id && x.CancelledAtUtc == null)
            .Select(x => new { x.Id, x.TaskChecklist }).SingleOrDefaultAsync(token);
        if (row?.TaskChecklist is null) return false;
        var tasks = JsonSerializer.Deserialize<GroomingTask[]>(row.TaskChecklist)!;
        if (index < 0 || index >= tasks.Length) return false;
        var task = tasks[index];
        if ((!onBehalf && !task.AssignedGroomers.Any(groomer => groomer.Id == employee)) || task.Completed.HasValue || complete != task.Started.HasValue)
            return false;
        tasks[index] = complete
            ? task with
            {
                Completed = DateTime.UtcNow, CompletedByEmployeeId = employee,
                Observations = observations?.Trim(), Aftercare = aftercare?.Trim()
            }
            : task with { Started = DateTime.UtcNow, StartedByEmployeeId = employee };
        db.Attach(new GroomingCheckIn { Id = row.Id }).Property(x => x.TaskChecklist).CurrentValue = JsonSerializer.Serialize(tasks);
        visit.RevisionToken = Guid.NewGuid();
        if (complete && tasks.All(x => x.Completed.HasValue))
            SmsOutbox.Enqueue(db, SmsNotificationKind.ReadyForPickup, row.Id);
        try { await db.SaveChangesAsync(token); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    private const int MaxWaiverBytes = 5 * 1024 * 1024;

    public async Task<GroomingCheckInDetails?> FindCheckInAsync(Guid visitId, CancellationToken token)
    {
        var checkIn = await db.Set<GroomingCheckIn>().Where(x => x.VisitId == visitId && x.CancelledAtUtc == null)
            .Select(x => new { x.Assessment, x.RecordedAtUtc }).SingleOrDefaultAsync(token);
        return checkIn is null ? null : new(JsonSerializer.Deserialize<GroomingCheckInForm>(checkIn.Assessment)!,
            checkIn.RecordedAtUtc);
    }

    public Task<GroomingWaiver?> FindWaiverAsync(Guid visitId, Guid? checkInId, CancellationToken token) =>
        db.Set<GroomingCheckIn>().Where(x => x.VisitId == visitId &&
            (checkInId.HasValue ? x.Id == checkInId : x.CancelledAtUtc == null))
            .Select(x => new GroomingWaiver(x.WaiverBytes, x.WaiverContentType)).SingleOrDefaultAsync(token);

    public async Task<IReadOnlyList<CancelledGroomingCheckIn>> FindCancelledCheckInsAsync(Guid visitId, CancellationToken token)
    {
        var rows = await db.Set<GroomingCheckIn>().Where(x => x.VisitId == visitId && x.CancelledAtUtc != null)
            .OrderByDescending(x => x.CancelledAtUtc)
            .Select(x => new { x.Id, x.CancelledEstimateVersion, x.CancelledAtUtc, x.CancellationReason, x.Assessment })
            .ToListAsync(token);
        return rows.Select(x => new CancelledGroomingCheckIn(x.Id, x.CancelledEstimateVersion, x.CancelledAtUtc,
            x.CancellationReason, JsonSerializer.Deserialize<GroomingCheckInForm>(x.Assessment)!)).ToArray();
    }

    public async Task<EstimateChangeResult> ChangeEstimateAsync(Guid visitId, Guid expectedRevision, int version,
        Guid employeeId, string? reason, CancellationToken token)
    {
        var visit = await db.Set<GroomingVisit>().SingleOrDefaultAsync(x => x.Id == visitId, token);
        if (visit is null)
            return EstimateChangeResult.VisitMissing;
        if (visit.PickedUpAtUtc.HasValue || visit.RevisionToken != expectedRevision)
            return EstimateChangeResult.Conflict;
        if (await db.Set<GroomingBill>().AnyAsync(x => x.VisitId == visitId, token))
            return EstimateChangeResult.BillIssued;
        if (await db.InventoryMovements.AnyAsync(x => x.GroomingVisitId == visitId, token))
            return EstimateChangeResult.SuppliesPosted;
        if (visit.EstimateVersion == version)
            return EstimateChangeResult.InvalidVersion;
        var snapshot = await db.Set<GroomingQuoteRevision>()
            .Where(x => x.QuoteId == visit.EstimateId && x.Number == version).Select(x => x.Snapshot)
            .SingleOrDefaultAsync(token);
        if (snapshot is null)
            return EstimateChangeResult.InvalidVersion;

        var checkIn = db.Set<GroomingCheckIn>();
        var activeId = await checkIn.Where(x => x.VisitId == visitId && x.CancelledAtUtc == null)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);
        if (activeId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
                return EstimateChangeResult.ReasonRequired;
            var cancelled = new GroomingCheckIn { Id = activeId.Value };
            var entry = db.Attach(cancelled);
            entry.Property(x => x.CancelledAtUtc).CurrentValue = DateTime.UtcNow;
            entry.Property(x => x.CancelledByEmployeeId).CurrentValue = employeeId;
            entry.Property(x => x.CancelledEstimateVersion).CurrentValue = visit.EstimateVersion;
            entry.Property(x => x.CancellationReason).CurrentValue = reason.Trim();
        }

        visit.EstimateVersion = version;
        visit.EstimateSnapshot = snapshot;
        visit.RevisionToken = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(token);
            return EstimateChangeResult.Changed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EstimateChangeResult.Conflict;
        }
    }

    public async Task<CheckInResult> CheckInAsync(Guid visitId, Guid employeeId, GroomingCheckInForm form,
        IReadOnlyCollection<Guid> productIds, Stream waiver, CancellationToken token)
    {
        var visit = await db.Set<GroomingVisit>().SingleOrDefaultAsync(x => x.Id == visitId, token);
        if (visit is null)
            return CheckInResult.VisitMissing;
        if (await db.Set<GroomingCheckIn>().AnyAsync(x => x.VisitId == visitId && x.CancelledAtUtc == null, token))
            return CheckInResult.AlreadyRecorded;
        if (form.VisitRevision != visit.RevisionToken)
            return CheckInResult.VisitChanged;

        var selected = productIds.Distinct().ToArray();
        var products = await db.Products.Where(x => selected.Contains(x.Id) && x.IsActive && x.IsConsumable)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new CheckInProduct(x.Id, x.Name)).ToListAsync(token);
        if (products.Count != selected.Length)
            return CheckInResult.UnavailableProduct;
        form.ProductsUsed = products;

        var buffer = new byte[MaxWaiverBytes + 1];
        var length = await waiver.ReadAtLeastAsync(buffer, buffer.Length, false, token);
        if (length == 0 || length > MaxWaiverBytes)
            return CheckInResult.InvalidWaiver;

        var content = buffer[..length];
        var contentType = IdentifyWaiver(content);
        if (contentType is null)
            return CheckInResult.InvalidWaiver;

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            visit.RevisionToken = Guid.NewGuid();
            var queueEntry = await (from entry in db.Set<WalkInEntry>()
                                    join estimate in db.Set<GroomingQuote>() on entry.PetId equals estimate.PetId
                                    where entry.IsOpen && estimate.Id == visit.EstimateId
                                    select entry).SingleOrDefaultAsync(token);
            if (queueEntry?.Status == WalkInStatus.Waiting)
                return CheckInResult.WaitingInQueue;
            if (queueEntry is not null)
            {
                queueEntry.IsOpen = false;
                queueEntry.Status = WalkInStatus.CheckedIn;
                queueEntry.VisitId = visitId;
                queueEntry.ClosedAtUtc = DateTime.UtcNow;
                queueEntry.ClosedBy = employeeId;
                queueEntry.Revision = Guid.NewGuid();
            }
            db.Add(new GroomingCheckIn
            {
                VisitId = visitId, EmployeeId = employeeId, RecordedAtUtc = DateTime.UtcNow,
                Assessment = JsonSerializer.Serialize(form), WaiverBytes = content, WaiverContentType = contentType,
                WaiverSha256 = Convert.ToHexString(SHA256.HashData(content))
            });
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return CheckInResult.Saved;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return CheckInResult.AlreadyRecorded;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or SqlException { Number: 1205 })
        {
            return CheckInResult.VisitChanged;
        }
    }

    private static string? IdentifyWaiver(byte[] content)
    {
        if (content.AsSpan().StartsWith("%PDF-"u8))
        {
            var end = content.Length;
            while (end > 0 && content[end - 1] is 9 or 10 or 12 or 13 or 32)
                end--;
            return content.AsSpan(0, end).EndsWith("%%EOF"u8) ? "application/pdf" : null;
        }
        using var stream = new MemoryStream(content, writable: false);
        try
        {
            return InventoryEvidenceStore.ValidateImage(stream);
        }
        catch (InventoryRuleException)
        {
            return null;
        }
    }
    public async Task<GroomingVisitDetails?> FindAsync(Guid id, CancellationToken token) =>
        ToDetails(await db.Set<GroomingVisit>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token));

    public async Task<GroomingVisitDetails?> FindByEstimateAsync(Guid estimateId, CancellationToken token) =>
        ToDetails(await db.Set<GroomingVisit>().AsNoTracking().SingleOrDefaultAsync(x => x.EstimateId == estimateId, token));

    public async Task<Guid?> StartAsync(Guid estimateId, int estimateVersion, Guid employeeId, CancellationToken token, Guid? appointmentId = null)
    {
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
            var snapshot = await db.Set<GroomingQuoteRevision>().AsNoTracking()
                .Where(x => x.QuoteId == estimateId && x.Number == estimateVersion)
                .Select(x => x.Snapshot).SingleOrDefaultAsync(token);
            if (snapshot is null)
                return null;

            var existing = await FindExistingIdAsync(estimateId, token);
            if (existing.HasValue)
                return existing;
            var booking = await db.Set<GroomingAppointment>().AsNoTracking()
                .Where(x => x.EstimateId == estimateId && x.CancelledAtUtc == null)
                .Select(x => new { x.Id, x.EstimateVersion }).SingleOrDefaultAsync(token);
            if ((appointmentId.HasValue && booking?.Id != appointmentId) ||
                (booking is not null && booking.EstimateVersion != estimateVersion))
                return null;

            var visit = new GroomingVisit
            {
                EstimateId = estimateId,
                EstimateVersion = estimateVersion,
                EstimateSnapshot = snapshot,
                StartedAtUtc = DateTime.UtcNow,
                StartedByEmployeeId = employeeId
            };
            db.Add(visit);
            await db.SaveChangesAsync(token);
            if (transaction is not null)
                await transaction.CommitAsync(token);
            return visit.Id;
        }
        catch (Exception ex) when (ex is SqlException { Number: 1205 } ||
            ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 1205 } })
        {
            return await FindExistingIdAsync(estimateId, token);
        }
    }

    private Task<Guid?> FindExistingIdAsync(Guid estimateId, CancellationToken token) =>
        db.Set<GroomingVisit>().AsNoTracking().Where(x => x.EstimateId == estimateId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);

    private static GroomingVisitDetails? ToDetails(GroomingVisit? visit) =>
        visit is null ? null : new(visit.Id, visit.EstimateId, visit.EstimateVersion, visit.StartedAtUtc,
            JsonSerializer.Deserialize<QuoteSnapshot>(visit.EstimateSnapshot)!, visit.RevisionToken, visit.PickedUpAtUtc, visit.CollectedBy);
}
