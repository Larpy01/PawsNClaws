using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Features.Notifications;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Grooming;

public sealed class GroomingAppointments(LocalDbContext db, IGroomingVisits visits, IConfiguration configuration) : IGroomingAppointments
{
    public async Task<IReadOnlyList<AppointmentRow>> BoardAsync(DateTime fromUtc, DateTime untilUtc, CancellationToken token) =>
        await (from appointment in db.Set<GroomingAppointment>().AsNoTracking()
               join pet in db.Set<PetRecord>() on appointment.PetId equals pet.Id
               join employee in db.Users on appointment.GroomerId equals employee.Id
               where appointment.StartsAtUtc < untilUtc && appointment.EndsAtUtc > fromUtc
               orderby appointment.StartsAtUtc, appointment.Id
               select new AppointmentRow(appointment.Id, appointment.Revision, appointment.EstimateId,
                   appointment.EstimateVersion, pet.Name, employee.FirstName + " " + employee.LastName,
                   appointment.StartsAtUtc, appointment.EndsAtUtc, appointment.CancelledAtUtc, appointment.CancellationReason,
                   db.Set<GroomingVisit>().Where(x => x.EstimateId == appointment.EstimateId).Select(x => (Guid?)x.Id).FirstOrDefault()))
            .ToListAsync(token);

    public Task<string?> ScheduleAsync(Guid estimateId, int version, Guid groomerId, DateTime startsAtUtc,
        int durationMinutes, Guid employeeId, CancellationToken token) => WriteAsync(async () =>
    {
        var now = DateTime.UtcNow;
        if (startsAtUtc <= now || startsAtUtc > DateTime.MaxValue.AddDays(-1))
            return "Choose a future appointment time.";
        if (durationMinutes is < 1 or > 1440)
            return "Enter a duration between 1 and 1,440 minutes.";
        var petId = await (from estimate in db.Set<GroomingQuote>()
                           join revision in db.Set<GroomingQuoteRevision>() on estimate.Id equals revision.QuoteId
                           where estimate.Id == estimateId && revision.Number == version
                           select (Guid?)estimate.PetId).SingleOrDefaultAsync(token);
        if (!petId.HasValue)
            return "Choose a saved price estimate version.";
        if (!(await visits.ActiveGroomersAsync(token)).Any(x => x.Id == groomerId))
            return "Choose an active groomer.";

        var endsAtUtc = startsAtUtc.AddMinutes(durationMinutes);
        var shopStart = startsAtUtc.AddHours(8);
        var shopEnd = endsAtUtc.AddHours(8);
        var hours = configuration.GetSection("Grooming:ShopHours");
        var opens = ReadTime(hours["Opens"] ?? "09:00");
        var closes = ReadTime(hours["Closes"] ?? "21:00");
        if (opens is null || closes is null || opens >= closes)
            return "Shop hours are invalid. Ask an administrator to review the grooming schedule settings.";
        if (shopStart.Date != shopEnd.Date || TimeOnly.FromDateTime(shopStart) < opens ||
            TimeOnly.FromDateTime(shopEnd) > closes)
            return $"Choose a time within shop hours ({opens.Value:h:mm tt} to {closes.Value:h:mm tt}, Philippine time).";

        var groomer = configuration.GetSection($"Grooming:Groomers:{groomerId}");
        var weeklyHours = groomer.GetSection("WeeklyHours");
        if (weeklyHours.Exists())
        {
            var shift = weeklyHours.GetSection(shopStart.DayOfWeek.ToString());
            var shiftStart = ReadTime(shift["Start"]);
            var shiftEnd = ReadTime(shift["End"]);
            if (shiftStart is null || shiftEnd is null || TimeOnly.FromDateTime(shopStart) < shiftStart ||
                TimeOnly.FromDateTime(shopEnd) > shiftEnd)
                return "The groomer is unavailable at that time. Choose another time or groomer.";
        }
        var species = groomer.GetSection("Species").Get<string[]>();
        if (species is { Length: > 0 })
        {
            var petSpecies = await db.Set<PetRecord>().Where(x => x.Id == petId).Select(x => x.Species).SingleAsync(token);
            if (!species.Contains(petSpecies, StringComparer.OrdinalIgnoreCase))
                return "Choose a groomer trained to work with this pet species.";
        }
        var appointments = db.Set<GroomingAppointment>().Where(x => x.CancelledAtUtc == null);
        if (await appointments.AnyAsync(x => x.EstimateId == estimateId, token))
            return "This price estimate already has an appointment. Cancel that slot or choose another estimate.";
        if (await db.Set<GroomingVisit>().AnyAsync(x => x.EstimateId == estimateId, token))
            return "This price estimate is already linked to a grooming visit. Create a new estimate to book another visit.";
        if (await appointments.AnyAsync(x => (x.PetId == petId || x.GroomerId == groomerId) &&
            x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > startsAtUtc, token))
            return "The pet or groomer is already booked during that time. Choose a different time or groomer.";

        if (await db.Set<OffsiteBooking>().AnyAsync(x => x.TransportEmployeeId == groomerId &&
            (x.Status == OffsiteStatus.Scheduled || x.Status == OffsiteStatus.InCustody) &&
            x.TransportStartUtc < endsAtUtc && x.TransportEndUtc > startsAtUtc, token))
            return "The groomer is scheduled for pet transport during that time.";

        if (await OffsiteBookings.ActiveHomeSlots(db, now).AnyAsync(x =>
            (x.Booking.HomeGroomerId == groomerId || x.PetId == petId) &&
            x.Booking.RequestedAtUtc < endsAtUtc && x.Booking.HomeEndsAtUtc > startsAtUtc, token))
            return "The pet or groomer has a home-service slot reserved during that time.";

        var appointment = new GroomingAppointment
        {
            PetId = petId.Value, GroomerId = groomerId, EstimateId = estimateId, EstimateVersion = version,
            StartsAtUtc = startsAtUtc, EndsAtUtc = endsAtUtc, ScheduledAtUtc = now, ScheduledBy = employeeId
        };
        db.Add(appointment);
        SmsOutbox.Enqueue(db, SmsNotificationKind.AppointmentConfirmed, appointment.Id);
        return null;
    }, token);

    public Task<string?> CancelAsync(Guid id, Guid revision, Guid employeeId, string? reason, CancellationToken token) =>
        WriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
                return "Enter a reason for cancelling (up to 500 characters).";
            var appointment = await db.Set<GroomingAppointment>()
                .SingleOrDefaultAsync(x => x.Id == id && x.Revision == revision && x.CancelledAtUtc == null, token);
            if (appointment is null)
                return "The appointment changed. Review the schedule and try again.";
            if (await db.Set<GroomingVisit>().AnyAsync(x => x.EstimateId == appointment.EstimateId, token))
                return "A grooming visit has already started for this appointment.";
            appointment.CancelledAtUtc = DateTime.UtcNow;
            appointment.CancelledBy = employeeId;
            appointment.CancellationReason = reason.Trim();
            appointment.Revision = Guid.NewGuid();
            return null;
        }, token);

    private static TimeOnly? ReadTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var time) ? time : null;

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
            return "The schedule changed while you were working. Review the schedule and try again.";
        }
    }
}
