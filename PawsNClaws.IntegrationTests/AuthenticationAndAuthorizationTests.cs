using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class AuthenticationAndAuthorizationTests(LocalAppFactory app)
    : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Anonymous_requests_to_dashboard_are_challenged()
    {
        using var client = app.CreateBrowser();
        using var response = await client.GetAsync("/Dashboard", TestContext.Current.CancellationToken);

        AssertRedirectTo(response, "/Account/Login");
    }

    [Fact]
    public async Task Login_post_without_antiforgery_token_is_rejected()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        using var client = app.CreateBrowser();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = employee.Email,
            ["Password"] = employee.Password
        });

        using var response = await client.PostAsync("/Account/Login", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Invalid_password_and_inactive_account_receive_the_same_generic_error(
        bool isActive, bool useCorrectPassword)
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.Cashier, isActive);
        using var client = app.CreateBrowser();
        using var response = await LocalAppFactory.SignInAsync(
            client, employee, useCorrectPassword ? null : TestEmployee.GeneratePassword());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Unable to sign in. Check your credentials.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var dashboard = await client.GetAsync("/Dashboard", TestContext.Current.CancellationToken);
        AssertRedirectTo(dashboard, "/Account/Login");
    }

    [Fact]
    public async Task Deactivating_an_employee_revokes_their_existing_cookie_session()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, employee);
        AssertRedirectTo(login, "/");
        using var before = await client.GetAsync("/Dashboard", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
            var account = await users.FindByIdAsync(employee.Id.ToString());
            Assert.NotNull(account);
            account.IsActive = false;
            TestEmployee.AssertSucceeded(await users.UpdateAsync(account));
        }

        using var after = await client.GetAsync("/Dashboard", TestContext.Current.CancellationToken);
        AssertRedirectTo(after, "/Account/Login");
    }

    [Theory]
    [InlineData(LocalRoles.Admin, "/Employees/Create", true)]
    [InlineData(LocalRoles.Cashier, "/Employees/Create", false)]
    [InlineData(LocalRoles.Cashier, "/Products/Create", false)]
    [InlineData(LocalRoles.InventoryStaff, "/Products/Create", true)]
    public async Task Seeded_role_permissions_control_access(string role, string path, bool authorized)
    {
        var employee = await app.CreateEmployeeAsync(role);
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, employee);
        AssertRedirectTo(login, "/");

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        if (authorized)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else
            AssertRedirectTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Employee_creation_requires_an_admin_role_even_with_the_permission()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
            var account = await users.FindByIdAsync(employee.Id.ToString());
            Assert.NotNull(account);
            TestEmployee.AssertSucceeded(await users.AddClaimAsync(
                account, new Claim(LocalRoleSeeder.PermissionClaimType, Permissions.UsersCreate)));
        }

        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, employee);
        AssertRedirectTo(login, "/");

        using var response = await client.GetAsync("/Employees/Create", TestContext.Current.CancellationToken);
        AssertRedirectTo(response, "/Account/AccessDenied");
    }

    [Theory]
    [InlineData("/Employees/Create")]
    [InlineData("/Employees/SetStatus")]
    [InlineData("/Employees/ChangeRole")]
    public async Task Cashiers_cannot_post_employee_changes_even_with_a_valid_antiforgery_token(string path)
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.Cashier);
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, employee);
        AssertRedirectTo(login, "/");
        var token = await LocalAppFactory.GetAntiforgeryTokenAsync(client, "/Dashboard");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        using var response = await client.PostAsync(path, form, TestContext.Current.CancellationToken);

        AssertRedirectTo(response, "/Account/AccessDenied");
    }

    private static void AssertRedirectTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var absolute = new Uri(new Uri("https://localhost"), location);
        Assert.Equal(path, absolute.AbsolutePath);
    }
}
