
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace PawsNClaws.Infastructure.Identity.Local;

public static class LocalRoleSeeder
{
    public const string PermissionClaimType = "Permission";

    public static async Task SeedAsync(
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        var roles = new Dictionary<string, string[]>
        {
            [LocalRoles.SuperAdmin] = [Permissions.TechnicalDashboardView],

            [LocalRoles.Admin] = Permissions.All
                .Where(permission => permission != Permissions.TechnicalDashboardView)
                .ToArray(),

            [LocalRoles.Manager] =
            [
                Permissions.GroomingCatalogManage,
                Permissions.CustomerRecordsManage,
                Permissions.DashboardView,
                Permissions.ProductsView,
                Permissions.ProductsCreate,
                Permissions.InventoryView,
                Permissions.InventoryAdjust,
                Permissions.InventoryReceive,
                Permissions.InventorySubmit,
                Permissions.InventoryReview,
                Permissions.InventoryUse,
                Permissions.PosAccess,
                Permissions.PosCreateSale,
                Permissions.PosVoidSale,
                Permissions.ReportsView
            ],

            [LocalRoles.Cashier] =
            [
                Permissions.CustomerRecordsManage,
                Permissions.DashboardView,
                Permissions.PosAccess,
                Permissions.PosCreateSale
            ],

            [LocalRoles.Groomer] =
            [
                Permissions.DashboardView,
                Permissions.InventoryView,
                Permissions.InventoryUse
            ],

            [LocalRoles.InventoryStaff] =
            [
                Permissions.DashboardView,
                Permissions.InventoryView,
                Permissions.InventoryReceive,
                Permissions.InventorySubmit
            ]
        };

        foreach (var (roleName, permissions) in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);

            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName);

                EnsureSuccess(
                    await roleManager.CreateAsync(role));
            }

            var existingClaims =
                await roleManager.GetClaimsAsync(role);

            if (roleName is LocalRoles.SuperAdmin or LocalRoles.InventoryStaff or LocalRoles.Cashier or LocalRoles.Groomer)
            {
                var obsoletePermissions = existingClaims
                    .Where(claim =>
                        string.Equals(claim.Type, PermissionClaimType, StringComparison.OrdinalIgnoreCase) &&
                        (roleName == LocalRoles.SuperAdmin
                            ? !permissions.Contains(claim.Value)
                            : claim.Value is Permissions.ProductsView or Permissions.ProductsCreate or Permissions.ProductsUpdate
                                || (roleName == LocalRoles.InventoryStaff && claim.Value == Permissions.InventoryAdjust)))
                    .DistinctBy(claim => (claim.Type, claim.Value))
                    .ToArray();

                foreach (var claim in obsoletePermissions)
                    EnsureSuccess(await roleManager.RemoveClaimAsync(role, claim));

                if (obsoletePermissions.Length > 0)
                    existingClaims = await roleManager.GetClaimsAsync(role);
            }

            foreach (var permission in permissions)
            {
                if (existingClaims.Any(c =>
                    c.Type == PermissionClaimType &&
                    c.Value == permission))
                {
                    continue;
                }

                EnsureSuccess(
                    await roleManager.AddClaimAsync(
                        role,
                        new Claim(
                            PermissionClaimType,
                            permission)));
            }
        }
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ",
                    result.Errors.Select(e => e.Description)));
        }
    }
}
