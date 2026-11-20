using PawsNClaws.Application.Features.Authentication.DTOs;

namespace PawsNClaws.Application.Features.Authentication.Interfaces;

public interface IEmployeeAuthenticationService
{
    Task<EmployeeLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe);

    Task LogoutAsync();
}
