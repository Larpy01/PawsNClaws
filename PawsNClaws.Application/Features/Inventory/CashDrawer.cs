using System.Security.Claims;
using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Application.Features.Inventory;

public sealed record CashDrawerBoard(CashDrawerShift? Active, decimal Expected, IReadOnlyList<CashDrawerShift> Recent);
public sealed record CashShiftDetails(CashDrawerShift Shift, decimal Expected, IReadOnlyList<CashDrawerEntry> Entries);

public interface ICashDrawer
{
    Task<CashDrawerBoard> BoardAsync(ClaimsPrincipal actor, CancellationToken token);
    Task<CashShiftDetails?> FindAsync(ClaimsPrincipal actor, Guid id, CancellationToken token);
    Task<string?> OpenAsync(ClaimsPrincipal actor, decimal openingCash, CancellationToken token);
    Task<string?> MoveAsync(ClaimsPrincipal actor, Guid shiftId, Guid operationId, decimal amount,
        string? reason, CancellationToken token);
    Task<string?> CloseAsync(ClaimsPrincipal actor, Guid shiftId, decimal countedCash, CancellationToken token);
}
