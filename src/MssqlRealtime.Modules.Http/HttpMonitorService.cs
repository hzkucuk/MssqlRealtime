using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Http.Models;

namespace MssqlRealtime.Modules.Http;

/// <summary>Last result per target, so a phone that just connected sees state immediately.</summary>
public sealed class HttpResultCache
{
    private const int HistoryLength = 60;

    private readonly ConcurrentDictionary<Guid, HttpCheckResult> _latest = new();
    private readonly ConcurrentDictionary<Guid, Queue<bool>> _history = new();

    public HttpCheckResult? Get(Guid id) => _latest.TryGetValue(id, out var r) ? r : null;

    public IReadOnlyList<HttpCheckResult> GetAll() =>
        _latest.Values.OrderBy(r => r.GroupName).ThenBy(r => r.TargetName).ToList();

    /// <summary>Records a result and returns the rolling uptime over the recent window.</summary>
    public (double Percent, int Count) Record(HttpCheckResult result)
    {
        var history = _history.GetOrAdd(result.TargetId, static _ => new Queue<bool>());

        lock (history)
        {
            history.Enqueue(result.Status == HttpCheckStatus.Up);
            while (history.Count > HistoryLength)
            {
                history.Dequeue();
            }

            var up = history.Count(x => x);
            var percent = Math.Round(up * 100.0 / history.Count, 1);

            _latest[result.TargetId] = result with { UptimePercent = percent, RecentChecks = history.Count };
            return (percent, history.Count);
        }
    }

    public void Remove(Guid id)
    {
        _latest.TryRemove(id, out _);
        _history.TryRemove(id, out _);
    }
}

/// <summary>
/// Checks every enabled endpoint on its own schedule. Structurally the same as the MSSQL
/// poller — which is the point: the platform supplies the shape, the module supplies the work.
/// </summary>
public sealed class HttpMonitorService(
    IServiceScopeFactory scopeFactory,
    HttpChecker checker,
    HttpResultCache cache,
    IAlertEngine alerts,
    IAlertSink alertSink,
    IRealtimePublisher publisher,
    ILogger<HttpMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan TargetRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>The certificate handshake is separate work; it does not need to run every check.</summary>
    private const int CertificateCheckEveryNth = 30;

    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HTTP monitor service started");

        using var timer = new PeriodicTimer(TargetRefreshInterval);

        do
        {
            try
            {
                await SyncWorkersAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not refresh HTTP targets");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));

        foreach (var worker in _workers.Values)
        {
            await worker.StopAsync();
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SyncWorkersAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();

        var targets = await db.Set<HttpTarget>().AsNoTracking().ToListAsync(stoppingToken);
        var wanted = targets.Where(t => t.Enabled).ToDictionary(t => t.Id);

        foreach (var (id, worker) in _workers)
        {
            if (wanted.ContainsKey(id))
            {
                continue;
            }

            if (_workers.TryRemove(id, out _))
            {
                await worker.StopAsync();
                cache.Remove(id);
                alerts.Forget(HttpModule.ModuleId, id.ToString("N"));
            }
        }

        foreach (var (id, target) in wanted)
        {
            if (_workers.TryGetValue(id, out var existing))
            {
                existing.Update(target);
                continue;
            }

            var worker = new Worker(target, RunCheckAsync, logger);
            if (_workers.TryAdd(id, worker))
            {
                worker.Start(stoppingToken);
                logger.LogInformation(
                    "Monitoring {Name} ({Url}) every {Interval}s",
                    target.Name, target.Url, target.CheckIntervalSeconds);
            }
        }
    }

    private async Task RunCheckAsync(HttpTarget target, long checkNumber, CancellationToken ct)
    {
        var result = await checker.CheckAsync(target, ct);

        // Certificate expiry changes daily at most; carry the previous reading in between.
        var previous = cache.Get(target.Id);
        if (checkNumber <= 1 || checkNumber % CertificateCheckEveryNth == 0)
        {
            var (days, subject) = await checker.InspectCertificateAsync(target, ct);
            result = result with { CertificateDaysRemaining = days, CertificateSubject = subject };
        }
        else
        {
            result = result with
            {
                CertificateDaysRemaining = previous?.CertificateDaysRemaining,
                CertificateSubject = previous?.CertificateSubject
            };
        }

        // Slow but answering is not the same as down; say so rather than flattening it to "up".
        if (result.Status == HttpCheckStatus.Up
            && target.SlowResponseMs is { } slowLimit
            && result.ResponseTimeMs >= slowLimit)
        {
            result = result with { Status = HttpCheckStatus.Degraded };
        }

        var (uptime, count) = cache.Record(result);
        result = result with { UptimePercent = uptime, RecentChecks = count };

        var alertTarget = new AlertTarget
        {
            ModuleId = HttpModule.ModuleId,
            TargetId = target.Id.ToString("N"),
            TargetName = target.Name,
            GroupName = string.IsNullOrWhiteSpace(target.GroupName) ? null : target.GroupName
        };

        var outcome = alerts.Evaluate(alertTarget, HttpAlertRules.Evaluate(target, result), DateTimeOffset.UtcNow);

        result = result with
        {
            ActiveAlerts = outcome.Active,
            Severity = outcome.Active.Count == 0 ? Severity.Ok : outcome.Active.Max(a => a.Severity)
        };

        cache.Record(result);

        await publisher.PublishAsync(HttpModule.ModuleId, alertTarget.TargetId, "check", result, ct);

        foreach (var notification in outcome.ToNotify)
        {
            logger.LogInformation(
                "Alert {State} {Rule} on {Target}: {Message}",
                notification.IsCleared ? "cleared" : "raised",
                notification.Alert.RuleId, target.Name, notification.Body);

            await alertSink.PublishAsync(notification, ct);
        }
    }

    private sealed class Worker(HttpTarget target, Func<HttpTarget, long, CancellationToken, Task> check, ILogger logger)
    {
        private readonly CancellationTokenSource _cts = new();
        private volatile HttpTarget _target = target;
        private Task? _loop;
        private long _checkNumber;

        public void Update(HttpTarget updated) => _target = updated;

        public void Start(CancellationToken stoppingToken)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, stoppingToken);
            _loop = Task.Run(() => RunAsync(linked.Token), CancellationToken.None);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _target.CheckIntervalSeconds)));

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await check(_target, Interlocked.Increment(ref _checkNumber), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "HTTP monitor loop error for {Target}", _target.Name);
                }

                var desired = TimeSpan.FromSeconds(Math.Max(5, _target.CheckIntervalSeconds));
                if (desired != timer.Period)
                {
                    timer.Period = desired;
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(ct))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task StopAsync()
        {
            await _cts.CancelAsync();

            if (_loop is not null)
            {
                try
                {
                    await _loop;
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }

            _cts.Dispose();
        }
    }
}
