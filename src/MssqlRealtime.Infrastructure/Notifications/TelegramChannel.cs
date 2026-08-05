using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Core.Notifications;

namespace MssqlRealtime.Infrastructure.Notifications;

/// <summary>
/// Telegram bot delivery.
/// <para>
/// This is the channel that actually solves "tell me while the app is closed": it needs no
/// Apple Developer membership, no Firebase project and no app-store release — just a bot
/// token from @BotFather. The message arrives on the phone's lock screen like any other chat.
/// </para>
/// </summary>
public sealed class TelegramChannel(IHttpClientFactory httpClientFactory, ILogger<TelegramChannel> logger)
    : INotificationChannel
{
    public const string ChannelId = "telegram";

    private const string TokenKey = "botToken";
    private const string ChatKey = "chatId";

    public string Id => ChannelId;
    public string Title => "Telegram";

    public IReadOnlyList<ChannelField> Fields =>
    [
        new()
        {
            Key = TokenKey,
            Label = "Bot token",
            IsSecret = true,
            Placeholder = "123456789:AA...",
            Help = "Telegram'da @BotFather ile /newbot komutundan alınır."
        },
        new()
        {
            Key = ChatKey,
            Label = "Sohbet (chat) kimliği",
            Placeholder = "123456789",
            Help = "Bota bir mesaj yazıp https://api.telegram.org/bot<TOKEN>/getUpdates adresinden öğrenilir. "
                 + "Grup için başında eksi işareti bulunur."
        }
    ];

    public Task<Result> SendAsync(AlertNotification notification, ChannelSettings settings, CancellationToken ct)
    {
        var severity = notification.IsCleared
            ? "✅"
            : notification.Alert.Severity == Severity.Critical ? "🔴" : "🟠";

        var group = notification.Alert.Target.GroupName;
        var lines = new List<string>
        {
            $"{severity} <b>{Escape(notification.Alert.Target.TargetName)}</b>",
            group is null ? string.Empty : $"<i>{Escape(group)}</i>",
            string.Empty,
            Escape(notification.Body)
        };

        if (!notification.IsCleared && notification.Alert.Value is { } value)
        {
            var unit = notification.Alert.Unit ?? string.Empty;
            lines.Add($"Ölçülen: <code>{value:0.#}{unit}</code> · Sınır: <code>{notification.Alert.Threshold:0.#}{unit}</code>");
        }

        lines.Add($"<i>{notification.RaisedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}</i>");

        var text = string.Join('\n', lines.Where(l => l.Length > 0 || l == string.Empty));

        return SendMessageAsync(settings, text, ct);
    }

    public Task<Result> SendTestAsync(ChannelSettings settings, CancellationToken ct) =>
        SendMessageAsync(
            settings,
            "✅ <b>Sunucu İzleme</b>\n\nTelegram bildirimi çalışıyor. Alarm oluştuğunda buraya düşecek.",
            ct);

    private async Task<Result> SendMessageAsync(ChannelSettings settings, string text, CancellationToken ct)
    {
        if (!settings.Has(TokenKey, ChatKey))
        {
            return Result.Failure("Telegram için bot token ve chat kimliği gerekli.", "not_configured");
        }

        try
        {
            var client = httpClientFactory.CreateClient(ChannelId);

            var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{settings.Require(TokenKey)}/sendMessage",
                new
                {
                    chat_id = settings.Require(ChatKey),
                    text,
                    parse_mode = "HTML",
                    disable_web_page_preview = true
                },
                ct);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            // Telegram explains the problem in the body ("chat not found", "Unauthorized"),
            // and that sentence is far more useful to the user than the status code.
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Telegram rejected the message: {Status} {Body}", (int)response.StatusCode, body);

            return Result.Failure(
                $"Telegram gönderimi reddedildi (HTTP {(int)response.StatusCode}). {Summarise(body)}",
                "telegram_rejected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram delivery failed");
            return Result.Failure($"Telegram'a ulaşılamadı: {ex.Message}", "telegram_unreachable");
        }
    }

    private static string Summarise(string body) =>
        body.Length > 200 ? body[..200] : body;

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
