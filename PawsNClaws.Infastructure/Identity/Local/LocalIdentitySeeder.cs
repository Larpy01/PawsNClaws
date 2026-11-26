
using Microsoft.AspNetCore.Identity;

namespace PawsNClaws.Infastructure.Identity.Local;

public static class LocalIdentitySeeder
{
    public static Task SeedSuperAdminAsync(
        UserManager<LocalApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        string email,
        string password) =>
        SeedAdministratorAsync(userManager, roleManager, email, password, LocalRoles.SuperAdmin);

    public static Task SeedBusinessAdminAsync(
        UserManager<LocalApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        string email,
        string password) =>
        SeedAdministratorAsync(userManager, roleManager, email, password, LocalRoles.Admin);

    private static async Task SeedAdministratorAsync(
        UserManager<LocalApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        string email,
        string password,
        string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var roleResult = await roleManager.CreateAsync(
                new IdentityRole<Guid>(roleName));

            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ",
                        roleResult.Errors.Select(
                            e => e.Description)));
            }
        }

        // Check whether the account already exists.
        var existingUser =
            await userManager.FindByEmailAsync(email);

        if (existingUser is not null)
        {
            if (roleName == LocalRoles.Admin &&
                (!await userManager.IsInRoleAsync(existingUser, LocalRoles.Admin) ||
                 await userManager.IsInRoleAsync(existingUser, LocalRoles.SuperAdmin)))
            {
                throw new InvalidOperationException(
                    "Business Admin bootstrap requires an email dedicated to a business administrator.");
            }

            // Do not overwrite credentials or elevate an existing account.
            return;
        }

        var admin = new LocalApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = roleName == LocalRoles.Admin ? "Business" : "System",
            LastName = "Administrator",
            IsActive = true
        };

        // Create the user with Identity password hashing.
        var createResult =
            await userManager.CreateAsync(admin, password);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ",
                    createResult.Errors.Select(
                        e => e.Description)));
        }

        var assignResult =
            await userManager.AddToRoleAsync(
                admin, roleName);

        if (!assignResult.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ",
                    assignResult.Errors.Select(
                        e => e.Description)));
        }
    }
}
