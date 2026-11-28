using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Application.Features.Grooming;

public sealed record OffsiteRequest(OffsiteKind Kind, DateTime RequestedAtUtc, string Address, string Phone);
public sealed record OffsiteBookingRow(OffsiteBooking Booking, QuoteSnapshot Estimate, string? TransportEmployee);

public interface IOffsiteBookings
{
    Task<IReadOnlyList<ChecklistGroomer>> TransportStaffAsync(CancellationToken token);
    Task<string?> HoldHomeAsync(Guid id, Guid revision, Guid groomerId, int minutes, DateTime expiresAtUtc,
        CancellationToken token);
    Task<string?> CancelHomeAsync(Guid id, Guid revision, string? reason, Guid employee, CancellationToken token);
    Task<string?> ReviewRefundAsync(Guid id, Guid revision, string? note, Guid employee, CancellationToken token);
    Task<string?> ConfirmHomeAsync(Guid id, Guid revision, bool accepted, decimal amount, string? reference,
        Guid employee, CancellationToken token);
    Task<string?> CancelTransportAsync(Guid id, Guid revision, string? reason, Guid employee, CancellationToken token);
    Task<string?> ScheduleTransportAsync(Guid id, Guid revision, Guid assignee, DateTime startUtc, int minutes,
        Guid employee, CancellationToken token);
    Task<string?> HandoverAsync(Guid id, Guid revision, string? person, Guid employee, CancellationToken token);
    Task<IReadOnlyList<OffsiteBookingRow>> BoardAsync(bool history, CancellationToken token);
    Task<string?> RequestAsync(Guid estimateId, int version, OffsiteRequest request, Guid employee, CancellationToken token);
    Task<string?> ReviewAsync(Guid id, Guid revision, OffsiteStatus decision, string? reason, Guid employee,
        CancellationToken token);
}
