using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.UnitTests;

public sealed class PermissionAuthorizationTests
{
    [Theory]
    [InlineData(true, Permissions.UsersCreate, true)]
    [InlineData(true, Permissions.UsersView, false)]
    [InlineData(true, null, false)]
    [InlineData(false, Permissions.UsersCreate, false)]
    public async Task Permission_requires_an_authenticated_identity_with_the_matching_claim(
        bool authenticated, string? permission, bool expected)
    {
        Claim[] claims = permission is null
            ? []
            : [new Claim(LocalRoleSeeder.PermissionClaimType, permission)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null));
        var requirement = new PermissionRequirement(Permissions.UsersCreate);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }
}
