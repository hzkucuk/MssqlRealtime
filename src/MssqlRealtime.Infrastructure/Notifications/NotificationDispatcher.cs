using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Core.Notifications;

namespace MssqlRealtime.Infrastructure.Notifications;

public interface INotificationDispatcher
{
    /// <summary>Delivers an alert through every enabled channel. Never throws.</summary>
    Task DispatchAsync(AlertNotification notification, CancellationToken ct = default);

    /// <summary>Retries one queued delivery for one channel. Used by the outbox sweep.</summary>
    Task<Result> DeliverAsync(string channelId, AlertNotification notification, CancellationToken ct = default);

    Task<Result> SendTestAsync(string channelId, CancellationToken ct = default);
}

public sealed class NotificationDispatcher(
    IEnumerable<INotificationChannel> channels,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private readonly INotificationChannel[] _channels = channels.ToArray();

    public async Task DispatchAsync(AlertNotification notification, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationSettingsStore>();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();

        IReadOnlyList<ChannelConfiguration> configurations;

        try
        {
            configurations = await store.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read notification channel settings");
            return;
        }

        // Sessiz mi? Mesai dışında bildirim kesilmez, yalnız zili kapatılır: mesaj ve geçmiş
        // eksilmez. Sunucunun yerel saatiyle bakılır — kullanıcı kendi saatiyle düşünür.
        var silent = false;

        try
        {
            var schedule = await store.GetScheduleAsync(ct);
            silent = schedule.IsQuietAt(
                DateTimeOffset.Now,
                notification.Alert.Severity >= Severity.Critical,
                TurkishHolidays.IsHoliday);
        }
        catch (Exception ex)
        {
            // Zamanlama okunamadıysa sesli gönder: sessizlik varsayılan olamaz, kaçırılan
            // alarm sessizlikten daha pahalıdır.
            logger.LogWarning(ex, "Bildirim zamanlaması okunamadı; sesli gönderiliyor.");
        }

        foreach (var channel in _channels)
        {
            var configuration = configurations.FirstOrDefault(c =>
                string.Equals(c.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase));

            if (!ShouldDeliver(configuration, notification))
            {
                continue;
            }

            try
            {
                var result = await channel.SendAsync(notification, configuration!.Settings, silent, ct);

                if (result.IsSuccess)
                {
                    logger.LogInformation(
                        "Alert delivered via {Channel}: {Target} / {Rule}",
                        channel.Id, notification.Alert.Target.TargetName, notification.Alert.RuleId);
                    continue;
                }

                // One channel failing must never stop the others — that is the whole point of
                // having more than one way to reach someone. The failure is queued, not dropped.
                logger.LogWarning("Notification channel {Channel} failed: {Error}", channel.Id, result.Error);
                await QueueAsync(outbox, channel.Id, notification, result.Error ?? "bilinmeyen hata", ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification channel {Channel} threw", channel.Id);
                await QueueAsync(outbox, channel.Id, notification, ex.Message, ct);
            }
        }
    }

    public async Task<Result> DeliverAsync(string channelId, AlertNotification notification, CancellationToken ct = default)
    {
        var channel = _channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
        {
            return Result.Failure("Bilinmeyen bildirim kanalı.", "unknown_channel");
        }

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationSettingsStore>();
        var configuration = await store.GetAsync(channelId, ct);


        // The channel may have been switched off since the delivery was queued; stop retrying.
        if (!configuration.Enabled)
        {
            return Result.Failure("Kanal kapatılmış.", "channel_disabled");
        }

        // Yeniden denemede de aynı kural: kuyrukta beklerken mesai bitmiş olabilir.
        var silent = false;
        try
        {
            var schedule = await store.GetScheduleAsync(ct);
            silent = schedule.IsQuietAt(
                DateTimeOffset.Now,
                notification.Alert.Severity >= Severity.Critical,
                TurkishHolidays.IsHoliday);
        }
        catch
        {
            // Sesli gönder: sessizlik varsayılan olamaz.
        }

        try
        {
            return await channel.SendAsync(notification, configuration.Settings, silent, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message, "channel_threw");
        }
    }

    public async Task<Result> SendTestAsync(string channelId, CancellationToken ct = default)
    {
        var channel = _channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
        {
            return Result.Failure("Bilinmeyen bildirim kanalı.", "unknown_channel");
        }

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationSettingsStore>();
        var configuration = await store.GetAsync(channelId, ct);

        // Test mesajı her zaman sesli: kullanıcı zaten gelip gelmediğine bakıyor.
        try
        {
            return await channel.SendTestAsync(configuration.Settings, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test notification for {Channel} threw", channelId);
            return Result.Failure(ex.Message, "channel_threw");
        }
    }

    /// <summary>Whether this notification is meant for this channel, given its configuration.</summary>
    internal static bool ShouldDeliver(ChannelConfiguration? configuration, AlertNotification notification)
    {
        if (configuration is null || !configuration.Enabled)
        {
            return false;
        }

        if (notification.IsCleared)
        {
            return configuration.SendRecoveries;
        }

        // A channel set to "critical only" must not be woken by a warning.
        return notification.Alert.Severity >= configuration.MinimumSeverity;
    }

    private async Task QueueAsync(
        INotificationOutbox outbox,
        string channelId,
        AlertNotification notification,
        string error,
        CancellationToken ct)
    {
        try
        {
            await outbox.EnqueueAsync(channelId, notification, error, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not queue failed notification for {Channel}", channelId);
        }
    }
}
