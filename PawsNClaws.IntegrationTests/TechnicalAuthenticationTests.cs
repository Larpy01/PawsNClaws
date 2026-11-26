using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class TechnicalAuthenticationTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Theory]
    [InlineData("/system-admin/login", "/css/technical-login.css")]
    [InlineData("/Account/Login", "/lib/bootstrap/dist/css/bootstrap.min.css")]
    public async Task Login_stylesheets_are_available_before_sign_in(string path, string stylesheet)
    {
        using var client = app.CreateBrowser();
        var token = TestContext.Current.CancellationToken;
        using var page = await client.GetAsync(path, token);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync(token);
        Assert.DoesNotContain("<header", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(stylesheet, html);
        using var asset = await client.GetAsync(stylesheet, token);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("text/css", asset.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Superadmin_can_sign_in_with_password_only(bool previouslyEnrolled)
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        if (previouslyEnrolled)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
            var account = await users.FindByIdAsync(employee.Id.ToString());
            Assert.NotNull(account);
            TestEmployee.AssertSucceeded(await users.ResetAuthenticatorKeyAsync(account));
            TestEmployee.AssertSucceeded(await users.SetTwoFactorEnabledAsync(account, true));
        }

        using var client = app.CreateBrowser();
        using var response = await SignInAsync(client, employee);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("__Host-PawsNClaws.Technical=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        using var dashboard = await client.GetAsync("/system-admin", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    [Theory]
    [InlineData(LocalRoles.Admin, true)]
    [InlineData(LocalRoles.SuperAdmin, false)]
    public async Task Ineligible_accounts_cannot_sign_in(string role, bool active)
    {
        var employee = await app.CreateEmployeeAsync(role, active);
        using var client = app.CreateBrowser();
        using var login = await SignInAsync(client, employee);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var dashboard = await client.GetAsync("/system-admin", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_trigger_identity_lockout()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        using var client = app.CreateBrowser();
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();

        for (var attempt = 0; attempt < users.Options.Lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var response = await SignInAsync(client, employee with { Password = "invalid" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var user = await users.FindByIdAsync(employee.Id.ToString());
        Assert.NotNull(user);
        Assert.True(await users.IsLockedOutAsync(user));
        using var rejected = await SignInAsync(client, employee);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
    }

    [Fact]
    public async Task Password_reset_revokes_existing_sessions_and_requires_the_new_password()
    {
        var token = TestContext.Current.CancellationToken;
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        using var original = app.CreateBrowser();
        using var signedIn = await SignInAsync(original, employee);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        var newPassword = TestEmployee.GeneratePassword();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
            var account = await users.FindByIdAsync(employee.Id.ToString());
            Assert.NotNull(account);
            var resetToken = await users.GeneratePasswordResetTokenAsync(account);
            TestEmployee.AssertSucceeded(await users.ResetPasswordAsync(account, resetToken, newPassword));
        }

        using var revoked = await original.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        using var client = app.CreateBrowser();
        using var rejected = await SignInAsync(client, employee);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var accepted = await SignInAsync(client, employee with { Password = newPassword });
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_technical_session()
    {
        var token = TestContext.Current.CancellationToken;
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        using var client = app.CreateBrowser();
        using var login = await SignInAsync(client, employee);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var antiforgery = await LocalAppFactory.GetAntiforgeryTokenAsync(client, "/system-admin");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgery
        });
        using var logout = await client.PostAsync("/system-admin/logout", form, token);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
    }

    internal static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, TestEmployee employee)
    {
        const string path = "/system-admin/login";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = employee.Email,
            ["Password"] = employee.Password,
            ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(client, path)
        });
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }
}
