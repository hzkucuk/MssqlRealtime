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
            c.client_net_address                                    AS ClientAddress,
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
            DATEDIFF(second, s.last_request_end_time, GETDATE())    AS IdleSeconds
        FROM sys.dm_exec_sessions s
        LEFT JOIN sys.dm_exec_connections c ON c.session_id = s.session_id
        WHERE s.is_user_process = 1
        ORDER BY s.cpu_time DESC;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

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
            IdleSeconds = r.IdleSeconds ?? 0
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
    }
}
