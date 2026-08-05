using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Modules.Mssql.Probes;

namespace MssqlRealtime.Modules.Mssql.Polling;

/// <summary>
/// Owns one independent polling loop per monitored server, so a slow or unreachable customer
/// never delays anyone else's screen. Servers added, edited, disabled or deleted from the
/// phone are picked up within one refresh interval — no restart.
/// </summary>
public sealed class MssqlPollingService(
    IServiceScopeFactory scopeFactory,
    ServerPoller poller,
    ISnapshotCache cache,
    IAlertEngine alerts,
    IEnumerable<ISqlProbe> probes,
    ILogger<MssqlPollingService> logger) : BackgroundService
{
    /// <summary>How often the profile list itself is re-read from the control-plane database.</summary>
    private static readonly TimeSpan ProfileRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MSSQL polling service started");

        using var timer = new PeriodicTimer(ProfileRefreshInterval);

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
                // Losing the profile list must not kill the loop; the existing workers keep running.
                logger.LogError(ex, "Could not refresh server profiles");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));

        foreach (var worker in _workers.Values)
        {
            await worker.StopAsync();
        }

        logger.LogInformation("MSSQL polling service stopped");
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
        var store = scope.ServiceProvider.GetRequiredService<IServerProfileStore>();
        var profiles = await store.GetAllAsync(stoppingToken);

        var wanted = profiles.Where(p => p.Enabled).ToDictionary(p => p.Id);

        // Stop workers whose server was deleted or disabled, and clear their state so a
        // re-enabled server does not inherit a stale alert or a stale wait-stats baseline.
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
                alerts.Forget(MssqlModule.ModuleId, id.ToString("N"));
                ForgetProbeState(id);
                logger.LogInformation("Stopped polling {Server}", worker.Profile.Name);
            }
        }

        foreach (var (id, profile) in wanted)
        {
            if (_workers.TryGetValue(id, out var existing))
            {
                existing.Update(profile);
                continue;
            }

            var worker = new Worker(profile, poller, logger);
            if (_workers.TryAdd(id, worker))
            {
                worker.Start(stoppingToken);
                logger.LogInformation(
                    "Started polling {Server} ({Host}:{Port}) every {Interval}s",
                    profile.Name, profile.Host, profile.Port, profile.PollIntervalSeconds);
            }
        }
    }

    private void ForgetProbeState(Guid serverId)
    {
        foreach (var probe in probes.OfType<WaitStatsProbe>())
        {
            probe.Forget(serverId);
        }
    }

    /// <summary>One server's loop. Its cadence follows the profile, live.</summary>
    private sealed class Worker(ServerProfile profile, ServerPoller poller, ILogger logger)
    {
        private readonly CancellationTokenSource _cts = new();
        private volatile ServerProfile _profile = profile;
        private Task? _loop;
        private long _pollNumber;

        public ServerProfile Profile => _profile;

        public void Update(ServerProfile updated) => _profile = updated;

        public void Start(CancellationToken stoppingToken)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, stoppingToken);
            _loop = Task.Run(() => RunAsync(linked.Token), CancellationToken.None);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, _profile.PollIntervalSeconds));
            using var timer = new PeriodicTimer(interval);

            while (!ct.IsCancellationRequested)
            {
                var current = _profile;

                try
                {
                    await poller.PollAsync(current, Interlocked.Increment(ref _pollNumber), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // PollAsync already reports connection faults inside the snapshot; anything
                    // reaching here is a bug on our side, and the loop must survive it.
                    logger.LogError(ex, "Polling loop error for {Server}", current.Name);
                }

                // Pick up an interval the user changed from the phone.
                var desired = TimeSpan.FromSeconds(Math.Max(1, _profile.PollIntervalSeconds));
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
