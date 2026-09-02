using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Privacy;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>
/// Keeps the panel-wide statement privacy setting, served from memory.
/// </summary>
/// <remarks>
/// <para>
/// A singleton because the pollers are singletons and ask on every cycle: a database round
/// trip per server per five seconds to read one enum would be pure waste. The value is loaded
/// once at startup and replaced when it is saved.
/// </para>
/// <para>
/// Stored as a key/value row in <c>NotificationChannelSettings</c> under a reserved channel
/// id, the way the notification schedule already is. That table is the panel's key/value
/// store in practice, and this way a privacy switch does not cost a schema migration. The
/// reserved ids start with <c>__</c> and never reach the channels API, which lists only
/// registered <c>INotificationChannel</c>s.
/// </para>
/// </remarks>
public sealed class StatementPrivacyService(
    IServiceScopeFactory scopes,
    ILogger<StatementPrivacyService> logger) : IStatementPrivacy
{
    private const string SettingsChannel = "__gizlilik";
    private const string StorageKey = "statementStorage";

    // Privacy by default: a panel nobody configured must not be the one that keeps two years
    // of identity numbers.
    private int _storage = (int)StatementStorage.Masked;

    public StatementStorage Storage => (StatementStorage)Volatile.Read(ref _storage);

    public async Task<StatementStorage> RefreshAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.NotificationChannelSettings
            .AsNoTracking()
            .Where(x => x.ChannelId == SettingsChannel && x.Key == StorageKey)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct);

        var storage = Parse(stored);
        Volatile.Write(ref _storage, (int)storage);

        return storage;
    }

    public async Task SaveAsync(StatementStorage storage, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.NotificationChannelSettings
            .FirstOrDefaultAsync(x => x.ChannelId == SettingsChannel && x.Key == StorageKey, ct);

        if (row is null)
        {
            db.NotificationChannelSettings.Add(new NotificationChannelSetting
            {
                ChannelId = SettingsChannel,
                Key = StorageKey,
                Value = Format(storage)
            });
        }
        else
        {
            row.Value = Format(storage);
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // Only after the write survives: a poller must never mask less than what is on record.
        Volatile.Write(ref _storage, (int)storage);

        logger.LogInformation("Sorgu metni saklama ayarı değiştirildi: {Storage}", Format(storage));
    }

    /// <summary>Names, not numbers: renumbering the enum must not change a stored setting.</summary>
    private static string Format(StatementStorage storage) => storage switch
    {
        StatementStorage.Full => "full",
        StatementStorage.None => "none",
        _ => "masked"
    };

    private static StatementStorage Parse(string? value) => value switch
    {
        "full" => StatementStorage.Full,
        "none" => StatementStorage.None,
        // Includes null (never set) and anything unreadable: an unrecognised value must fall
        // to the safer side, not to "store everything".
        _ => StatementStorage.Masked
    };
}
