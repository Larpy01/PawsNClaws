using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class TechnicalAuthenticationService(
    UserManager<LocalApplicationUser> users,
    SignInManager<LocalApplicationUser> signIn)
{
    public async Task<ClaimsPrincipal?> SignInAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (!await CanSignInAsync(user))
            return null;

        var result = await signIn.CheckPasswordSignInAsync(user!, password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return null;

        // Previously enrolled accounts may still have Identity's two-factor flag set.
        if (!(await users.ResetAccessFailedCountAsync(user!)).Succeeded)
            return null;

        return await signIn.CreateUserPrincipalAsync(user!);
    }

    public async Task<ClaimsPrincipal?> RevalidateAsync(ClaimsPrincipal? principal)
    {
        if (principal is null)
            return null;

        var user = await signIn.ValidateSecurityStampAsync(principal);
        return await CanSignInAsync(user) ? await signIn.CreateUserPrincipalAsync(user!) : null;
    }

    private async Task<bool> CanSignInAsync(LocalApplicationUser? user) =>
        user is { IsActive: true } && await users.IsInRoleAsync(user, LocalRoles.SuperAdmin);
}
