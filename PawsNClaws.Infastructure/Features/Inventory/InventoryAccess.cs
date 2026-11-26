using System.Security.Claims;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Features.Inventory;

public static class InventoryAccess
{
    public static bool Has(ClaimsPrincipal actor, string permission) =>
        actor.Identity?.IsAuthenticated == true &&
        !actor.IsInRole(LocalRoles.SuperAdmin) &&
        actor.HasClaim(LocalRoleSeeder.PermissionClaimType, permission);

    public static bool IsManagement(ClaimsPrincipal actor) =>
        !actor.IsInRole(LocalRoles.SuperAdmin) &&
        (actor.IsInRole(LocalRoles.Admin) || actor.IsInRole(LocalRoles.Manager));

    internal static Guid Demand(ClaimsPrincipal actor, string permission, bool management = false)
    {
        if (!Has(actor, permission) || (management && !IsManagement(actor)) ||
            !Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            throw new UnauthorizedAccessException();
        return id;
    }
}
