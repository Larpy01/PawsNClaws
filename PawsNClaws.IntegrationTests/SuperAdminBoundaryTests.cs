using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class SuperAdminBoundaryTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Reseeding_removes_legacy_technical_role_permissions_and_is_repeatable()
    {
        await using var isolatedApp = new LocalAppFactory();
        await using var scope = isolatedApp.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var technical = await roles.FindByNameAsync(LocalRoles.SuperAdmin);
        var admin = await roles.FindByNameAsync(LocalRoles.Admin);
        Assert.NotNull(technical);
        Assert.NotNull(admin);
        var tag = new Claim("Test.Metadata", "preserve");
        foreach (var permission in Permissions.All.Where(value => value != Permissions.TechnicalDashboardView))
        {
            TestEmployee.AssertSucceeded(await roles.AddClaimAsync(technical,
                new Claim(LocalRoleSeeder.PermissionClaimType, permission)));
        }

        TestEmployee.AssertSucceeded(await roles.AddClaimAsync(technical,
            new Claim("permission", "Retired.Permission")));
        TestEmployee.AssertSucceeded(await roles.AddClaimAsync(technical,
            new Claim(LocalRoleSeeder.PermissionClaimType, Permissions.TechnicalDashboardView.ToLowerInvariant())));
        TestEmployee.AssertSucceeded(await roles.AddClaimAsync(technical, tag));

        await LocalRoleSeeder.SeedAsync(roles);
        await LocalRoleSeeder.SeedAsync(roles);

        var claims = await roles.GetClaimsAsync(technical);
        var permissionClaim = Assert.Single(claims, claim => string.Equals(
            claim.Type, LocalRoleSeeder.PermissionClaimType, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Permissions.TechnicalDashboardView, permissionClaim.Value);
        Assert.Contains(claims, claim => claim.Type == tag.Type && claim.Value == tag.Value);
        var adminClaims = await roles.GetClaimsAsync(admin);
        Assert.Contains(adminClaims, claim => claim.Value == Permissions.UsersCreate);
        Assert.DoesNotContain(adminClaims, claim => claim.Value == Permissions.TechnicalDashboardView);
    }

    [Fact]
    public async Task Technical_sign_in_lands_on_its_dashboard_and_business_routes_remain_denied()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
            var account = await users.FindByIdAsync(employee.Id.ToString());
            Assert.NotNull(account);
            // Simulate an older mixed-role account carrying both role and user grants.
            TestEmployee.AssertSucceeded(await users.AddToRoleAsync(account, LocalRoles.Admin));
            TestEmployee.AssertSucceeded(await users.AddClaimAsync(account,
                new Claim(LocalRoleSeeder.PermissionClaimType, Permissions.ProductsCreate)));
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = await TechnicalAuthenticationTests.EnrollAsync(app, employee);
        using var client = app.CreateBrowser();
        using var login = await TechnicalAuthenticationTests.SignInAsync(client, employee, enrollment.RecoveryCode, true);
        AssertRedirect(login, "/system-admin");
        using var console = await client.GetAsync("/system-admin", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, console.StatusCode);
        Assert.Contains("System Administration", await console.Content.ReadAsStringAsync(cancellationToken));

        foreach (var path in new[] { "/Dashboard", "/Employees", "/EmployeeAuditLogs", "/Products/Create" })
        {
            using var denied = await client.GetAsync(path, cancellationToken);
            AssertRedirect(denied, "/Account/Login");
        }

        var token = await LocalAppFactory.GetAntiforgeryTokenAsync(client, "/system-admin");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        foreach (var path in new[] { "/Employees/Create", "/Employees/SetStatus", "/Employees/ChangeRole" })
        {
            using var denied = await client.PostAsync(path, form, cancellationToken);
            AssertRedirect(denied, "/Account/Login");
        }

        await using var serviceScope = app.Services.CreateAsyncScope();
        var employees = serviceScope.ServiceProvider.GetRequiredService<IEmployeeManagementService>();
        var target = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        var change = await employees.ChangeRoleAsync(
            new(target.Id, employee.Id, LocalRoles.Manager), cancellationToken);
        Assert.False(change.Succeeded);
        Assert.Equal("You are not authorized to change roles.", Assert.Single(change.Errors));
    }

    [Fact]
    public async Task Business_admin_cannot_enter_the_technical_dashboard_even_with_the_permission_claim()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.Admin);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
            var account = await users.FindByIdAsync(employee.Id.ToString());
            Assert.NotNull(account);
            TestEmployee.AssertSucceeded(await users.AddClaimAsync(account,
                new Claim(LocalRoleSeeder.PermissionClaimType, Permissions.TechnicalDashboardView)));
        }

        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, employee);
        AssertRedirect(login, "/");
        using var denied = await client.GetAsync("/system-admin", TestContext.Current.CancellationToken);
        AssertRedirect(denied, "/system-admin/login");
        using var admitted = await client.GetAsync("/Employees/Create", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task Business_bootstrap_provisions_a_separate_account_and_cannot_elevate_an_existing_user()
    {
        var technical = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var email = $"{Guid.NewGuid():N}@example.test";
        var password = TestEmployee.GeneratePassword();

        await LocalIdentitySeeder.SeedBusinessAdminAsync(users, roles, email, password);
        await LocalIdentitySeeder.SeedBusinessAdminAsync(users, roles, email, TestEmployee.GeneratePassword());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalIdentitySeeder.SeedBusinessAdminAsync(users, roles, technical.Email, password));

        var businessAccount = await users.FindByEmailAsync(email);
        var technicalAccount = await users.FindByEmailAsync(technical.Email);
        Assert.NotNull(businessAccount);
        Assert.NotNull(technicalAccount);
        Assert.Equal(LocalRoles.Admin, Assert.Single(await users.GetRolesAsync(businessAccount)));
        Assert.Equal(LocalRoles.SuperAdmin, Assert.Single(await users.GetRolesAsync(technicalAccount)));
        Assert.True(await users.CheckPasswordAsync(businessAccount, password));
    }

    private static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal(path, new Uri(new Uri("https://localhost"), location).AbsolutePath);
    }
}
