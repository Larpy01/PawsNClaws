using PawsNClaws.Application.Features.Grooming;

namespace PawsNClaws.Local.Models;

public sealed record AppointmentBoard(DateOnly Date, IReadOnlyList<AppointmentRow> Rows);
public sealed record NewAppointmentPage(QuoteVersion Estimate, IReadOnlyList<ChecklistGroomer> Groomers,
    Guid GroomerId, string? StartsAt, int? DurationMinutes);
