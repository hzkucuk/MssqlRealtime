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

        foreach (var channel in _channels)
        {
            var configuration = configurations.FirstOrDefault(c =>
                string.Equals(c.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase));

            if (configuration is null || !configuration.Enabled)
            {
                continue;
            }

            if (notification.IsCleared && !configuration.SendRecoveries)
            {
                continue;
            }

            // A channel set to "critical only" must not be woken by a warning.
            if (!notification.IsCleared && notification.Alert.Severity < configuration.MinimumSeverity)
            {
                continue;
            }

            try
            {
                var result = await channel.SendAsync(notification, configuration.Settings, ct);

                if (result.IsFailure)
                {
                    // One channel failing must never stop the others: that is the whole point
                    // of having more than one way to reach someone.
                    logger.LogWarning(
                        "Notification channel {Channel} failed: {Error}",
                        channel.Id, result.Error);
                }
                else
                {
                    logger.LogInformation(
                        "Alert delivered via {Channel}: {Target} / {Rule}",
                        channel.Id, notification.Alert.Target.TargetName, notification.Alert.RuleId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification channel {Channel} threw", channel.Id);
            }
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
}
