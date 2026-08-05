using System.Diagnostics;
using Microsoft.Data.SqlClient;
using MssqlRealtime.Core.Agents;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Alerts;
using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Modules.Mssql.Polling;
using MssqlRealtime.Modules.Mssql.Probes;

namespace MssqlRealtime.Agent;

/// <summary>
/// Measures a local SQL Server using exactly the same probes the hub uses.
/// <para>
/// The probes are shared, the orchestration is not: the hub's poller publishes, caches and
/// alerts, none of which an agent should do. Sharing the probes is what guarantees that a
/// server monitored through an agent produces identical numbers to one polled directly.
/// </para>
/// </summary>
public sealed class AgentSqlPoller(IEnumerable<ISqlProbe> probes, ILogger<AgentSqlPoller> logger)
{
    private readonly ISqlProbe[] _probes = probes.OrderBy(p => p.Order).ToArray();
    private readonly Dictionary<Guid, ServerSnapshot> _previous = [];

    public async Task<(ServerSnapshot Snapshot, IReadOnlyList<AlertCandidate> Candidates)> PollAsync(
        AgentSqlTarget target,
        long pollNumber,
        CancellationToken ct)
    {
        var profile = ToProfile(target);
        var builder = new SnapshotBuilder(profile);
        var started = Stopwatch.GetTimestamp();
        var capturedAt = DateTimeOffset.UtcNow;

        await using var connection = new SqlConnection(BuildConnectionString(target));

        try
        {
            await connection.OpenAsync(ct);
            builder.Status = ServerStatus.Online;
        }
        catch (SqlException ex)
        {
            builder.Status = ServerStatus.Offline;
            builder.ErrorMessage = ServerPoller.DescribeSqlError(ex);
            logger.LogWarning("Bağlanılamadı {Name} ({Host}): {Error}", target.Name, target.Host, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            builder.Status = ServerStatus.Error;
            builder.ErrorMessage = ex.Message;
        }

        if (builder.Status == ServerStatus.Online)
        {
            var context = new ProbeContext(profile, connection, builder, pollNumber);

            foreach (var probe in _probes)
            {
                ct.ThrowIfCancellationRequested();

                if (!probe.AppliesTo(profile)
                    || (probe.EveryNthPoll > 1 && pollNumber > 1 && pollNumber % probe.EveryNthPoll != 0))
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
                    builder.AddProbeError(probe.Name, ServerPoller.DescribeSqlError(ex));
                }
                catch (Exception ex)
                {
                    builder.AddProbeError(probe.Name, ex.Message);
                }
            }
        }

        if (_previous.TryGetValue(target.ServerId, out var previous))
        {
            builder.CarryForwardFrom(previous);
        }

        // Rules are evaluated here so the payload carries what was measured against, but the
        // hub re-runs the engine: an agent decides nothing about alerting.
        var candidates = MssqlAlertRules.Evaluate(profile, builder);

        var elapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var snapshot = builder.Build(capturedAt, elapsedMs, []);
        _previous[target.ServerId] = snapshot;

        return (snapshot, candidates);
    }

    /// <summary>
    /// Thresholds are not sent to the agent: it reports measurements, and the hub applies the
    /// user's limits. These defaults only shape the candidate list the hub re-evaluates.
    /// </summary>
    private static ServerProfile ToProfile(AgentSqlTarget target) => new()
    {
        Id = target.ServerId,
        Name = target.Name,
        CustomerName = target.CustomerName,
        Host = target.Host,
        Port = target.Port,
        InitialCatalog = target.InitialCatalog,
        AuthMode = target.IntegratedSecurity ? SqlAuthMode.Integrated : SqlAuthMode.SqlLogin,
        Username = target.Username,
        EncryptConnection = target.EncryptConnection,
        TrustServerCertificate = target.TrustServerCertificate,
        ConnectTimeoutSeconds = target.ConnectTimeoutSeconds,
        CommandTimeoutSeconds = target.CommandTimeoutSeconds,
        PollIntervalSeconds = target.PollIntervalSeconds
    };

    /// <summary>
    /// The agent builds its own connection string: it has no data-protection key ring, and it
    /// deliberately never writes the password anywhere — it lives in memory only.
    /// </summary>
    private static string BuildConnectionString(AgentSqlTarget target)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = target.Port == 1433 ? target.Host : $"{target.Host},{target.Port}",
            InitialCatalog = string.IsNullOrWhiteSpace(target.InitialCatalog) ? "master" : target.InitialCatalog,
            Encrypt = target.EncryptConnection,
            TrustServerCertificate = target.TrustServerCertificate,
            ConnectTimeout = target.ConnectTimeoutSeconds,
            ApplicationName = "MssqlRealtime-Agent",
            Pooling = true,
            MaxPoolSize = 5
        };

        if (target.IntegratedSecurity)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = target.Username ?? string.Empty;
            builder.Password = target.Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }
}
