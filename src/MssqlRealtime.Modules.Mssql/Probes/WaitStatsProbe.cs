using System.Collections.Concurrent;
using Dapper;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// Top waits as a <b>delta</b> between polls, not the cumulative totals.
/// <para>
/// sys.dm_os_wait_stats counts since the last restart, so on a server that has been up for
/// three months the cumulative top waits describe history, not the problem the user is
/// looking at right now. We subtract the previous reading and report what happened in
/// between — which is the only version of this number that belongs on a live screen.
/// </para>
/// </summary>
public sealed class WaitStatsProbe : ISqlProbe
{
    public string Name => "waits";
    public int Order => 60;
    public int EveryNthPoll => 4;

    private const int TopN = 8;

    private readonly ConcurrentDictionary<Guid, Dictionary<string, Reading>> _previous = new();

    /// <summary>
    /// Waits that are idle/benign by design. Leaving these in would put SLEEP_TASK and
    /// friends at the top of every screen forever.
    /// </summary>
    private const string Sql = """
        SELECT
            wait_type            AS WaitType,
            wait_time_ms         AS WaitTimeMs,
            waiting_tasks_count  AS WaitingTasks
        FROM sys.dm_os_wait_stats
        WHERE waiting_tasks_count > 0
          AND wait_type NOT IN (
              N'BROKER_EVENTHANDLER', N'BROKER_RECEIVE_WAITFOR', N'BROKER_TASK_STOP',
              N'BROKER_TO_FLUSH', N'BROKER_TRANSMITTER', N'CHECKPOINT_QUEUE',
              N'CHKPT', N'CLR_AUTO_EVENT', N'CLR_MANUAL_EVENT', N'CLR_SEMAPHORE',
              N'DBMIRROR_DBM_EVENT', N'DBMIRROR_EVENTS_QUEUE', N'DBMIRROR_WORKER_QUEUE',
              N'DBMIRRORING_CMD', N'DIRTY_PAGE_POLL', N'DISPATCHER_QUEUE_SEMAPHORE',
              N'EXECSYNC', N'FSAGENT', N'FT_IFTS_SCHEDULER_IDLE_WAIT', N'FT_IFTSHC_MUTEX',
              N'HADR_CLUSAPI_CALL', N'HADR_FILESTREAM_IOMGR_IOCOMPLETION', N'HADR_LOGCAPTURE_WAIT',
              N'HADR_NOTIFICATION_DEQUEUE', N'HADR_TIMER_TASK', N'HADR_WORK_QUEUE',
              N'KSOURCE_WAKEUP', N'LAZYWRITER_SLEEP', N'LOGMGR_QUEUE', N'MEMORY_ALLOCATION_EXT',
              N'ONDEMAND_TASK_QUEUE', N'PARALLEL_REDO_DRAIN_WORKER', N'PARALLEL_REDO_LOG_CACHE',
              N'PARALLEL_REDO_TRAN_LIST', N'PARALLEL_REDO_WORKER_SYNC', N'PARALLEL_REDO_WORKER_WAIT_WORK',
              N'PREEMPTIVE_XE_GETTARGETSTATE', N'PWAIT_ALL_COMPONENTS_INITIALIZED',
              N'PWAIT_DIRECTLOGCONSUMER_GETNEXT', N'QDS_PERSIST_TASK_MAIN_LOOP_SLEEP',
              N'QDS_ASYNC_QUEUE', N'QDS_CLEANUP_STALE_QUERIES_TASK_MAIN_LOOP_SLEEP',
              N'QDS_SHUTDOWN_QUEUE', N'REDO_THREAD_PENDING_WORK', N'REQUEST_FOR_DEADLOCK_SEARCH',
              N'RESOURCE_QUEUE', N'SERVER_IDLE_CHECK', N'SLEEP_BPOOL_FLUSH', N'SLEEP_DBSTARTUP',
              N'SLEEP_DCOMSTARTUP', N'SLEEP_MASTERDBREADY', N'SLEEP_MASTERMDREADY',
              N'SLEEP_MASTERUPGRADED', N'SLEEP_MSDBSTARTUP', N'SLEEP_SYSTEMTASK', N'SLEEP_TASK',
              N'SLEEP_TEMPDBSTARTUP', N'SNI_HTTP_ACCEPT', N'SOS_WORK_DISPATCHER',
              N'SP_SERVER_DIAGNOSTICS_SLEEP', N'SQLTRACE_BUFFER_FLUSH',
              N'SQLTRACE_INCREMENTAL_FLUSH_SLEEP', N'SQLTRACE_WAIT_ENTRIES',
              N'VDI_CLIENT_OTHER', N'WAIT_FOR_RESULTS', N'WAITFOR', N'WAITFOR_TASKSHUTDOWN',
              N'WAIT_XTP_RECOVERY', N'WAIT_XTP_HOST_WAIT', N'WAIT_XTP_OFFLINE_CKPT_NEW_LOG',
              N'WAIT_XTP_CKPT_CLOSE', N'XE_DISPATCHER_JOIN', N'XE_DISPATCHER_WAIT',
              N'XE_TIMER_EVENT', N'XE_LIVE_TARGET_TVF'
          );
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        var current = rows.ToDictionary(
            r => r.WaitType,
            r => new Reading(r.WaitTimeMs, r.WaitingTasks),
            StringComparer.Ordinal);

        var serverId = context.Profile.Id;

        if (!_previous.TryGetValue(serverId, out var previous))
        {
            // First pass after startup only establishes the baseline; a delta needs two points.
            _previous[serverId] = current;
            return;
        }

        _previous[serverId] = current;

        var deltas = new List<WaitStat>();
        foreach (var (waitType, now) in current)
        {
            if (!previous.TryGetValue(waitType, out var before))
            {
                continue;
            }

            var deltaMs = now.WaitTimeMs - before.WaitTimeMs;
            var deltaTasks = now.WaitingTasks - before.WaitingTasks;

            // Negative means the instance restarted (counters reset) — drop the baseline.
            if (deltaMs < 0 || deltaTasks < 0)
            {
                _previous.TryRemove(serverId, out _);
                return;
            }

            if (deltaMs == 0)
            {
                continue;
            }

            deltas.Add(new WaitStat
            {
                WaitType = waitType,
                WaitTimeMs = deltaMs,
                WaitingTasks = deltaTasks,
                Percentage = 0
            });
        }

        var totalMs = deltas.Sum(d => d.WaitTimeMs);
        context.Builder.TopWaits = deltas
            .OrderByDescending(d => d.WaitTimeMs)
            .Take(TopN)
            .Select(d => d with
            {
                Percentage = totalMs == 0 ? 0 : Math.Round(d.WaitTimeMs * 100.0 / totalMs, 1)
            })
            .ToList();
    }

    /// <summary>Called when a server is removed, so a deleted profile leaks no baseline.</summary>
    public void Forget(Guid serverId) => _previous.TryRemove(serverId, out _);

    private readonly record struct Reading(long WaitTimeMs, long WaitingTasks);

    private sealed class Row
    {
        public string WaitType { get; set; } = string.Empty;
        public long WaitTimeMs { get; set; }
        public long WaitingTasks { get; set; }
    }
}
