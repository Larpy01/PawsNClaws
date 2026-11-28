using System.Text.Json;
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class OffsiteBookings(LocalDbContext db, IGroomingVisits visits, IConfiguration configuration) : IOffsiteBookings
{
    public async Task<IReadOnlyList<ChecklistGroomer>> TransportStaffAsync(CancellationToken token)
    {
        string[] roles = [LocalRoles.Admin, LocalRoles.Manager, LocalRoles.Groomer, LocalRoles.Cashier, LocalRoles.InventoryStaff];
        return await db.Users.Where(user => user.IsActive &&
            (from membership in db.UserRoles
             join role in db.Roles on membership.RoleId equals role.Id
             where membership.UserId == user.Id
             select role.Name).Any(name => roles.Contains(name!)) &&
            !(from membership in db.UserRoles
              join role in db.Roles on membership.RoleId equals role.Id
              where membership.UserId == user.Id
              select role.Name).Any(name => name == LocalRoles.SuperAdmin))
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id)
            .Select(x => new ChecklistGroomer(x.Id, x.FirstName + " " + x.LastName)).ToListAsync(token);
    }

    public Task<string?> HoldHomeAsync(Guid id, Guid revision, Guid groomerId, int minutes, DateTime expiresAtUtc,
        CancellationToken token) => ChangeBookingAsync(id, revision, OffsiteKind.HomeService, async booking =>
    {
        var now = DateTime.UtcNow;
        if (booking.Status != OffsiteStatus.AwaitingDeposit || booking.HoldExpiresAtUtc > now)
            return "Only a request awaiting deposit without an active hold can reserve time.";
        if (minutes is < 1 or > 720 || expiresAtUtc <= now || expiresAtUtc > booking.RequestedAtUtc)
            return "Enter service and travel time and an expiry before the requested start.";
        var rateText = configuration["Grooming:HomeService:DepositPercent"];
        if (!decimal.TryParse(rateText, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var rate) || rate is <= 0 or > 100)
            return "An administrator must configure the approved home-service deposit before reserving a slot.";
        if (!(await visits.ActiveGroomersAsync(token)).Any(x => x.Id == groomerId))
            return "Choose an active groomer.";
        var estimate = await db.Set<GroomingQuoteRevision>().SingleAsync(x =>
            x.QuoteId == booking.EstimateId && x.Number == booking.EstimateVersion, token);
        var ends = booking.RequestedAtUtc.AddMinutes(minutes);
        var petId = await db.Set<GroomingQuote>().Where(x => x.Id == booking.EstimateId)
            .Select(x => x.PetId).SingleAsync(token);
        if (await db.Set<GroomingAppointment>().AnyAsync(x => x.CancelledAtUtc == null &&
            (x.GroomerId == groomerId || x.PetId == petId) && x.StartsAtUtc < ends && x.EndsAtUtc > booking.RequestedAtUtc, token) ||
            await db.Set<OffsiteBooking>().AnyAsync(x => x.TransportEmployeeId == groomerId &&
                (x.Status == OffsiteStatus.Scheduled || x.Status == OffsiteStatus.InCustody) &&
                x.TransportStartUtc < ends && x.TransportEndUtc > booking.RequestedAtUtc, token) ||
            await ActiveHomeSlots(db, now).AnyAsync(x => x.Booking.Id != id &&
                (x.Booking.HomeGroomerId == groomerId || x.PetId == petId) &&
                x.Booking.RequestedAtUtc < ends && x.Booking.HomeEndsAtUtc > booking.RequestedAtUtc, token))
            return "The pet or groomer already has work scheduled during that time.";
        booking.HomeGroomerId = groomerId;
        booking.HomeEndsAtUtc = ends;
        booking.HoldExpiresAtUtc = expiresAtUtc;
        booking.DepositRequired = Math.Round(JsonSerializer.Deserialize<QuoteSnapshot>(estimate.Snapshot)!.Total * rate / 100,
            2, MidpointRounding.AwayFromZero);
        return null;
    }, token);

    public Task<string?> ConfirmHomeAsync(Guid id, Guid revision, bool accepted, decimal amount, string? reference,
        Guid employee, CancellationToken token) => ChangeBookingAsync(id, revision, OffsiteKind.HomeService, async booking =>
    {
        var now = DateTime.UtcNow;
        if (booking.Status != OffsiteStatus.AwaitingDeposit || booking.HoldExpiresAtUtc is not DateTime expiry ||
            expiry <= now || booking.RequestedAtUtc <= now || booking.DepositRequired is not decimal required)
            return "Reserve an active slot before confirming this booking.";
        if (!accepted || amount < required || amount < 0 || amount >= 1_000_000_000_000_000m ||
            decimal.Round(amount, 2) != amount || string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100)
            return "Confirm the customer accepted the estimate and enter the verified deposit and its reference.";
        if (!(await visits.ActiveGroomersAsync(token)).Any(x => x.Id == booking.HomeGroomerId))
            return "The groomer is unavailable. Review the schedule before confirming.";
        if (await (from visit in db.Set<GroomingVisit>()
                   join bill in db.Set<GroomingBill>() on visit.Id equals bill.VisitId
                   where visit.EstimateId == booking.EstimateId
                   select bill.VisitId).AnyAsync(token))
            return "A grooming bill already exists for this estimate. Review it before recording a separate deposit.";
        booking.VerifiedDeposit = amount;
        booking.DepositReference = reference.Trim();
        booking.ConfirmedAtUtc = now;
        booking.ConfirmedBy = employee;
        booking.Status = OffsiteStatus.Confirmed;
        return null;
    }, token);

    public Task<string?> CancelHomeAsync(Guid id, Guid revision, string? reason, Guid employee, CancellationToken token) =>
        ChangeBookingAsync(id, revision, OffsiteKind.HomeService, async booking =>
        {
            if (booking.Status is not (OffsiteStatus.AwaitingDeposit or OffsiteStatus.Confirmed))
                return "Only a pending or confirmed home-service booking can be cancelled.";
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                return "Enter a cancellation reason (up to 500 characters).";
            if (await db.Set<GroomingBill>().AnyAsync(x => x.HomeDepositBookingId == id, token))
                return "The deposit is credited on a grooming bill. Resolve the bill before cancelling this booking.";
            booking.Status = OffsiteStatus.Cancelled;
            booking.HomeCancelledAtUtc = DateTime.UtcNow;
            booking.HomeCancelledBy = employee;
            booking.HomeCancellationReason = reason.Trim();
            return null;
        }, token);

    public Task<string?> ReviewRefundAsync(Guid id, Guid revision, string? note, Guid employee, CancellationToken token) =>
        ChangeBookingAsync(id, revision, OffsiteKind.HomeService, booking =>
        {
            if (booking.Status != OffsiteStatus.Cancelled || booking.VerifiedDeposit is not > 0 ||
                booking.RefundReviewedAtUtc.HasValue)
                return Task.FromResult<string?>("This booking has no outstanding refund review.");
            if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 500)
                return Task.FromResult<string?>("Enter the refund review outcome (up to 500 characters).");
            booking.RefundReviewedBy = employee;
            booking.RefundReviewedAtUtc = DateTime.UtcNow;
            booking.RefundReviewNote = note.Trim();
            return Task.FromResult<string?>(null);
        }, token);

    internal static IQueryable<HomeSlot> ActiveHomeSlots(LocalDbContext context, DateTime now) =>
        from booking in context.Set<OffsiteBooking>()
        join estimate in context.Set<GroomingQuote>() on booking.EstimateId equals estimate.Id
        where booking.HomeGroomerId != null && (booking.Status == OffsiteStatus.Confirmed ||
            (booking.Status == OffsiteStatus.AwaitingDeposit && booking.HoldExpiresAtUtc > now))
        select new HomeSlot { Booking = booking, PetId = estimate.PetId };

    internal sealed class HomeSlot
    {
        public required OffsiteBooking Booking { get; init; }
        public Guid PetId { get; init; }
    }

    public Task<string?> ScheduleTransportAsync(Guid id, Guid revision, Guid assignee, DateTime startUtc, int minutes,
        Guid employee, CancellationToken token) => ChangeTransportAsync(id, revision, async booking =>
    {
        if (booking.Status != OffsiteStatus.Reviewed)
            return "Review the transport request before scheduling.";
        if (startUtc <= DateTime.UtcNow || startUtc.Year >= 9999 || minutes is < 1 or > 720)
            return "Choose a future time and a duration between 1 and 720 minutes.";
        if (!(await TransportStaffAsync(token)).Any(x => x.Id == assignee))
            return "Choose an active employee for transport.";
        var endUtc = startUtc.AddMinutes(minutes);
        if (await db.Set<OffsiteBooking>().AnyAsync(x => x.TransportEmployeeId == assignee &&
            (x.Status == OffsiteStatus.Scheduled || x.Status == OffsiteStatus.InCustody) &&
            x.TransportStartUtc < endUtc && x.TransportEndUtc > startUtc, token) ||
            await db.Set<GroomingAppointment>().AnyAsync(x => x.GroomerId == assignee && x.CancelledAtUtc == null &&
                x.StartsAtUtc < endUtc && x.EndsAtUtc > startUtc, token) ||
            await ActiveHomeSlots(db, DateTime.UtcNow).AnyAsync(x => x.Booking.HomeGroomerId == assignee &&
                x.Booking.RequestedAtUtc < endUtc && x.Booking.HomeEndsAtUtc > startUtc, token))
            return "This employee already has work scheduled during that time.";
        booking.TransportEmployeeId = assignee;
        booking.TransportStartUtc = startUtc;
        booking.TransportEndUtc = endUtc;
        booking.TransportScheduledBy = employee;
        booking.Status = OffsiteStatus.Scheduled;
        return null;
    }, token);

    public Task<string?> HandoverAsync(Guid id, Guid revision, string? person, Guid employee, CancellationToken token) =>
        ChangeTransportAsync(id, revision, booking =>
        {
            if (string.IsNullOrWhiteSpace(person) || person.Trim().Length > 150)
                return Task.FromResult<string?>("Enter a handover name (up to 150 characters).");
            if (booking.Status == OffsiteStatus.Scheduled)
            {
                booking.Status = OffsiteStatus.InCustody;
                booking.ReleasedByName = person.Trim();
                booking.CustodyAtUtc = DateTime.UtcNow;
                booking.CustodyRecordedBy = employee;
            }
            else if (booking.Status == OffsiteStatus.InCustody)
            {
                booking.Status = OffsiteStatus.Completed;
                booking.AcceptedByName = person.Trim();
                booking.DeliveredAtUtc = DateTime.UtcNow;
                booking.DeliveryRecordedBy = employee;
            }
            else return Task.FromResult<string?>("Schedule transport before recording a handover.");
            return Task.FromResult<string?>(null);
        }, token);

    public Task<string?> CancelTransportAsync(Guid id, Guid revision, string? reason, Guid employee, CancellationToken token) =>
        ChangeTransportAsync(id, revision, booking =>
        {
            if (booking.Status is not (OffsiteStatus.Reviewed or OffsiteStatus.Scheduled))
                return Task.FromResult<string?>("A transport request can be cancelled before the pet is collected.");
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                return Task.FromResult<string?>("Enter a cancellation reason (up to 500 characters).");
            booking.Status = OffsiteStatus.Cancelled;
            booking.TransportCancellationReason = reason.Trim();
            booking.TransportCancelledBy = employee;
            booking.TransportCancelledAtUtc = DateTime.UtcNow;
            return Task.FromResult<string?>(null);
        }, token);

    private Task<string?> ChangeTransportAsync(Guid id, Guid revision, Func<OffsiteBooking, Task<string?>> change,
        CancellationToken token) => ChangeBookingAsync(id, revision, null, change, token);

    private async Task<string?> ChangeBookingAsync(Guid id, Guid revision, OffsiteKind? kind,
        Func<OffsiteBooking, Task<string?>> change, CancellationToken token)
    {
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var booking = await db.Set<OffsiteBooking>().SingleOrDefaultAsync(x =>
                x.Id == id && x.Revision == revision &&
                (kind.HasValue ? x.Kind == kind : x.Kind != OffsiteKind.HomeService), token);
            if (booking is null) return "The booking changed. Review the latest details.";
            var error = await change(booking);
            if (error is not null) return error;
            booking.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return null;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or SqlException { Number: 1205 or 1222 } ||
            ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } })
        {
            return "The schedule changed. Review it and try again.";
        }
    }

    public async Task<IReadOnlyList<OffsiteBookingRow>> BoardAsync(bool history, CancellationToken token)
    {
        var rows = await (from booking in db.Set<OffsiteBooking>().AsNoTracking()
                          join revision in db.Set<GroomingQuoteRevision>() on
                              new { booking.EstimateId, booking.EstimateVersion } equals
                              new { EstimateId = revision.QuoteId, EstimateVersion = revision.Number }
                          where history == (booking.Status != OffsiteStatus.Requested)
                          orderby booking.RequestedAtUtc, booking.Id
                          select new { Booking = booking, revision.Snapshot,
                              Assignee = db.Users.Where(x => x.Id == booking.TransportEmployeeId)
                                  .Select(x => x.FirstName + " " + x.LastName).FirstOrDefault() }).ToListAsync(token);
        return rows.Select(x => new OffsiteBookingRow(x.Booking, JsonSerializer.Deserialize<QuoteSnapshot>(x.Snapshot)!, x.Assignee)).ToArray();
    }

    public async Task<string?> RequestAsync(Guid estimateId, int version, OffsiteRequest request, Guid employee,
        CancellationToken token)
    {
        var now = DateTime.UtcNow;
        if (!Enum.IsDefined(request.Kind) || request.RequestedAtUtc <= now || request.RequestedAtUtc.Year >= 9999)
            return "Choose a request type and a future date and time.";
        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length > 300 ||
            string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Trim().Length > 40 ||
            request.Phone.Count(char.IsDigit) < 7)
            return "Enter a service address and a contact number.";
        if (!await db.Set<GroomingQuoteRevision>().AnyAsync(x => x.QuoteId == estimateId && x.Number == version, token))
            return "Choose a saved price estimate.";
        var bookings = db.Set<OffsiteBooking>();
        if (await bookings.AnyAsync(x => x.EstimateId == estimateId && x.Kind == request.Kind &&
            x.Status != OffsiteStatus.Rejected && x.Status != OffsiteStatus.Cancelled, token))
            return "This price estimate already has an open request of this type.";
        bookings.Add(new OffsiteBooking
        {
            EstimateId = estimateId, EstimateVersion = version, Kind = request.Kind, RequestedAtUtc = request.RequestedAtUtc,
            Address = request.Address.Trim(), Phone = request.Phone.Trim(), RecordedAtUtc = now, RecordedBy = employee
        });
        try
        {
            await db.SaveChangesAsync(token);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return "This request was already recorded. Review the off-site requests.";
        }
    }

    public async Task<string?> ReviewAsync(Guid id, Guid revision, OffsiteStatus decision, string? reason,
        Guid employee, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return "Enter a review note (up to 500 characters).";
        var booking = await db.Set<OffsiteBooking>().SingleOrDefaultAsync(x => x.Id == id &&
            x.Revision == revision && x.Status == OffsiteStatus.Requested, token);
        if (booking is null)
            return "This request has changed. Review the latest details.";
        var eligibleDecision = booking.Kind == OffsiteKind.HomeService ? OffsiteStatus.AwaitingDeposit : OffsiteStatus.Reviewed;
        if (decision != eligibleDecision && decision != OffsiteStatus.Rejected)
            return "Choose a decision for this request type.";
        if (decision != OffsiteStatus.Rejected && booking.RequestedAtUtc <= DateTime.UtcNow)
            return "The requested time has passed. Reject the request and record a new time.";
        booking.Status = decision;
        booking.ReviewedBy = employee;
        booking.ReviewedAtUtc = DateTime.UtcNow;
        booking.ReviewReason = reason.Trim();
        booking.Revision = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(token);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return "This request has changed. Review the latest details.";
        }
    }
}
