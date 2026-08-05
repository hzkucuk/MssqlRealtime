using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Infrastructure.Notifications;

namespace MssqlRealtime.Api.Realtime;

/// <summary>
/// Everything that happens when an alert fires: it reaches connected apps immediately, it is
/// written to history, and it is queued for the notification channels.
/// <para>
/// Channel delivery is queued rather than awaited because an SMTP handshake can take ten
/// seconds and the polling loop must not wait for it — a monitoring tool that stalls while
/// telling you about a problem is its own problem.
/// </para>
/// </summary>
public sealed class AlertBroadcaster(
    IHubContext<ToolsHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<AlertBroadcaster> logger) : IAlertSink
{
    // Bounded: if channels are down and alerts keep coming, drop the oldest rather than grow
    // without limit. The in-app stream and the history are unaffected.
    private readonly Channel<AlertNotification> _queue = Channel.CreateBounded<AlertNotification>(
        new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ChannelReader<AlertNotification> Queue => _queue.Reader;

    public async Task PublishAsync(AlertNotification notification, CancellationToken ct = default)
    {
        // 1. Connected clients — instant, and the only path that raises an in-app notification.
        try
        {
            await hub.Clients.Group(ToolsHub.AlertsGroup).SendAsync("alert", notification, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not push alert to connected clients");
        }

        // 2. History — survives restarts and answers "what happened last night".
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAlertStore>();
            await store.RecordAsync(notification, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not persist alert history");
        }

        // 3. Out-of-app delivery — Telegram, e-mail, webhook.
        if (!_queue.Writer.TryWrite(notification))
        {
            logger.LogWarning("Alert notification queue rejected an item; delivery skipped");
        }
    }
}

/// <summary>Drains the delivery queue. One consumer, so channels are never hit in parallel.</summary>
public sealed class AlertDeliveryService(
    AlertBroadcaster broadcaster,
    INotificationDispatcher dispatcher,
    ILogger<AlertDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Alert delivery service started");

        await foreach (var notification in broadcaster.Queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await dispatcher.DispatchAsync(notification, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Dispatch already swallows per-channel failures; reaching here is a bug.
                logger.LogError(ex, "Alert delivery failed for {Target}", notification.Alert.Target.TargetName);
            }
        }
    }
}

/// <summary>
/// Reloads alerts that were open when the service stopped, and trims old history daily.
/// Without the reload, every restart re-announces problems the user already knows about.
/// </summary>
public sealed class AlertMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IAlertEngine engine,
    ILogger<AlertMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAlertStore>();

            var active = await store.GetActiveAsync(stoppingToken);
            if (active.Count > 0)
            {
                engine.Restore(active);
                logger.LogInformation("Restored {Count} alert(s) that were active before restart", active.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not restore active alerts");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IAlertStore>();
                var pruned = await store.PruneAsync(Retention, stoppingToken);

                if (pruned > 0)
                {
                    logger.LogInformation("Pruned {Count} alert record(s) older than {Days} days", pruned, Retention.Days);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Alert history maintenance failed");
            }
        }
    }
}
