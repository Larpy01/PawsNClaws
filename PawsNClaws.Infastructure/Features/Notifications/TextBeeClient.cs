using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Infastructure.Features.Notifications;

public sealed class TextBeeClient(HttpClient client)
{
    internal async Task<SmsSubmission> SubmitAsync(SmsMessage message, string apiKey, string? deviceId,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.textbee.dev/api/v1/gateway/send-sms");
        request.Headers.Add("x-api-key", apiKey);
        request.Content = string.IsNullOrWhiteSpace(deviceId)
            ? JsonContent.Create(new { recipients = new[] { message.Recipient }, message = message.Text })
            : JsonContent.Create(new { recipients = new[] { message.Recipient }, message = message.Text, deviceId });

        try
        {
            using var response = await client.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new(SmsNotificationStatus.Pending, "rate_limited",
                    RetryAt: response.Headers.RetryAfter?.Date ??
                        DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1)));
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                return new(SmsNotificationStatus.NeedsReview, "provider_response_uncertain");
            if (!response.IsSuccessStatusCode)
                return new(SmsNotificationStatus.Failed, $"http_{(int)response.StatusCode}");

            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(token);
            if (result?.RootElement.ValueKind == JsonValueKind.Object &&
                result.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
                data.TryGetProperty("smsBatchId", out var id) && id.ValueKind == JsonValueKind.String &&
                id.GetString() is { Length: > 0 and <= 80 } batchId)
                return new(SmsNotificationStatus.Accepted, "provider_accepted", batchId);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.NameResolutionError ||
            ex.InnerException is SocketException { SocketErrorCode: SocketError.NetworkUnreachable or
                SocketError.HostUnreachable or SocketError.ConnectionRefused })
        {
            return new(SmsNotificationStatus.Pending, "network_unavailable", RetryAt: DateTimeOffset.UtcNow.AddMinutes(5));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
            ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            return new(SmsNotificationStatus.NeedsReview, "provider_response_uncertain");
        }

        return new(SmsNotificationStatus.NeedsReview, "provider_response_uncertain");
    }
}

internal sealed record SmsSubmission(SmsNotificationStatus Status, string Outcome, string? BatchId = null,
    DateTimeOffset? RetryAt = null);
