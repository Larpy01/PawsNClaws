using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class TechnicalAuthenticationTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Unenrolled_technical_account_cannot_use_either_sign_in()
    {
        var token = TestContext.Current.CancellationToken;
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        using var client = app.CreateBrowser();
        using var employeeLogin = await LocalAppFactory.SignInAsync(client, employee);
        Assert.Equal(HttpStatusCode.OK, employeeLogin.StatusCode);
        using var technicalLogin = await SignInAsync(client, employee, "123456");
        Assert.Equal(HttpStatusCode.OK, technicalLogin.StatusCode);
        using var dashboard = await client.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.StartsWith("/system-admin/login", dashboard.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Authenticator_code_is_required_and_cannot_be_replayed()
    {
        var token = TestContext.Current.CancellationToken;
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        var enrollment = await EnrollAsync(app, employee);
        using var first = app.CreateBrowser();
        using var bad = await SignInAsync(first, employee, "invalid");
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);

        var code = CalculateCode(enrollment.Key);
        using var accepted = await SignInAsync(first, employee, code);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        var cookie = Assert.Single(accepted.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("__Host-PawsNClaws.Technical=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PawsNClaws.Local.Auth=", cookie, StringComparison.Ordinal);
        using var dashboard = await first.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);

        using var second = app.CreateBrowser();
        using var replay = await SignInAsync(second, employee, code);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task Repeated_second_factor_failures_trigger_identity_lockout()
    {
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        var enrollment = await EnrollAsync(app, employee);
        using var client = app.CreateBrowser();
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();

        for (var attempt = 0; attempt < users.Options.Lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var response = await SignInAsync(client, employee, "invalid");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var user = await users.FindByIdAsync(employee.Id.ToString());
        Assert.NotNull(user);
        Assert.True(await users.IsLockedOutAsync(user));
        using var rejected = await SignInAsync(client, employee, CalculateCode(enrollment.Key));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
    }

    [Fact]
    public async Task Password_reset_revokes_existing_sessions_and_requires_the_new_password()
    {
        var token = TestContext.Current.CancellationToken;
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        var enrollment = await EnrollAsync(app, employee);
        using var original = app.CreateBrowser();
        using var signedIn = await SignInAsync(original, employee, CalculateCode(enrollment.Key));
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
        using var rejected = await SignInAsync(client, employee, enrollment.RecoveryCode, true);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var accepted = await SignInAsync(client, employee with { Password = newPassword },
            enrollment.RecoveryCode, true);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
    }

    [Fact]
    public async Task Recovery_code_is_single_use_and_revokes_existing_technical_sessions()
    {
        var token = TestContext.Current.CancellationToken;
        var employee = await app.CreateEmployeeAsync(LocalRoles.SuperAdmin);
        var enrollment = await EnrollAsync(app, employee);
        using var original = app.CreateBrowser();
        using var originalLogin = await SignInAsync(original, employee, CalculateCode(enrollment.Key));
        Assert.Equal(HttpStatusCode.Redirect, originalLogin.StatusCode);
        using var recovered = app.CreateBrowser();
        using var recovery = await SignInAsync(recovered, employee, enrollment.RecoveryCode, true);
        Assert.Equal(HttpStatusCode.Redirect, recovery.StatusCode);

        using var revoked = await original.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        using var active = await recovered.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        using var replayClient = app.CreateBrowser();
        using var replay = await SignInAsync(replayClient, employee, enrollment.RecoveryCode, true);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var antiforgery = await LocalAppFactory.GetAntiforgeryTokenAsync(recovered, "/system-admin");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgery
        });
        using var logout = await recovered.PostAsync("/system-admin/logout", form, token);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var afterLogout = await recovered.GetAsync("/system-admin", token);
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
    }

    internal static async Task<(string Key, string RecoveryCode)> EnrollAsync(LocalAppFactory app, TestEmployee employee)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>();
        var user = await users.FindByIdAsync(employee.Id.ToString());
        Assert.NotNull(user);
        TestEmployee.AssertSucceeded(await users.ResetAuthenticatorKeyAsync(user));
        TestEmployee.AssertSucceeded(await users.SetTwoFactorEnabledAsync(user, true));
        var key = await users.GetAuthenticatorKeyAsync(user);
        Assert.NotNull(key);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1);
        Assert.NotNull(recoveryCodes);
        return (key, Assert.Single(recoveryCodes));
    }

    internal static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, TestEmployee employee, string code, bool useRecoveryCode = false)
    {
        const string path = "/system-admin/login";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = employee.Email,
            ["Password"] = employee.Password,
            ["Code"] = code,
            ["UseRecoveryCode"] = useRecoveryCode.ToString(),
            ["__RequestVerificationToken"] = await LocalAppFactory.GetAntiforgeryTokenAsync(client, path)
        });
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }

    private static string CalculateCode(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in key.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(char.ToUpperInvariant(character));
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 0xf;
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset)) & 0x7fffffff;
        return (value % 1000000).ToString("D6", CultureInfo.InvariantCulture);
    }
}
