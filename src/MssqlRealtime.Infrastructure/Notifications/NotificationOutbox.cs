using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Infrastructure.Persistence;

namespace MssqlRealtime.Infrastructure.Notifications;

/// <summary>
/// Durable retry for notifications that could not be delivered.
/// <para>
/// A channel is unreachable exactly when it matters most — during the incident that also took
/// out the network. Without this, that alert is simply gone.
/// </para>
/// </summary>
public interface INotificationOutbox
{
    Task EnqueueAsync(string channelId, AlertNotification notification, string error, CancellationToken ct = default);

    /// <summary>Entries whose backoff has elapsed, oldest first.</summary>
    Task<IReadOnlyList<PendingDelivery>> GetDueAsync(int limit, CancellationToken ct = default);

    Task MarkDeliveredAsync(long id, CancellationToken ct = default);

    /// <summary>Records a failed attempt and schedules the next one, or gives up.</summary>
    Task MarkFailedAsync(long id, string error, CancellationToken ct = default);

    Task<OutboxStatus> GetStatusAsync(CancellationToken ct = default);
}

public sealed record PendingDelivery(long Id, string ChannelId, AlertNotification Notification, int Attempts);

public sealed record OutboxStatus(int Pending, int Abandoned, DateTimeOffset? OldestPendingUtc);

public sealed class NotificationOutbox(AppDbContext db) : INotificationOutbox
{
    /// <summary>After this long a delivery is abandoned; an eight-hour-old alert is history, not news.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(8);

    /// <summary>Backoff schedule. Beyond the last entry the final delay repeats.</summary>
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync(string channelId, AlertNotification notification, string error, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        db.NotificationOutbox.Add(new NotificationOutboxEntry
        {
            ChannelId = channelId,
            Payload = JsonSerializer.Serialize(notification, SerializerOptions),
            Summary = Truncate($"{notification.Title} — {notification.Body}", 400),
            Attempts = 1,
            FirstFailedUtc = now,
            LastAttemptUtc = now,
            NextAttemptUtc = now + Backoff[0],
            LastError = Truncate(error, 1000)
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PendingDelivery>> GetDueAsync(int limit, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var entries = await db.NotificationOutbox
            .Where(e => e.AbandonedUtc == null && e.NextAttemptUtc <= now)
            .OrderBy(e => e.FirstFailedUtc)
            .Take(limit)
            .ToListAsync(ct);

        var due = new List<PendingDelivery>(entries.Count);

        foreach (var entry in entries)
        {
            var notification = JsonSerializer.Deserialize<AlertNotification>(entry.Payload, SerializerOptions);

            if (notification is null)
            {
                // Unreadable payload: nothing to retry, and keeping it would block the queue.
                entry.AbandonedUtc = now;
                entry.LastError = "Bildirim içeriği okunamadı.";
                continue;
            }

            due.Add(new PendingDelivery(entry.Id, entry.ChannelId, notification, entry.Attempts));
        }

        await db.SaveChangesAsync(ct);
        return due;
    }

    public async Task MarkDeliveredAsync(long id, CancellationToken ct = default) =>
        await db.NotificationOutbox.Where(e => e.Id == id).ExecuteDeleteAsync(ct);

    public async Task MarkFailedAsync(long id, string error, CancellationToken ct = default)
    {
        var entry = await db.NotificationOutbox.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        entry.Attempts++;
        entry.LastAttemptUtc = now;
        entry.LastError = Truncate(error, 1000);

        if (now - entry.FirstFailedUtc >= GiveUpAfter)
        {
            entry.AbandonedUtc = now;
        }
        else
        {
            var delay = Backoff[Math.Min(entry.Attempts - 1, Backoff.Length - 1)];
            entry.NextAttemptUtc = now + delay;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<OutboxStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var pending = await db.NotificationOutbox.CountAsync(e => e.AbandonedUtc == null, ct);
        var abandoned = await db.NotificationOutbox.CountAsync(e => e.AbandonedUtc != null, ct);

        var oldest = await db.NotificationOutbox
            .Where(e => e.AbandonedUtc == null)
            .OrderBy(e => e.FirstFailedUtc)
            .Select(e => (DateTime?)e.FirstFailedUtc)
            .FirstOrDefaultAsync(ct);

        return new OutboxStatus(
            pending,
            abandoned,
            oldest is null ? null : new DateTimeOffset(oldest.Value, TimeSpan.Zero));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
