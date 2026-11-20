using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Employees.DTOs;
using PawsNClaws.Application.Features.Employees.Interfaces;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class EmployeeManagementService
    : IEmployeeManagementService
{
    private readonly UserManager<LocalApplicationUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;

    public EmployeeManagementService(
        UserManager<LocalApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await _users.Users
            .AsNoTracking()
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        var result = new List<EmployeeDto>();

        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user);

            result.Add(ToDto(user, roles));
        }

        return result;
    }

    public async Task<EmployeeDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == id,
                cancellationToken);

        if (user is null)
            return null;

        var roles = await _users.GetRolesAsync(user);

        return ToDto(user, roles);
    }

    public async Task<(
        bool Succeeded,
        IReadOnlyList<string> Errors)> CreateAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = request.Email.Trim();

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return (false, ["Required employee information is missing."]);
        }

        // Privileged roles are never assignable through
        // this general employee-creation operation.
        var allowedRoles = new[]
        {
            LocalRoles.Manager,
            LocalRoles.Cashier,
            LocalRoles.Groomer,
            LocalRoles.InventoryStaff
        };

        if (!allowedRoles.Contains(request.Role))
        {
            return (false, ["The selected role is not permitted."]);
        }

        if (!await _roles.RoleExistsAsync(request.Role))
        {
            return (false, ["The selected role does not exist."]);
        }

        if (await _users.FindByEmailAsync(email) is not null)
        {
            return (false, ["An employee with this email already exists."]);
        }

        var user = new LocalApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmailConfirmed = false,
            IsActive = true
        };

        var created = await _users.CreateAsync(
            user,
            request.Password);

        if (!created.Succeeded)
        {
            return (false, created.Errors
                .Select(e => e.Description)
                .ToArray());
        }

        var assigned = await _users.AddToRoleAsync(
            user,
            request.Role);

        if (!assigned.Succeeded)
        {
            // Roll back the newly created account if
            // role assignment fails.
            var deleted = await _users.DeleteAsync(user);

            if (!deleted.Succeeded)
            {
                throw new InvalidOperationException(
                    "Employee creation failed and cleanup requires attention.");
            }

            return (false, assigned.Errors
                .Select(e => e.Description)
                .ToArray());
        }

        return (true, Array.Empty<string>());
    }

    private static EmployeeDto ToDto(
        LocalApplicationUser user,
        IEnumerable<string> roles)
    {
        return new EmployeeDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email ?? string.Empty,
            user.IsActive,
            roles.ToArray());
    }


    public async Task<(
        bool Succeeded,
        IReadOnlyList<string> Errors)> SetActiveStatusAsync(
        Guid employeeId,
        Guid actingUserId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (employeeId == actingUserId)
        {
            return (false,
                ["You cannot change your own account status."]);
        }

        var actor = await _users.FindByIdAsync(
            actingUserId.ToString());

        if (actor is null || !actor.IsActive)
        {
            return (false,
                ["The administrator account is not active."]);
        }

        if (!await _users.IsInRoleAsync(
            actor, LocalRoles.SuperAdmin) &&
            !await _users.IsInRoleAsync(
                actor, LocalRoles.Admin))
        {
            return (false,
                ["You are not authorized to change employee status."]);
        }

        var employee = await _users.FindByIdAsync(
            employeeId.ToString());

        if (employee is null)
        {
            return (false, ["Employee not found."]);
        }

        // SuperAdmin accounts require a separate,
        // privileged account recovery process.
        if (await _users.IsInRoleAsync(
            employee, LocalRoles.SuperAdmin))
        {
            return (false,
                ["SuperAdmin accounts cannot be modified here."]);
        }

        if (employee.IsActive == isActive)
        {
            return (true, Array.Empty<string>());
        }

        employee.IsActive = isActive;

        var result = await _users.UpdateAsync(employee);

        if (!result.Succeeded)
        {
            return (false, result.Errors
                .Select(error => error.Description)
                .ToArray());
        }

        // Force existing authentication sessions to be
        // revalidated after the account status changes.
        var stampResult =
            await _users.UpdateSecurityStampAsync(employee);

        if (!stampResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Employee status changed, but session invalidation failed.");
        }

        return (true, Array.Empty<string>());
    }

}
