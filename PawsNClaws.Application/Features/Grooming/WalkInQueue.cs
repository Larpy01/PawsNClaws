using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Application.Features.Grooming;

public sealed record WalkInRow(Guid Id, Guid PetId, string Pet, string Customer, DateTime ArrivedAtUtc,
    WalkInStatus Status, Guid Revision, string? PriorityReason, string? CancellationReason);

public sealed record WalkInBoard(IReadOnlyList<WalkInRow> Open, IReadOnlyList<WalkInRow> Recent);

public interface IWalkInQueue
{
    Task<WalkInBoard> BoardAsync(CancellationToken token);
    Task<string?> AddAsync(Guid petId, Guid employee, CancellationToken token);
    Task<string?> CallAsync(Guid id, Guid revision, Guid employee, bool canPrioritize, string? reason, CancellationToken token);
    Task<string?> CancelAsync(Guid id, Guid revision, Guid employee, string? reason, CancellationToken token);
}
