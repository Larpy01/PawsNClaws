
using Microsoft.AspNetCore.Identity;

namespace PawsNClaws.Infastructure.Identity.Local;

public static class LocalIdentitySeeder
{
    public static async Task SeedSuperAdminAsync(
        UserManager<LocalApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        string email,
        string password)
    {
        const string roleName = "SuperAdmin";

        // Ensure the SuperAdmin role exists.
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
            // Never overwrite existing credentials
            // or automatically elevate existing accounts.
            return;
        }

        var admin = new LocalApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "System",
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

        // Assign the SuperAdmin role.
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
