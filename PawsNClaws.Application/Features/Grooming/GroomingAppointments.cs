namespace PawsNClaws.Application.Features.Grooming;

public sealed record AppointmentRow(Guid Id, Guid Revision, Guid EstimateId, int Version, string Pet,
    string Groomer, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime? CancelledAtUtc, string? CancellationReason, Guid? VisitId);

public interface IGroomingAppointments
{
    Task<IReadOnlyList<AppointmentRow>> BoardAsync(DateTime fromUtc, DateTime untilUtc, CancellationToken token);
    Task<string?> ScheduleAsync(Guid estimateId, int version, Guid groomerId, DateTime startsAtUtc,
        int durationMinutes, Guid employeeId, CancellationToken token);
    Task<string?> CancelAsync(Guid id, Guid revision, Guid employeeId, string? reason, CancellationToken token);
}
