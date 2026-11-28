using PawsNClaws.Application.Features.Grooming;
using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Local.Models;

public sealed record OffsiteBookingPage(QuoteVersion Estimate, OffsiteKind? Kind = null, string? RequestedAt = null,
    string? Address = null, string? Phone = null);
