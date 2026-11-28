using System.Security.Cryptography;
using System.Text;
using PawsNClaws.Infastructure.Features.Notifications;

namespace PawsNClaws.Local.Notifications;

internal sealed class SmsDeliveryWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<SmsDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("TextBee:Enabled")) return;

        string? apiKey = null;
        try { apiKey = ReadKey(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            logger.LogWarning("The TextBee credential could not be opened. SMS delivery is paused.");
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
        {
            logger.LogWarning("A valid TextBee credential is required. SMS delivery is paused.");
            return;
        }

        var device = configuration["TextBee:DeviceId"]?.Trim();
        using var interval = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                for (var i = 0; i < 10 && !stoppingToken.IsCancellationRequested; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<SmsDispatcher>();
                    if (!await dispatcher.ProcessNextAsync(apiKey, device, stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException
                or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                logger.LogWarning("The SMS queue is temporarily unavailable.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await interval.WaitForNextTickAsync(stoppingToken));
    }

    private static string? ReadKey()
    {
        var injected = Environment.GetEnvironmentVariable("TextBee__ApiKey");
        if (!string.IsNullOrWhiteSpace(injected)) return injected.Trim();
        if (!OperatingSystem.IsWindows()) return null;

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PawsNClaws", "Secrets", "textbee-key.dpapi");
        if (!File.Exists(path)) return null;

        var ciphertext = Convert.FromHexString(File.ReadAllText(path).Trim());
        var plaintext = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
        try { return Encoding.Unicode.GetString(plaintext).Trim(); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
