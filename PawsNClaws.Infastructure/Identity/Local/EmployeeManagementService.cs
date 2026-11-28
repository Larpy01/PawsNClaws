using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Application.Features.Employees.DTOs;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Persistence.Local;


namespace PawsNClaws.Infastructure.Identity.Local;


public sealed class EmployeeManagementService
    : IEmployeeManagementService
{
    private readonly UserManager<LocalApplicationUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly LocalDbContext _db;

    public EmployeeManagementService(
        UserManager<LocalApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        LocalDbContext db)
    {
        _users = users;
        _roles = roles;
        _db = db;
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

            if (!HasPrivilegedRole(roles))
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


    public async Task<(bool Succeeded, IReadOnlyList<string> Errors)>
        CreateAsync(
            CreateEmployeeRequest request,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return (false, ["Required employee information is missing."]);
        }

        var email = request.Email.Trim();

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        // Verify the acting administrator within the transaction.
        var actor = await _db.Users
            .FromSqlInterpolated(
                $"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {request.ActingUserId}")
            .SingleOrDefaultAsync(cancellationToken);

        if (actor is null || !actor.IsActive)
            return (false, ["Administrator account is not active."]);

        var actorRoles = await _users.GetRolesAsync(actor);

        if (!CanManageEmployees(actorRoles))
            return (false, ["You are not authorized to create employees."]);

        string[] operationalRoles =
        [
            LocalRoles.Manager,
        LocalRoles.Cashier,
        LocalRoles.Groomer,
        LocalRoles.InventoryStaff
        ];

        bool isAllowed = operationalRoles.Contains(request.Role, StringComparer.Ordinal);

        if (!isAllowed)
            return (false, ["You are not authorized to assign this role."]);

        if (!await _roles.RoleExistsAsync(request.Role))
            return (false, ["The selected role does not exist."]);

        if (await _users.FindByEmailAsync(email) is not null)
            return (false, ["An employee with this email already exists."]);

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
            user, request.Password);

        if (!created.Succeeded)
        {
            return (false, created.Errors
                .Select(e => e.Description).ToArray());
        }

        var assigned = await _users.AddToRoleAsync(
            user, request.Role);

        if (!assigned.Succeeded)
        {
            // The transaction rolls back the user creation.
            return (false, assigned.Errors
                .Select(e => e.Description).ToArray());
        }

        _db.EmployeeAuditLogs.Add(new EmployeeAuditLog
        {
            ActorUserId = request.ActingUserId,
            TargetUserId = user.Id,
            Action = "Employee.Created",
            OldValue = null,
            NewValue = request.Role,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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



    public async Task<(bool Succeeded, IReadOnlyList<string> Errors)>
        SetActiveStatusAsync(
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

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        var actor = await _db.Users
            .FromSqlInterpolated(
                $"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {actingUserId}")
            .SingleOrDefaultAsync(cancellationToken);

        if (actor is null || !actor.IsActive)
            return (false, ["Administrator account is not active."]);

        var actorRoles = await _users.GetRolesAsync(actor);

        if (!CanManageEmployees(actorRoles))
        {
            return (false,
                ["You are not authorized to change employee status."]);
        }

        var employee = await _db.Users
            .FromSqlInterpolated(
                $"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {employeeId}")
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
            return (false, ["Employee not found."]);

        var employeeRoles = await _users.GetRolesAsync(employee);

        // Protect privileged accounts from general status management.
        if (HasPrivilegedRole(employeeRoles))
        {
            return (false,
                ["Administrator accounts cannot be modified here."]);
        }

        if (employee.IsActive == isActive)
            return (true, Array.Empty<string>());

        string oldStatus = employee.IsActive
            ? "Active"
            : "Inactive";

        string newStatus = isActive
            ? "Active"
            : "Inactive";

        employee.IsActive = isActive;

        var updated = await _users.UpdateAsync(employee);

        if (!updated.Succeeded)
        {
            return (false, updated.Errors
                .Select(e => e.Description).ToArray());
        }

        var stampResult =
            await _users.UpdateSecurityStampAsync(employee);

        if (!stampResult.Succeeded)
        {
            return (false, stampResult.Errors
                .Select(e => e.Description).ToArray());
        }

        _db.EmployeeAuditLogs.Add(new EmployeeAuditLog
        {
            ActorUserId = actingUserId,
            TargetUserId = employeeId,
            Action = "Employee.StatusChanged",
            OldValue = oldStatus,
            NewValue = newStatus,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (true, Array.Empty<string>());
    }





    public async Task<(bool Succeeded, IReadOnlyList<string> Errors)>
        ChangeRoleAsync(
            ChangeEmployeeRoleRequest request,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.EmployeeId == request.ActingUserId)
            return (false, ["You cannot change your own role."]);

        string[] allowedRoles =
        [
            LocalRoles.Manager,
            LocalRoles.Cashier, 
            LocalRoles.Groomer,
            LocalRoles.InventoryStaff
        ];

        if (string.IsNullOrWhiteSpace(request.NewRole) ||
            !allowedRoles.Contains(
                request.NewRole, StringComparer.Ordinal))
        {
            return (false, ["The selected role is not permitted."]);
        }

        var actor = await _users.FindByIdAsync(
            request.ActingUserId.ToString());

        if (actor is null || !actor.IsActive)
            return (false, ["Administrator account is not active."]);

        if (!CanManageEmployees(await _users.GetRolesAsync(actor)))
            return (false, ["You are not authorized to change roles."]);

        if (!await _roles.RoleExistsAsync(request.NewRole))
            return (false, ["The selected role does not exist."]);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        var employee = await _db.Users
            .FromSqlInterpolated(
                $"SELECT * FROM dbo.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {request.EmployeeId}")
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
            return (false, ["Employee not found."]);

        var currentRoles = await _users.GetRolesAsync(employee);

        if (HasPrivilegedRole(currentRoles))
        {
            return (false,
                ["Privileged administrator accounts cannot be modified here."]);
        }

        if (currentRoles.Count != 1 ||
            !allowedRoles.Contains(
                currentRoles[0], StringComparer.Ordinal))
        {
            return (false,
                ["Employee has an unexpected role configuration."]);
        }

        string oldRole = currentRoles[0];

        if (oldRole == request.NewRole)
            return (true, Array.Empty<string>());

        var removed = await _users.RemoveFromRoleAsync(
            employee, oldRole);

        if (!removed.Succeeded)
            return (false, removed.Errors
                .Select(e => e.Description).ToArray());

        var assigned = await _users.AddToRoleAsync(
            employee, request.NewRole);

        if (!assigned.Succeeded)
            return (false, assigned.Errors
                .Select(e => e.Description).ToArray());

        var stampResult =
            await _users.UpdateSecurityStampAsync(employee);

        if (!stampResult.Succeeded)
            return (false, stampResult.Errors
                .Select(e => e.Description).ToArray());

        _db.EmployeeAuditLogs.Add(new EmployeeAuditLog
        {
            ActorUserId = request.ActingUserId,
            TargetUserId = request.EmployeeId,
            Action = "Employee.RoleChanged",
            OldValue = oldRole,
            NewValue = request.NewRole,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (true, Array.Empty<string>());
    }

    private static bool HasPrivilegedRole(IList<string> roles) =>
        roles.Contains(LocalRoles.Admin) || roles.Contains(LocalRoles.SuperAdmin);

    private static bool CanManageEmployees(IList<string> roles) =>
        roles.Contains(LocalRoles.Admin) && !roles.Contains(LocalRoles.SuperAdmin);


}
