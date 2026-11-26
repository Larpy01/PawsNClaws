using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class LocalAppFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"PawsNClawsTests_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:LocalConnection"] = null,
                ["BootstrapAdmin:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        builder.ConfigureTestServices(services =>
        {
            // Remove the SQL Server configuration before startup role seeding.
            services.RemoveAll<IDbContextOptionsConfiguration<LocalDbContext>>();
            services.RemoveAll<DbContextOptions<LocalDbContext>>();
            services.RemoveAll<LocalDbContext>();
            services.AddDbContext<LocalDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.Configure<KeyManagementOptions>(options =>
                options.XmlRepository = new InMemoryKeyRepository());
        });
    }

    internal HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    internal async Task<TestEmployee> CreateEmployeeAsync(string role, bool isActive = true)
    {
        await using var scope = Services.CreateAsyncScope();
        return await TestEmployee.CreateAsync(
            scope.ServiceProvider.GetRequiredService<UserManager<LocalApplicationUser>>(),
            role, isActive);
    }

    internal static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, TestEmployee employee, string? password = null)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = employee.Email,
            ["Password"] = password ?? employee.Password,
            ["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, "/Account/Login")
        });
        return await client.PostAsync("/Account/Login", form, TestContext.Current.CancellationToken);
    }

    internal static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var page = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, $"The form at {path} did not render an antiforgery token.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    private sealed class InMemoryKeyRepository : IXmlRepository
    {
        private readonly ConcurrentBag<XElement> _keys = [];

        public IReadOnlyCollection<XElement> GetAllElements() =>
            _keys.Select(key => new XElement(key)).ToArray();

        public void StoreElement(XElement element, string friendlyName) =>
            _keys.Add(new XElement(element));
    }
}

internal sealed record TestEmployee(Guid Id, string Email, string Password)
{
    internal static string GeneratePassword() => $"Test!aA7{RandomNumberGenerator.GetHexString(32)}";

    internal static async Task<TestEmployee> CreateAsync(
        UserManager<LocalApplicationUser> users, string role, bool isActive = true)
    {
        var id = Guid.NewGuid();
        var email = $"{id:N}@example.test";
        var password = GeneratePassword();
        var user = new LocalApplicationUser
        {
            Id = id,
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = "Employee",
            IsActive = isActive
        };
        AssertSucceeded(await users.CreateAsync(user, password));
        AssertSucceeded(await users.AddToRoleAsync(user, role));
        return new TestEmployee(id, email, password);
    }

    internal static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
}
