using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Core.Notifications;

namespace MssqlRealtime.Infrastructure.Notifications;

/// <summary>
/// POSTs the alert as JSON to any URL: Slack and Teams incoming webhooks, an internal ticket
/// system, or a script of your own. The escape hatch for everything we did not build in.
/// </summary>
public sealed class WebhookChannel(IHttpClientFactory httpClientFactory, ILogger<WebhookChannel> logger)
    : INotificationChannel
{
    public const string ChannelId = "webhook";

    private const string UrlKey = "url";
    private const string SecretKey = "secret";
    private const string FormatKey = "format";

    public string Id => ChannelId;
    public string Title => "Webhook";

    public IReadOnlyList<ChannelField> Fields =>
    [
        new()
        {
            Key = UrlKey,
            Label = "Hedef adres",
            Placeholder = "https://hooks.slack.com/services/…",
            Help = "Slack veya Teams incoming webhook adresi ya da kendi uç noktan."
        },
        new()
        {
            Key = FormatKey,
            Label = "Biçim",
            IsRequired = false,
            Placeholder = "json",
            Help = "\"slack\" yazarsan Slack/Teams'in beklediği {text: …} gövdesi gönderilir; boş bırakılırsa tam alarm JSON'u."
        },
        new()
        {
            Key = SecretKey,
            Label = "İmza anahtarı",
            IsSecret = true,
            IsRequired = false,
            Help = "Doldurulursa gövdenin HMAC-SHA256 imzası X-Signature başlığında gönderilir; alıcı isteğin bizden geldiğini doğrulayabilir."
        }
    ];

    public Task<Result> SendAsync(AlertNotification notification, ChannelSettings settings, CancellationToken ct) =>
        PostAsync(settings, notification, ct);

    public Task<Result> SendTestAsync(ChannelSettings settings, CancellationToken ct)
    {
        var probe = new AlertNotification
        {
            Alert = new AlertState
            {
                Target = new AlertTarget
                {
                    ModuleId = "test",
                    TargetId = "test",
                    TargetName = "Sunucu İzleme",
                    GroupName = "Test"
                },
                RuleId = "test",
                RuleTitle = "Test",
                Severity = Severity.Warning,
                Message = "Webhook bildirimi çalışıyor.",
                SinceUtc = DateTimeOffset.UtcNow
            },
            IsCleared = false,
            RaisedAtUtc = DateTimeOffset.UtcNow
        };

        return PostAsync(settings, probe, ct);
    }

    private async Task<Result> PostAsync(ChannelSettings settings, AlertNotification notification, CancellationToken ct)
    {
        if (settings.Get(UrlKey) is not { } url)
        {
            return Result.Failure("Webhook adresi girilmemiş.", "not_configured");
        }

        try
        {
            var client = httpClientFactory.CreateClient(ChannelId);

            var payload = string.Equals(settings.Get(FormatKey), "slack", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(new { text = $"{notification.Title} — {notification.Body}" })
                : JsonSerializer.Serialize(new
                {
                    title = notification.Title,
                    body = notification.Body,
                    isCleared = notification.IsCleared,
                    raisedAtUtc = notification.RaisedAtUtc,
                    moduleId = notification.Alert.Target.ModuleId,
                    targetId = notification.Alert.Target.TargetId,
                    targetName = notification.Alert.Target.TargetName,
                    groupName = notification.Alert.Target.GroupName,
                    ruleId = notification.Alert.RuleId,
                    ruleTitle = notification.Alert.RuleTitle,
                    severity = notification.Alert.Severity.ToString(),
                    value = notification.Alert.Value,
                    threshold = notification.Alert.Threshold,
                    unit = notification.Alert.Unit
                });

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            // Signing lets the receiver reject anything that did not come from this service.
            if (settings.Get(SecretKey) is { } secret)
            {
                var signature = System.Security.Cryptography.HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(payload));

                request.Headers.TryAddWithoutValidation("X-Signature", Convert.ToHexStringLower(signature));
            }

            var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            logger.LogWarning("Webhook returned {Status}", (int)response.StatusCode);
            return Result.Failure($"Webhook hedefi HTTP {(int)response.StatusCode} döndü.", "webhook_rejected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Webhook delivery failed");
            return Result.Failure($"Webhook adresine ulaşılamadı: {ex.Message}", "webhook_unreachable");
        }
    }
}
