
using Microsoft.AspNetCore.Authorization;

namespace PawsNClaws.Infastructure.Identity.Local;

public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var isTechnicalAccount = context.User.IsInRole(LocalRoles.SuperAdmin);
        var isTechnicalPermission = requirement.Permission == Permissions.TechnicalDashboardView;

        // Technical identities cannot inherit business access through legacy or extra grants.
        if (isTechnicalAccount != isTechnicalPermission)
            return Task.CompletedTask;

        if (context.User.HasClaim(LocalRoleSeeder.PermissionClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class PermissionRequirement
    : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
