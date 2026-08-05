using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MssqlRealtime.Core.Agents;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Agents;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Agent;

/// <summary>
/// Runs on the customer's side. Dials out to the hub, receives its work list, measures the
/// local SQL Servers and pushes results up.
/// <para>
/// The whole point is the direction of the connection: nothing has to be opened inbound on
/// the customer's firewall, and no credentials are written to their disk — the agent's only
/// stored secret is its own enrollment key.
/// </para>
/// </summary>
public sealed class AgentWorker(
    AgentOptions options,
    AgentSqlPoller poller,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, TargetWorker> _workers = new();

    private HubConnection? _connection;
    private Guid _agentId;
    private string _configurationRevision = string.Empty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.HubUrl) || string.IsNullOrWhiteSpace(options.EnrollmentKey))
        {
            logger.LogError(
                "Agent yapılandırılmamış. appsettings.json içinde Agent:HubUrl ve Agent:EnrollmentKey doldurulmalı.");
            return;
        }

        var hubUrl = options.HubUrl.TrimEnd('/') + AgentProtocol.HubPath;

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            // A customer's uplink drops; that is normal, not an incident. Keep trying forever
            // with a bounded delay rather than giving up and going silent.
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _connection.On<AgentConfiguration>(AgentProtocol.ConfigurationChanged, async configuration =>
        {
            logger.LogInformation("Yeni yapılandırma alındı: {Count} sunucu", configuration.SqlTargets.Count);
            await ApplyConfigurationAsync(configuration, stoppingToken);
        });

        _connection.Reconnected += async _ =>
        {
            logger.LogInformation("Hub bağlantısı yeniden kuruldu, kayıt yenileniyor");
            await RegisterAsync(stoppingToken);
        };

        _connection.Closed += error =>
        {
            logger.LogWarning(error, "Hub bağlantısı kapandı");
            return Task.CompletedTask;
        };

        await ConnectWithRetryAsync(hubUrl, stoppingToken);

        using var heartbeat = new PeriodicTimer(HeartbeatInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await heartbeat.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                if (_connection.State == HubConnectionState.Connected && _agentId != Guid.Empty)
                {
                    await _connection.InvokeAsync(
                        AgentProtocol.Heartbeat,
                        new AgentHeartbeat
                        {
                            AgentId = _agentId,
                            SentAtUtc = DateTimeOffset.UtcNow,
                            ActiveTargets = _workers.Count
                        },
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat gönderilemedi");
            }
        }

        foreach (var worker in _workers.Values)
        {
            await worker.StopAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private async Task ConnectWithRetryAsync(string hubUrl, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Hub'a bağlanılıyor: {Url}", hubUrl);
                await _connection!.StartAsync(ct);
                await RegisterAsync(ct);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Hub'a bağlanılamadı ({Error}). {Delay} sn sonra yeniden denenecek.",
                    ex.Message, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        try
        {
            var result = await _connection!.InvokeAsync<AgentRegistrationResult>(
                AgentProtocol.Register,
                new AgentRegistration
                {
                    EnrollmentKey = options.EnrollmentKey,
                    MachineName = Environment.MachineName,
                    Version = typeof(AgentWorker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription
                },
                ct);

            if (!result.Accepted)
            {
                // A rejected key will not fix itself; say so loudly and stop measuring.
                logger.LogError("Kayıt reddedildi: {Error}", result.Error);
                return;
            }

            _agentId = result.AgentId;
            logger.LogInformation(
                "Kayıt başarılı: {Name} ({Count} sunucu atanmış)",
                result.AgentName, result.Configuration.SqlTargets.Count);

            await ApplyConfigurationAsync(result.Configuration, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kayıt sırasında hata");
        }
    }

    private async Task ApplyConfigurationAsync(AgentConfiguration configuration, CancellationToken ct)
    {
        if (configuration.Revision == _configurationRevision && _workers.Count > 0)
        {
            return;
        }

        _configurationRevision = configuration.Revision;
        var wanted = configuration.SqlTargets.ToDictionary(t => t.ServerId);

        foreach (var (id, worker) in _workers)
        {
            if (!wanted.ContainsKey(id) && _workers.TryRemove(id, out _))
            {
                await worker.StopAsync();
                logger.LogInformation("İzleme durduruldu: {Name}", worker.Target.Name);
            }
        }

        foreach (var (id, target) in wanted)
        {
            if (_workers.TryGetValue(id, out var existing))
            {
                existing.Update(target);
                continue;
            }

            var worker = new TargetWorker(target, MeasureAsync, logger);
            if (_workers.TryAdd(id, worker))
            {
                worker.Start(ct);
                logger.LogInformation(
                    "İzleme başladı: {Name} ({Host}:{Port}) her {Interval} sn",
                    target.Name, target.Host, target.Port, target.PollIntervalSeconds);
            }
        }
    }

    private async Task MeasureAsync(AgentSqlTarget target, long pollNumber, CancellationToken ct)
    {
        var (snapshot, candidates) = await poller.PollAsync(target, pollNumber, ct);

        if (_connection?.State != HubConnectionState.Connected)
        {
            // Nothing is queued locally on purpose: a snapshot is a point-in-time reading and
            // a five-minute-old one is misleading, not useful. Alerts are the hub's job.
            logger.LogDebug("Bağlantı yok, ölçüm gönderilmedi: {Name}", target.Name);
            return;
        }

        var envelope = new AgentSnapshotEnvelope { Snapshot = snapshot, Candidates = candidates };

        try
        {
            await _connection.InvokeAsync(
                AgentProtocol.PublishSnapshot,
                MssqlAgentSnapshotSink.Kind,
                JsonSerializer.Serialize(envelope, SerializerOptions),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ölçüm gönderilemedi: {Name}", target.Name);
        }
    }

    /// <summary>Never stops trying: an agent that gives up is an agent nobody notices is gone.</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext context) => context.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(10),
            4 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(60)
        };
    }

    private sealed class TargetWorker(
        AgentSqlTarget target,
        Func<AgentSqlTarget, long, CancellationToken, Task> measure,
        ILogger logger)
    {
        private readonly CancellationTokenSource _cts = new();
        private volatile AgentSqlTarget _target = target;
        private Task? _loop;
        private long _pollNumber;

        public AgentSqlTarget Target => _target;

        public void Update(AgentSqlTarget updated) => _target = updated;

        public void Start(CancellationToken stoppingToken)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, stoppingToken);
            _loop = Task.Run(() => RunAsync(linked.Token), CancellationToken.None);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _target.PollIntervalSeconds)));

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await measure(_target, Interlocked.Increment(ref _pollNumber), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ölçüm döngüsü hatası: {Name}", _target.Name);
                }

                var desired = TimeSpan.FromSeconds(Math.Max(1, _target.PollIntervalSeconds));
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

public sealed class AgentOptions
{
    public string HubUrl { get; set; } = string.Empty;
    public string EnrollmentKey { get; set; } = string.Empty;
}
