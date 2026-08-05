using MssqlRealtime.Infrastructure.Notifications;

namespace MssqlRealtime.Api.Realtime;

/// <summary>
/// Retries notifications that failed to go out. A Telegram outage during an incident must
/// delay the message, not delete it.
/// </summary>
public sealed class NotificationRetryService(
    IServiceScopeFactory scopeFactory,
    INotificationDispatcher dispatcher,
    ILogger<NotificationRetryService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification retry sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();

        var due = await outbox.GetDueAsync(BatchSize, ct);
        if (due.Count == 0)
        {
            return;
        }

        foreach (var delivery in due)
        {
            var result = await dispatcher.DeliverAsync(delivery.ChannelId, delivery.Notification, ct);

            if (result.IsSuccess)
            {
                await outbox.MarkDeliveredAsync(delivery.Id, ct);
                logger.LogInformation(
                    "Queued notification delivered via {Channel} after {Attempts} attempt(s)",
                    delivery.ChannelId, delivery.Attempts + 1);
                continue;
            }

            // A channel switched off mid-retry is a decision, not a failure: stop retrying.
            if (result.Code == "channel_disabled")
            {
                await outbox.MarkDeliveredAsync(delivery.Id, ct);
                continue;
            }

            await outbox.MarkFailedAsync(delivery.Id, result.Error ?? "bilinmeyen hata", ct);
        }
    }
}
