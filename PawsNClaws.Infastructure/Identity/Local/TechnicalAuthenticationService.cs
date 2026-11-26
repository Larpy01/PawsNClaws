using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class TechnicalAuthenticationService(
    UserManager<LocalApplicationUser> users,
    SignInManager<LocalApplicationUser> signIn,
    LocalDbContext db,
    ILogger<TechnicalAuthenticationService> logger)
{
    private const string ReplayProvider = "TechnicalMfa";
    private const string ReplayToken = "RecentAuthenticatorCodes";
    private const string MfaClaim = "amr";
    private const string MfaValue = "mfa";

    public async Task<ClaimsPrincipal?> SignInAsync(
        string email, string password, string code, bool useRecoveryCode)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (!await CanSignInAsync(user))
            return null;

        var passwordResult = await signIn.CheckPasswordSignInAsync(user!, password, lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
            return null;

        await using var recoveryTransaction = useRecoveryCode && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync()
            : null;
        code = code.Replace(" ", "");
        var verified = useRecoveryCode
            ? (await users.RedeemTwoFactorRecoveryCodeAsync(user!, code)).Succeeded
            : await VerifyAuthenticatorAsync(user!, code.Replace("-", ""));
        if (!verified)
        {
            await users.AccessFailedAsync(user!);
            if (recoveryTransaction is not null)
                await recoveryTransaction.CommitAsync();
            logger.LogWarning("Technical second factor rejected. UserId={UserId}", user!.Id);
            return null;
        }

        if (useRecoveryCode && !(await users.UpdateSecurityStampAsync(user!)).Succeeded)
            return null;

        if (!(await users.ResetAccessFailedCountAsync(user!)).Succeeded)
            return null;

        if (recoveryTransaction is not null)
            await recoveryTransaction.CommitAsync();
        if (useRecoveryCode)
            logger.LogWarning("Technical recovery code used. UserId={UserId}", user!.Id);

        return await CreatePrincipalAsync(user!);
    }

    public async Task<ClaimsPrincipal?> RevalidateAsync(ClaimsPrincipal? principal)
    {
        if (principal?.HasClaim(MfaClaim, MfaValue) != true)
            return null;

        var user = await signIn.ValidateSecurityStampAsync(principal);
        return await CanSignInAsync(user) ? await CreatePrincipalAsync(user!) : null;
    }

    private async Task<bool> CanSignInAsync(LocalApplicationUser? user) =>
        user is { IsActive: true } &&
        await users.IsInRoleAsync(user, LocalRoles.SuperAdmin) &&
        await users.GetTwoFactorEnabledAsync(user) &&
        !string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user));

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(LocalApplicationUser user)
    {
        var principal = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(MfaClaim, MfaValue));
        return principal;
    }

    private async Task<bool> VerifyAuthenticatorAsync(LocalApplicationUser user, string code)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit) ||
            !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
            return false;

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
            return false;

        // Keep fingerprints through Identity's TOTP acceptance window. Identity's
        // user concurrency stamp prevents two requests from accepting the same code.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fingerprint = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(code)));
        var stored = await users.GetAuthenticationTokenAsync(user, ReplayProvider, ReplayToken);
        var recent = (stored ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => long.TryParse(entry.Split(':', 2)[0],
                CultureInfo.InvariantCulture, out var usedAt) && usedAt > now - 180)
            .ToList();
        if (recent.Any(entry => entry.EndsWith(":" + fingerprint, StringComparison.Ordinal)))
            return false;

        recent.Add(now.ToString(CultureInfo.InvariantCulture) + ":" + fingerprint);
        return (await users.SetAuthenticationTokenAsync(
            user, ReplayProvider, ReplayToken, string.Join(";", recent))).Succeeded;
    }
}
