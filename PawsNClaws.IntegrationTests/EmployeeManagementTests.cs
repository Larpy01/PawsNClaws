using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class EmployeeManagementTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task An_admin_cannot_change_their_own_role_or_active_status()
    {
        var actor = await app.CreateEmployeeAsync(LocalRoles.Admin);
        await using var scope = app.Services.CreateAsyncScope();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeManagementService>();

        var role = await employees.ChangeRoleAsync(new(actor.Id, actor.Id, LocalRoles.Manager), TestContext.Current.CancellationToken);
        var status = await employees.SetActiveStatusAsync(actor.Id, actor.Id, false, TestContext.Current.CancellationToken);

        Assert.False(role.Succeeded);
        Assert.Equal("You cannot change your own role.", Assert.Single(role.Errors));
        Assert.False(status.Succeeded);
        Assert.Equal("You cannot change your own account status.", Assert.Single(status.Errors));
        var employee = await employees.GetByIdAsync(actor.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(employee);
        Assert.True(employee.IsActive);
        Assert.Equal(LocalRoles.Admin, Assert.Single(employee.Roles));
    }

    [Theory]
    [InlineData(LocalRoles.Admin)]
    [InlineData(LocalRoles.SuperAdmin)]
    public async Task An_admin_cannot_promote_an_employee_to_a_privileged_role(string newRole)
    {
        var actor = await app.CreateEmployeeAsync(LocalRoles.Admin);
        var target = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        await using var scope = app.Services.CreateAsyncScope();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeManagementService>();

        var result = await employees.ChangeRoleAsync(new(target.Id, actor.Id, newRole), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("The selected role is not permitted.", Assert.Single(result.Errors));
        var employee = await employees.GetByIdAsync(target.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(employee);
        Assert.Equal(LocalRoles.Cashier, Assert.Single(employee.Roles));
    }

    [Theory]
    [InlineData(LocalRoles.Admin, false, "Administrator account is not active.")]
    [InlineData(LocalRoles.Cashier, true, "You are not authorized to change roles.")]
    public async Task Changing_roles_requires_an_active_administrator(
        string actorRole, bool actorIsActive, string expectedError)
    {
        var actor = await app.CreateEmployeeAsync(actorRole, actorIsActive);
        var target = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        await using var scope = app.Services.CreateAsyncScope();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeManagementService>();

        var result = await employees.ChangeRoleAsync(new(target.Id, actor.Id, LocalRoles.Manager), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, Assert.Single(result.Errors));
    }
}
