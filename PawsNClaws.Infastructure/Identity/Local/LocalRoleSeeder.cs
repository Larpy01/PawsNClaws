
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
            [LocalRoles.SuperAdmin] = Permissions.All.ToArray(),

            [LocalRoles.Admin] = Permissions.All.ToArray(),

            [LocalRoles.Manager] =
            [
                Permissions.DashboardView,
                Permissions.ProductsView,
                Permissions.InventoryView,
                Permissions.InventoryAdjust,
                Permissions.PosAccess,
                Permissions.PosCreateSale,
                Permissions.PosVoidSale,
                Permissions.ReportsView
            ],

            [LocalRoles.Cashier] =
            [
                Permissions.DashboardView,
                Permissions.ProductsView,
                Permissions.PosAccess,
                Permissions.PosCreateSale
            ],

            [LocalRoles.Groomer] =
            [
                Permissions.DashboardView
            ],

            [LocalRoles.InventoryStaff] =
            [
                Permissions.DashboardView,
                Permissions.ProductsView,
                Permissions.ProductsCreate,
                Permissions.ProductsUpdate,
                Permissions.InventoryView,
                Permissions.InventoryAdjust
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
