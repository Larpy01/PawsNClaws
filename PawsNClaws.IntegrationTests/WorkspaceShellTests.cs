using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class WorkspaceShellTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Dashboard_renders_metrics_and_scopes_saved_appearance_to_the_shell()
    {
        var token = TestContext.Current.CancellationToken;
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, manager);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Dashboard");
        request.Headers.Add("Cookie", "sidebar_state=true; workspace_theme=dark");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(token);

        Assert.Contains("data-sidebar-expanded=\"true\"", html);
        Assert.Contains("data-shell-theme=\"dark\"", html);
        Assert.DoesNotContain("data-bs-theme=", html);
        Assert.Contains("id=\"shell-command\"", html);
        Assert.Contains("id=\"shell-settings\"", html);
        Assert.Contains("Appointments today", html);
        Assert.Contains("Grooming activity", html);
        Assert.Contains("Retail sales by payment", html);
        Assert.Contains("href=\"/Customers/New\"", html);
        Assert.Contains("href=\"/GroomingCatalog/New\"", html);
        Assert.DoesNotContain("href=\"/Employees\"", html);
    }

    [Theory]
    [InlineData("/Account/Login", "Employee sign-in")]
    [InlineData("/system-admin/login", "SuperAdmin sign-in")]
    public async Task Sign_in_pages_use_separate_forms_outside_the_dashboard_shell(string path, string heading)
    {
        var token = TestContext.Current.CancellationToken;
        using var client = app.CreateBrowser();
        var html = await client.GetStringAsync(path, token);
        Assert.Contains(heading, html);
        Assert.Contains($"action=\"{path}\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("id=\"workspace-sidebar\"", html);
        Assert.DoesNotContain("/css/shell.css", html);
    }
}
