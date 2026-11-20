using Microsoft.AspNetCore.Identity;
using PawsNClaws.Application.Features.Authentication.DTOs;
using PawsNClaws.Application.Features.Authentication.Interfaces;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class EmployeeAuthenticationService
    : IEmployeeAuthenticationService
{
    private readonly UserManager<LocalApplicationUser> _users;
    private readonly SignInManager<LocalApplicationUser> _signIn;

    public EmployeeAuthenticationService(
        UserManager<LocalApplicationUser> users,
        SignInManager<LocalApplicationUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    public async Task<EmployeeLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe)
    {
        var user = await _users.FindByEmailAsync(email);

        if (user is null || !user.IsActive)
        {
            return new(EmployeeLoginStatus.InvalidCredentials);
        }

        var result = await _signIn.PasswordSignInAsync(
            user,
            password,
            rememberMe,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
            return new(EmployeeLoginStatus.LockedOut);

        if (result.IsNotAllowed)
            return new(EmployeeLoginStatus.NotAllowed);

        return new(result.Succeeded
            ? EmployeeLoginStatus.Success
            : EmployeeLoginStatus.InvalidCredentials);
    }

    public Task LogoutAsync()
    {
        return _signIn.SignOutAsync();
    }
}
