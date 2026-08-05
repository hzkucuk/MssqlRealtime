using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;

namespace MssqlRealtime.Core.Notifications;

/// <summary>
/// A way of reaching a human that does not depend on the app being open.
/// <para>
/// The in-app SignalR notification only works while a phone is connected, so it cannot be the
/// only path an alert takes. Channels are the server-side answer: the service is running
/// anyway, so it delivers the alert itself.
/// </para>
/// <para>
/// Adding a channel means implementing this and registering it — no other file changes.
/// </para>
/// </summary>
public interface INotificationChannel
{
    /// <summary>Stable slug used as the settings key, e.g. "telegram".</summary>
    string Id { get; }

    string Title { get; }

    /// <summary>What the user has to supply, so the settings screen can be generated.</summary>
    IReadOnlyList<ChannelField> Fields { get; }

    /// <summary>Sends one alert. Must not throw: a broken channel cannot silence the others.</summary>
    Task<Result> SendAsync(AlertNotification notification, ChannelSettings settings, CancellationToken ct);

    /// <summary>Sends a "this works" message so the user finds out now, not during an incident.</summary>
    Task<Result> SendTestAsync(ChannelSettings settings, CancellationToken ct);
}

/// <summary>One configurable value of a channel; drives the settings form on every client.</summary>
public sealed record ChannelField
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>Secrets are encrypted at rest and never returned to the client.</summary>
    public bool IsSecret { get; init; }

    public bool IsRequired { get; init; } = true;
    public string? Placeholder { get; init; }
    public string? Help { get; init; }
}

/// <summary>Resolved settings for one channel: plain values, secrets already decrypted.</summary>
public sealed class ChannelSettings(IReadOnlyDictionary<string, string> values)
{
    public IReadOnlyDictionary<string, string> Values { get; } = values;

    public string? Get(string key) => Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : null;

    public string Require(string key) =>
        Get(key) ?? throw new InvalidOperationException($"Channel setting '{key}' is missing.");

    public bool Has(params string[] keys) => keys.All(k => Get(k) is not null);
}
