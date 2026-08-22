using Dapper;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Modules.Mssql.Probes;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// "Who is connected, from which machine, with which application" — the question this
/// product was built to answer. sys.dm_exec_sessions joined to sys.dm_exec_connections.
/// </summary>
public sealed class SessionsProbe : ISqlProbe
{
    public string Name => "sessions";
    public int Order => 20;

    private const string Sql = """
        SELECT
            s.session_id                                            AS SessionId,
            s.login_name                                            AS LoginName,
            s.host_name                                             AS HostName,
            s.program_name                                          AS ProgramName,
            c.ClientAddress                                         AS ClientAddress,
            s.status                                                AS Status,
            DB_NAME(s.database_id)                                  AS DatabaseName,
            s.login_time                                            AS LoginTime,
            s.last_request_end_time                                 AS LastRequestEnd,
            s.cpu_time                                              AS CpuTimeMs,
            s.reads                                                 AS Reads,
            s.writes                                                AS Writes,
            s.logical_reads                                         AS LogicalReads,
            s.memory_usage * 8                                      AS MemoryUsageKb,
            s.open_transaction_count                                AS OpenTransactionCount,
            DATEDIFF(second, s.last_request_end_time, GETDATE())    AS IdleSeconds,
            LEFT(t.text, @MaxLen)                                   AS SqlText
        FROM sys.dm_exec_sessions s
        -- One row per session, always. sys.dm_exec_connections holds one row per
        -- *connection*, and a session can own several: MARS
        -- (MultipleActiveResultSets=True, the default in many EF connection strings)
        -- opens a child connection per active batch. A plain JOIN therefore fans a
        -- single session out into N rows, the payload carries duplicate SessionId
        -- values, and the keyed {#each} in the UI aborts the whole tab. Express
        -- instances rarely show it because few clients enable MARS.
        -- The oldest connection is the parent one; MARS children share its address.
        OUTER APPLY (
            SELECT TOP 1
                c2.client_net_address     AS ClientAddress,
                c2.most_recent_sql_handle AS SqlHandle
            FROM sys.dm_exec_connections c2
            WHERE c2.session_id = s.session_id
            ORDER BY c2.connect_time
        ) c
        -- The last statement this session ran. For a sleeping blocker holding an open
        -- transaction this is the only way to see what it did — it owns no request, so
        -- sys.dm_exec_requests knows nothing about it.
        --
        -- The CASE is the cost control. sys.dm_exec_sql_text is a plan-cache lookup per
        -- row, and on a pooled application most sessions are idle with nothing open; doing
        -- it for all of them every few seconds would be paid on the customer's server for
        -- text nobody reads. Passing NULL makes the function return no row at all.
        OUTER APPLY sys.dm_exec_sql_text(
            CASE
                WHEN s.status <> 'sleeping' OR s.open_transaction_count > 0
                THEN c.SqlHandle
            END) t
        WHERE s.is_user_process = 1
        ORDER BY s.cpu_time DESC;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, new { MaxLen = RequestsProbe.SqlTextMaxLength },
                commandTimeout: context.CommandTimeoutSeconds, cancellationToken: cancellationToken));

        context.Builder.Sessions = rows.Select(r => new SessionInfo
        {
            SessionId = r.SessionId,
            LoginName = r.LoginName,
            HostName = r.HostName,
            // SqlClient pads program_name with trailing spaces on some drivers.
            ProgramName = r.ProgramName?.Trim(),
            ClientAddress = r.ClientAddress,
            Status = r.Status,
            DatabaseName = r.DatabaseName,
            LoginTime = r.LoginTime is null ? null : new DateTimeOffset(r.LoginTime.Value, TimeSpan.Zero),
            LastRequestEnd = r.LastRequestEnd is null ? null : new DateTimeOffset(r.LastRequestEnd.Value, TimeSpan.Zero),
            CpuTimeMs = r.CpuTimeMs,
            Reads = r.Reads,
            Writes = r.Writes,
            LogicalReads = r.LogicalReads,
            MemoryUsageKb = r.MemoryUsageKb,
            OpenTransactionCount = r.OpenTransactionCount,
            IdleSeconds = r.IdleSeconds ?? 0,
            SqlText = r.SqlText?.Trim()
        }).ToList();
    }

    // session_id is smallint and several counters are int, not bigint — settable properties
    // let Dapper widen them instead of failing to find a matching constructor.
    private sealed class Row
    {
        public int SessionId { get; set; }
        public string? LoginName { get; set; }
        public string? HostName { get; set; }
        public string? ProgramName { get; set; }
        public string? ClientAddress { get; set; }
        public string? Status { get; set; }
        public string? DatabaseName { get; set; }
        public DateTime? LoginTime { get; set; }
        public DateTime? LastRequestEnd { get; set; }
        public long CpuTimeMs { get; set; }
        public long Reads { get; set; }
        public long Writes { get; set; }
        public long LogicalReads { get; set; }
        public long MemoryUsageKb { get; set; }
        public int OpenTransactionCount { get; set; }
        public int? IdleSeconds { get; set; }
        public string? SqlText { get; set; }
    }
}
