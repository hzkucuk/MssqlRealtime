using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Privacy;
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
    IAlertSink alertSink,
    IMetricSink metrics,
    IStatementPrivacy privacy,
    ILogger<ServerPoller> logger)
{
    private readonly ISqlProbe[] _probes = probes.OrderBy(p => p.Order).ToArray();

    /// <summary>
    /// Yavaş probların son değerleri. 🔴 Ölçüldü 2026-08-07: sürüm/edisyon, veritabanı ve
    /// servis listesi 60 turda bir okunuyor ama anlık görüntü her turda sıfırdan kuruluyordu.
    /// Sonuç: bu alanlar 60 turun 59'unda BOŞ gidiyordu — arayüzde sürüm satırı hiç
    /// görünmüyor, bir kez belirip kayboluyordu. Koddaki yorum "builder taşır" diyordu;
    /// taşımıyordu.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, (SqlInstanceInfo? Instance,
        IReadOnlyList<DatabaseInfo> Databases,
        IReadOnlyList<SqlServiceInfo> Services)> _slowValues = new();

    public async Task<ServerSnapshot> PollAsync(ServerProfile profile, long pollNumber, CancellationToken ct)
    {
        var builder = new SnapshotBuilder(profile);

        // Yavaş probların bu turda çalışmayacağı değerler önceki turdan taşınır; prob
        // çalışırsa üzerine yazar.
        if (_slowValues.TryGetValue(profile.Id, out var carried))
        {
            builder.Instance = carried.Instance;
            builder.Databases = carried.Databases;
            builder.Services = carried.Services;
        }

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

        // Read once per pass, not per rule: the setting is panel-wide and a cycle that
        // masked some of its records and not others would be indefensible to explain.
        var statementStorage = privacy.Storage;

        var candidates = MssqlAlertRules.Evaluate(profile, builder, statementStorage);
        var outcome = alerts.Evaluate(target, candidates, DateTimeOffset.UtcNow);

        var elapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var snapshot = builder.Build(capturedAt, elapsedMs, outcome.Active);

        // Yalnız erişilebilen turda sakla: kapalı sunucudan boş liste taşımak, sonraki turda
        // "veritabanı yok" göstermek olurdu.
        if (builder.Status == ServerStatus.Online)
        {
            _slowValues[profile.Id] = (builder.Instance, builder.Databases, builder.Services);
        }

        cache.Set(snapshot);

        // History for the reports screen. Only when the server actually answered: writing
        // zeros for an unreachable server would draw a calm month where there was an outage.
        if (builder.Status == ServerStatus.Online)
        {
            // Who ran the slowest query, captured now: the report is read hours later, when
            // the session no longer exists and nothing can say what it was.
            var longest = LongestQuery.From(builder.Requests, statementStorage);

            metrics.Report(MssqlModule.ModuleId, target.TargetId, new MetricPoint
            {
                CpuPercent = builder.Resources?.CpuPercent,
                SqlCpuPercent = builder.Resources?.SqlCpuPercent,
                MemoryPercent = builder.Resources?.MemoryUsedPercent,
                SqlMemoryMb = (int?)builder.Resources?.SqlProcessMemoryMb,
                SessionCount = builder.Sessions.Count,
                RequestCount = builder.Requests.Count,
                BlockedCount = builder.Blocking.Select(b => b.BlockedSessionId).Distinct().Count(),
                LongestQuerySeconds = longest.Seconds,
                LongestQueryBy = longest.By,
                LongestQueryText = longest.Text
            });
        }

        await publisher.PublishAsync(MssqlModule.ModuleId, target.TargetId, "snapshot", snapshot, ct);

        foreach (var notification in outcome.ToNotify)
        {
            logger.LogInformation(
                "Alert {State} {Rule} on {Server}: {Message}",
                notification.IsCleared ? "cleared" : "raised",
                notification.Alert.RuleId,
                profile.Name,
                notification.Body);

            // The sink fans this out: connected apps, persisted history, and every configured
            // notification channel (Telegram, e-mail, webhook) — so an alert still reaches
            // the user with the app closed.
            await alertSink.PublishAsync(notification, ct);
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
    public static string DescribeSqlError(SqlException ex) => ex.Number switch
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
