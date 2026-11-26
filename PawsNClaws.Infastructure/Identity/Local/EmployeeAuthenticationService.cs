using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PawsNClaws.Application.Features.Authentication.DTOs;
using PawsNClaws.Application.Features.Authentication.Interfaces;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class EmployeeAuthenticationService
    : IEmployeeAuthenticationService
{
    private readonly UserManager<LocalApplicationUser> _users;
    private readonly SignInManager<LocalApplicationUser> _signIn;
    private readonly ILogger<EmployeeAuthenticationService> _logger;

    public EmployeeAuthenticationService(
        UserManager<LocalApplicationUser> users,
        SignInManager<LocalApplicationUser> signIn,
        ILogger<EmployeeAuthenticationService> logger)
    {
        _users = users;
        _signIn = signIn;
        _logger = logger;
    }


    public async Task<EmployeeLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe)
    {
        var user = await _users.FindByEmailAsync(email.Trim());

        if (user is null)
        {
            _logger.LogWarning(
                "Employee login failed: account not found.");

            return new(EmployeeLoginStatus.InvalidCredentials);
        }

        if (!user.IsActive || await _users.IsInRoleAsync(user, LocalRoles.SuperAdmin))
        {
            _logger.LogWarning(
                "Business sign-in rejected. UserId={UserId}",
                user.Id);

            return new(EmployeeLoginStatus.InvalidCredentials);
        }

        var result = await _signIn.PasswordSignInAsync(
            user,
            password,
            rememberMe,
            lockoutOnFailure: true);

        _logger.LogInformation(
            "Employee sign-in result. UserId={UserId}, " +
            "Succeeded={Succeeded}, LockedOut={LockedOut}, " +
            "NotAllowed={NotAllowed}, RequiresTwoFactor={RequiresTwoFactor}",
            user.Id,
            result.Succeeded,
            result.IsLockedOut,
            result.IsNotAllowed,
            result.RequiresTwoFactor);

        if (result.IsLockedOut)
            return new(EmployeeLoginStatus.LockedOut);

        if (result.IsNotAllowed)
            return new(EmployeeLoginStatus.NotAllowed);

        if (!result.Succeeded)
            return new(EmployeeLoginStatus.InvalidCredentials);

        return new(EmployeeLoginStatus.Success);
    }


    public Task LogoutAsync()
    {
        return _signIn.SignOutAsync();
    }
}
