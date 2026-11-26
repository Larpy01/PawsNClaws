using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Local;

internal static class TechnicalAccountCommands
{
    internal const string Enroll = "--enroll-technical-mfa";
    internal const string ResetPassword = "--reset-technical-password";

    internal static async Task RunAsync(WebApplication app, string[] args)
    {
        if (args.Length != 2 || Console.IsInputRedirected || Console.IsOutputRedirected)
            throw new InvalidOperationException("Use an interactive terminal and supply one technical account email.");

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var user = await users.FindByEmailAsync(args[1].Trim());
        if (user is not { IsActive: true } || !await users.IsInRoleAsync(user, LocalRoles.SuperAdmin))
            throw new InvalidOperationException("An active, named SuperAdmin account is required.");

        if (args[0] == ResetPassword)
        {
            Console.Write("New password (hidden): ");
            var password = ReadPassword();
            Console.Write("Confirm password (hidden): ");
            if (password != ReadPassword())
                throw new InvalidOperationException("The passwords do not match.");

            await using var transaction = await db.Database.BeginTransactionAsync();
            EnsureSuccess(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password));
            EnsureSuccess(await users.SetLockoutEndDateAsync(user, null));
            EnsureSuccess(await users.ResetAccessFailedCountAsync(user));
            await transaction.CommitAsync();
            app.Logger.LogWarning("Operator reset a technical password. UserId={UserId}", user.Id);
            return;
        }

        await using var enrollment = await db.Database.BeginTransactionAsync();
        EnsureSuccess(await users.ResetAuthenticatorKeyAsync(user));
        EnsureSuccess(await users.SetTwoFactorEnabledAsync(user, true));
        var key = await users.GetAuthenticatorKeyAsync(user);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        if (string.IsNullOrEmpty(key) || recoveryCodes is null)
            throw new InvalidOperationException("Authenticator provisioning failed.");

        EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        await enrollment.CommitAsync();
        app.Logger.LogWarning("Operator provisioned or rotated technical MFA. UserId={UserId}", user.Id);
        Console.WriteLine("Add this setup key to your authenticator app using time-based codes.");
        Console.WriteLine(key);
        Console.WriteLine("Store these single-use recovery codes offline.");
        foreach (var recoveryCode in recoveryCodes)
            Console.WriteLine(recoveryCode);
    }

    private static string ReadPassword()
    {
        var value = new List<char>();
        ConsoleKeyInfo input;
        while ((input = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (input.Key == ConsoleKey.Backspace && value.Count > 0)
                value.RemoveAt(value.Count - 1);
            else if (!char.IsControl(input.KeyChar) && value.Count < 256)
                value.Add(input.KeyChar);
        }

        Console.WriteLine();
        return new string(value.ToArray());
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}
