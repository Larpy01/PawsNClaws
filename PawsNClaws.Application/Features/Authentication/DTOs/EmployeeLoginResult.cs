namespace PawsNClaws.Application.Features.Authentication.DTOs;

public enum EmployeeLoginStatus
{
    Success,
    InvalidCredentials,
    LockedOut,
    NotAllowed
}

public sealed record EmployeeLoginResult(
    EmployeeLoginStatus Status);
