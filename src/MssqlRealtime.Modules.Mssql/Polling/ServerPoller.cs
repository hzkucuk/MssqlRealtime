using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Modules.Mssql.Alerts;
using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Modules.Mssql.Probes;

namespace MssqlRealtime.Modules.Mssql.Polling;

/// <summary>
/// One collection pass against one server: open a connection, run every applicable probe,
/// evaluate the user's thresholds, publish the snapshot and any alert worth a notification.
/// <para>
/// A failing probe never fails the pass — a login without VIEW SERVER STATE should still
/// give you sessions, and a snapshot with a hole in it beats no snapshot at all.
/// </para>
/// </summary>
public sealed class ServerPoller(
    IEnumerable<ISqlProbe> probes,
    IConnectionStringFactory connectionStrings,
    ISnapshotCache cache,
    IAlertEngine alerts,
    IRealtimePublisher publisher,
    ILogger<ServerPoller> logger)
{
    private readonly ISqlProbe[] _probes = probes.OrderBy(p => p.Order).ToArray();

    public async Task<ServerSnapshot> PollAsync(ServerProfile profile, long pollNumber, CancellationToken ct)
    {
        var builder = new SnapshotBuilder(profile);
        var started = Stopwatch.GetTimestamp();
        var capturedAt = DateTimeOffset.UtcNow;

        var connectionString = connectionStrings.Build(profile);
        if (connectionString.IsFailure)
        {
            builder.Status = ServerStatus.Error;
            builder.ErrorMessage = connectionString.Error;
        }
        else
        {
            await CollectAsync(profile, connectionString.Value!, builder, pollNumber, ct);
        }

        if (cache.Get(profile.Id) is { } previous)
        {
            builder.CarryForwardFrom(previous);
        }

        var target = new AlertTarget
        {
            ModuleId = MssqlModule.ModuleId,
            TargetId = profile.Id.ToString("N"),
            TargetName = profile.Name,
            GroupName = profile.CustomerName
        };

        var candidates = MssqlAlertRules.Evaluate(profile, builder);
        var outcome = alerts.Evaluate(target, candidates, DateTimeOffset.UtcNow);

        var elapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var snapshot = builder.Build(capturedAt, elapsedMs, outcome.Active);

        cache.Set(snapshot);
        await publisher.PublishAsync(MssqlModule.ModuleId, target.TargetId, "snapshot", snapshot, ct);

        foreach (var notification in outcome.ToNotify)
        {
            logger.LogInformation(
                "Alert {State} {Rule} on {Server}: {Message}",
                notification.IsCleared ? "cleared" : "raised",
                notification.Alert.RuleId,
                profile.Name,
                notification.Body);

            await publisher.PublishAlertAsync(notification, ct);
        }

        return snapshot;
    }

    private async Task CollectAsync(
        ServerProfile profile,
        string connectionString,
        SnapshotBuilder builder,
        long pollNumber,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct);
            builder.Status = ServerStatus.Online;
        }
        catch (SqlException ex)
        {
            builder.Status = ServerStatus.Offline;
            builder.ErrorMessage = DescribeSqlError(ex);
            logger.LogWarning("Cannot connect to {Server} ({Host}): {Error}", profile.Name, profile.Host, ex.Message);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            builder.Status = ServerStatus.Error;
            builder.ErrorMessage = ex.Message;
            logger.LogError(ex, "Unexpected failure connecting to {Server}", profile.Name);
            return;
        }

        var context = new ProbeContext(profile, connection, builder, pollNumber);

        foreach (var probe in _probes)
        {
            ct.ThrowIfCancellationRequested();

            if (!probe.AppliesTo(profile))
            {
                continue;
            }

            // Expensive probes run on a slower cadence; the builder carries their last values
            // forward in between. The first poll always runs everything — otherwise a freshly
            // added server shows no version, no databases and no services for minutes.
            if (probe.EveryNthPoll > 1 && pollNumber > 1 && pollNumber % probe.EveryNthPoll != 0)
            {
                continue;
            }

            try
            {
                await probe.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SqlException ex)
            {
                builder.AddProbeError(probe.Name, DescribeSqlError(ex));
                logger.LogWarning("Probe {Probe} failed on {Server}: {Error}", probe.Name, profile.Name, ex.Message);
            }
            catch (Exception ex)
            {
                builder.AddProbeError(probe.Name, ex.Message);
                logger.LogError(ex, "Probe {Probe} failed on {Server}", probe.Name, profile.Name);
            }
        }
    }

    /// <summary>
    /// Turns the SQL error number into something the operator can act on. Guessing from the
    /// raw driver text is how "login failed" ends up looking like a network outage.
    /// </summary>
    internal static string DescribeSqlError(SqlException ex) => ex.Number switch
    {
        -2 or 258 => "Bağlantı zaman aşımına uğradı. Sunucu yanıt vermiyor ya da ağ yavaş.",
        2 or 53 or 10060 or 10061 => "Sunucuya ulaşılamıyor. Adres, port ve güvenlik duvarı kuralını doğrulayın.",
        18456 => "Giriş reddedildi. Kullanıcı adı veya parola hatalı.",
        4060 => "Belirtilen veritabanına erişim yok. `master` yeterlidir.",
        18452 => "Giriş güvenilir bir etki alanından gelmiyor (Windows kimlik doğrulaması).",
        300 or 297 or 229 => "İzin yetersiz. İzleyen kullanıcıya VIEW SERVER STATE verilmeli.",
        // TLS handshake failure — the single most common on-prem stumbling block.
        -2146893019 or 20 => "Şifreli bağlantı kurulamadı. Sertifikaya güvenmiyorsanız 'Sertifikaya güven' seçeneğini açın.",
        _ => $"SQL hatası {ex.Number}: {ex.Message}"
    };
}
