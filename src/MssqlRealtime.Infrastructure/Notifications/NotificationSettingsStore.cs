using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Notifications;
using MssqlRealtime.Infrastructure.Persistence;

namespace MssqlRealtime.Infrastructure.Notifications;

public sealed record ChannelConfiguration
{
    public required string ChannelId { get; init; }
    public required bool Enabled { get; init; }
    public required Severity MinimumSeverity { get; init; }
    public required bool SendRecoveries { get; init; }
    public required ChannelSettings Settings { get; init; }
}

/// <summary>
/// Reads and writes channel configuration, decrypting secrets on the way out and encrypting
/// them on the way in. The only component that ever holds a bot token in clear text.
/// </summary>
public interface INotificationSettingsStore
{
    Task<IReadOnlyList<ChannelConfiguration>> GetAllAsync(CancellationToken ct = default);
    Task<ChannelConfiguration> GetAsync(string channelId, CancellationToken ct = default);

    Task SaveAsync(
        string channelId,
        bool enabled,
        Severity minimumSeverity,
        bool sendRecoveries,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<ChannelField> fields,
        CancellationToken ct = default);
}

public sealed class NotificationSettingsStore(AppDbContext db, ISecretProtector protector)
    : INotificationSettingsStore
{
    public async Task<IReadOnlyList<ChannelConfiguration>> GetAllAsync(CancellationToken ct = default)
    {
        var settings = await db.NotificationChannelSettings.AsNoTracking().ToListAsync(ct);
        var states = await db.NotificationChannelStates.AsNoTracking().ToListAsync(ct);

        return settings
            .Select(s => s.ChannelId)
            .Concat(states.Select(s => s.ChannelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(channelId => Build(channelId, settings, states))
            .ToList();
    }

    public async Task<ChannelConfiguration> GetAsync(string channelId, CancellationToken ct = default)
    {
        var settings = await db.NotificationChannelSettings
            .AsNoTracking()
            .Where(s => s.ChannelId == channelId)
            .ToListAsync(ct);

        var state = await db.NotificationChannelStates
            .AsNoTracking()
            .Where(s => s.ChannelId == channelId)
            .ToListAsync(ct);

        return Build(channelId, settings, state);
    }

    public async Task SaveAsync(
        string channelId,
        bool enabled,
        Severity minimumSeverity,
        bool sendRecoveries,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<ChannelField> fields,
        CancellationToken ct = default)
    {
        var existing = await db.NotificationChannelSettings
            .Where(s => s.ChannelId == channelId)
            .ToListAsync(ct);

        foreach (var field in fields)
        {
            if (!values.TryGetValue(field.Key, out var incoming))
            {
                // Key absent from the payload: leave whatever is stored alone. This is what
                // lets the client re-save a form without ever seeing the secret it contains.
                continue;
            }

            var current = existing.FirstOrDefault(s => s.Key == field.Key);

            if (string.IsNullOrEmpty(incoming))
            {
                // Explicit empty string clears the value.
                if (current is not null) db.NotificationChannelSettings.Remove(current);
                continue;
            }

            var stored = field.IsSecret ? protector.Protect(incoming) : incoming;

            if (current is null)
            {
                db.NotificationChannelSettings.Add(new NotificationChannelSetting
                {
                    ChannelId = channelId,
                    Key = field.Key,
                    Value = stored,
                    IsSecret = field.IsSecret
                });
            }
            else
            {
                current.Value = stored;
                current.IsSecret = field.IsSecret;
                current.UpdatedAt = DateTimeOffset.UtcNow;
                db.NotificationChannelSettings.Update(current);
            }
        }

        var state = await db.NotificationChannelStates.FirstOrDefaultAsync(s => s.ChannelId == channelId, ct);
        if (state is null)
        {
            db.NotificationChannelStates.Add(new NotificationChannelState
            {
                ChannelId = channelId,
                Enabled = enabled,
                MinimumSeverity = minimumSeverity,
                SendRecoveries = sendRecoveries
            });
        }
        else
        {
            state.Enabled = enabled;
            state.MinimumSeverity = minimumSeverity;
            state.SendRecoveries = sendRecoveries;
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private ChannelConfiguration Build(
        string channelId,
        List<NotificationChannelSetting> settings,
        List<NotificationChannelState> states)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings.Where(s => string.Equals(s.ChannelId, channelId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!setting.IsSecret)
            {
                values[setting.Key] = setting.Value;
                continue;
            }

            // A key-ring change makes secrets unreadable; skip the value rather than crash,
            // and the channel will report itself as unconfigured.
            var plain = protector.Unprotect(setting.Value);
            if (plain.IsSuccess)
            {
                values[setting.Key] = plain.Value!;
            }
        }

        var state = states.FirstOrDefault(s => string.Equals(s.ChannelId, channelId, StringComparison.OrdinalIgnoreCase));

        return new ChannelConfiguration
        {
            ChannelId = channelId,
            Enabled = state?.Enabled ?? false,
            MinimumSeverity = state?.MinimumSeverity ?? Severity.Warning,
            SendRecoveries = state?.SendRecoveries ?? true,
            Settings = new ChannelSettings(values)
        };
    }
}
