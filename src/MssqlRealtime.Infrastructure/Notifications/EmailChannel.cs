using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Core.Notifications;

namespace MssqlRealtime.Infrastructure.Notifications;

/// <summary>
/// SMTP delivery. Slower and easier to miss than a chat message, but it is the one channel
/// every company already has, and it works from a server with no outbound internet beyond
/// its own mail relay.
/// </summary>
public sealed class EmailChannel(ILogger<EmailChannel> logger) : INotificationChannel
{
    public const string ChannelId = "email";

    private const string HostKey = "host";
    private const string PortKey = "port";
    private const string UserKey = "username";
    private const string PasswordKey = "password";
    private const string FromKey = "from";
    private const string ToKey = "to";
    private const string SslKey = "ssl";

    public string Id => ChannelId;
    public string Title => "E-posta";

    public IReadOnlyList<ChannelField> Fields =>
    [
        new() { Key = HostKey, Label = "SMTP sunucusu", Placeholder = "smtp.firma.com" },
        new() { Key = PortKey, Label = "Port", Placeholder = "587" },
        new() { Key = UserKey, Label = "Kullanıcı", IsRequired = false },
        new() { Key = PasswordKey, Label = "Parola", IsSecret = true, IsRequired = false },
        new() { Key = FromKey, Label = "Gönderen", Placeholder = "izleme@firma.com" },
        new()
        {
            Key = ToKey,
            Label = "Alıcı(lar)",
            Placeholder = "nobetci@firma.com, yonetici@firma.com",
            Help = "Virgülle ayırarak birden fazla adres yazılabilir."
        },
        new()
        {
            Key = SslKey,
            Label = "TLS kullan",
            IsRequired = false,
            Placeholder = "true",
            Help = "587 için true, 25 için genelde false."
        }
    ];

    public Task<Result> SendAsync(
        AlertNotification notification, ChannelSettings settings, bool silent, CancellationToken ct)
    {
        var subject = $"{(notification.IsCleared ? "[NORMAL]" : notification.Alert.Severity == Severity.Critical ? "[KRİTİK]" : "[UYARI]")} "
                    + $"{notification.Alert.Target.TargetName} — {notification.Alert.RuleTitle}";

        var body = new System.Text.StringBuilder()
            .AppendLine(notification.Body)
            .AppendLine()
            .AppendLine($"Sunucu   : {notification.Alert.Target.TargetName}")
            .AppendLine($"Müşteri  : {notification.Alert.Target.GroupName ?? "—"}")
            .AppendLine($"Kural    : {notification.Alert.RuleTitle}");

        if (notification.Alert.Value is { } value)
        {
            var unit = notification.Alert.Unit ?? string.Empty;
            body.AppendLine($"Ölçülen  : {value.ToString("0.#", CultureInfo.InvariantCulture)}{unit}")
                .AppendLine($"Sınır    : {notification.Alert.Threshold?.ToString("0.#", CultureInfo.InvariantCulture)}{unit}");
        }

        body.AppendLine($"Başlangıç: {notification.Alert.SinceUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}")
            .AppendLine($"Zaman    : {notification.RaisedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}");

        return SendMailAsync(settings, subject, body.ToString(), ct);
    }

    public Task<Result> SendTestAsync(ChannelSettings settings, CancellationToken ct) =>
        SendMailAsync(
            settings,
            "[TEST] Sunucu İzleme bildirimi",
            "E-posta bildirimi çalışıyor. Alarm oluştuğunda bu adrese gönderilecek.",
            ct);

    private async Task<Result> SendMailAsync(ChannelSettings settings, string subject, string body, CancellationToken ct)
    {
        if (!settings.Has(HostKey, FromKey, ToKey))
        {
            return Result.Failure("E-posta için sunucu, gönderen ve alıcı gerekli.", "not_configured");
        }

        try
        {
            var port = int.TryParse(settings.Get(PortKey), out var parsed) ? parsed : 587;

            using var client = new SmtpClient(settings.Require(HostKey), port)
            {
                EnableSsl = !string.Equals(settings.Get(SslKey), "false", StringComparison.OrdinalIgnoreCase),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            // An unauthenticated internal relay is a normal setup; only attach credentials
            // when a username was actually supplied.
            if (settings.Get(UserKey) is { } username)
            {
                client.Credentials = new NetworkCredential(username, settings.Get(PasswordKey) ?? string.Empty);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(settings.Require(FromKey)),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            foreach (var recipient in settings.Require(ToKey).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                message.To.Add(recipient);
            }

            await client.SendMailAsync(message, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SMTP delivery failed");
            return Result.Failure($"E-posta gönderilemedi: {ex.Message}", "smtp_failed");
        }
    }
}
