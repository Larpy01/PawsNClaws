using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.Local;

internal static class TechnicalAccountCommands
{
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
