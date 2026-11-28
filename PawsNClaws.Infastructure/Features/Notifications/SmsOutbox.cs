using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Features.Notifications;

internal static class SmsOutbox
{
    internal static void Enqueue(LocalDbContext db, SmsNotificationKind kind, Guid sourceId)
    {
        var now = DateTime.UtcNow;
        db.Add(new SmsNotification { Kind = kind, SourceId = sourceId, CreatedAtUtc = now, NextAttemptUtc = now });
    }

    internal static async Task<SmsMessage?> ResolveAsync(LocalDbContext db, SmsNotification item, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        if (item.Kind == SmsNotificationKind.AppointmentConfirmed)
        {
            var booking = await (from appointment in db.Set<GroomingAppointment>()
                                 join pet in db.Set<PetRecord>() on appointment.PetId equals pet.Id
                                 join owner in db.Set<CustomerRecord>() on pet.CustomerId equals owner.Id
                                 where appointment.Id == item.SourceId && appointment.CancelledAtUtc == null &&
                                       appointment.StartsAtUtc > now && owner.RecordConsent
                                 select new { owner.Phone, appointment.StartsAtUtc }).SingleOrDefaultAsync(token);
            var recipient = NormalizePhone(booking?.Phone);
            return recipient is null || booking is null ? null : new(recipient,
                FormattableString.Invariant($"Paws N' Claws: Your in-shop grooming appointment is confirmed for {booking.StartsAtUtc.AddHours(8):MMM d, yyyy h:mm tt} Philippine time."));
        }

        if (item.CreatedAtUtc < now.AddDays(-1)) return null;

        var phone = await (from checkIn in db.Set<GroomingCheckIn>()
                           join visit in db.Set<GroomingVisit>() on checkIn.VisitId equals visit.Id
                           join quote in db.Set<GroomingQuote>() on visit.EstimateId equals quote.Id
                           join pet in db.Set<PetRecord>() on quote.PetId equals pet.Id
                           join owner in db.Set<CustomerRecord>() on pet.CustomerId equals owner.Id
                           where checkIn.Id == item.SourceId && checkIn.CancelledAtUtc == null &&
                                 visit.PickedUpAtUtc == null && owner.RecordConsent
                           select owner.Phone).SingleOrDefaultAsync(token);
        var number = NormalizePhone(phone);
        return number is null ? null : new(number, "Paws N' Claws: Your pet is ready for pickup at the shop.");
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var value = string.Concat(phone.Where(c => c is not (' ' or '-' or '(' or ')')));
        if (value.Length == 11 && value.StartsWith("09", StringComparison.Ordinal))
            value = "+63" + value[1..];
        else if (value.Length == 12 && value.StartsWith("639", StringComparison.Ordinal))
            value = "+" + value;
        return value.Length == 13 && value.StartsWith("+639", StringComparison.Ordinal) &&
            value[1..].All(char.IsAsciiDigit) ? value : null;
    }
}

internal sealed record SmsMessage(string Recipient, string Text);
